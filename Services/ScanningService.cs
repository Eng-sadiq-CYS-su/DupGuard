using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Models;

namespace Services
{
    /// <summary>
    /// Implementation of the scanning service with basic duplicate detection
    /// </summary>
    public class ScanningService : IScanningService
    {
        public event EventHandler<ScanProgressEventArgs>? ScanProgressChanged;
        public event EventHandler<DuplicateFoundEventArgs>? DuplicateFound;

        private readonly IHashService _hashService;
        private readonly IFileSystemService _fileSystemService;
        private readonly ILogger _logger;
        private CancellationTokenSource? _cancellationTokenSource;

        public ScanningService(IHashService hashService, IFileSystemService fileSystemService, ILogger logger)
        {
            _hashService = hashService;
            _fileSystemService = fileSystemService;
            _logger = logger;
        }

        public async Task<List<DuplicateGroup>> ScanDirectoriesAsync(
            IEnumerable<string> directories,
            ScanOptions options,
            CancellationToken cancellationToken = default)
        {
            _cancellationTokenSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

            var stopwatch = Stopwatch.StartNew();
            var duplicateGroups = new List<DuplicateGroup>();
            var bytesProcessed = 0L;

            try
            {
                // Enumerate all files
                var allFiles = new List<Models.FileInfo>();
                foreach (var directory in directories)
                {
                    if (_cancellationTokenSource.IsCancellationRequested)
                        break;

                    allFiles.AddRange(EnumerateFiles(directory, options));
                }

                var totalFiles = allFiles.Count;
                var processedFiles = 0;
                var totalBytes = allFiles.Sum(f => f.Size);

                // Group files by size first
                var sizeGroups = allFiles
                    .GroupBy(f => f.Size)
                    .Where(g => g.Count() > 1)
                    .ToDictionary(g => g.Key, g => g.ToList());

                var finalHashGroups = new Dictionary<string, List<Models.FileInfo>>();

                var partialBytesToRead = Math.Max(1, options.PartialHashSizeKB) * 1024;

                // Process each size group
                foreach (var sizeGroup in sizeGroups)
                {
                    if (_cancellationTokenSource.IsCancellationRequested)
                        break;

                    // Stage 1: partial hash (or full hash directly if disabled)
                    var stageHashGroups = new System.Collections.Concurrent.ConcurrentDictionary<string, System.Collections.Concurrent.ConcurrentBag<Models.FileInfo>>();
                    
                    var parallelOptions = new ParallelOptions 
                    { 
                        CancellationToken = _cancellationTokenSource.Token,
                        MaxDegreeOfParallelism = Environment.ProcessorCount
                    };

                    await Parallel.ForEachAsync(sizeGroup.Value, parallelOptions, async (file, ct) =>
                    {
                        if (ct.IsCancellationRequested) return;

                        Interlocked.Increment(ref processedFiles);

                        try
                        {
                            string hash;
                            long currentBytesProcessed = 0;

                            if (options.UsePartialHash)
                            {
                                var bytesForPartial = (int)Math.Min(file.Size, partialBytesToRead);
                                hash = await _hashService.ComputePartialHashAsync(file.FullPath, bytesForPartial, ct);
                                file.PartialHash = hash;
                                currentBytesProcessed = bytesForPartial;
                            }
                            else
                            {
                                hash = await _hashService.ComputeFullHashAsync(file.FullPath, ct);
                                file.FullHash = hash;
                                currentBytesProcessed = file.Size;
                            }

                            Interlocked.Add(ref bytesProcessed, currentBytesProcessed);

                            // We need to throttle this or just fire event. 
                            // Since we throttled the UI, firing often is okay, but thread safety of the event?
                            // Events are just delegate invocations. If the subscriber handles thread safety (which MainViewModel now does with lock/InvokeAsync), it's fine.
                            // But calling Invoke on delegate is synchronous.
                            OnScanProgress(processedFiles, totalFiles, Interlocked.Read(ref bytesProcessed), totalBytes, file.FullPath, stopwatch.Elapsed);

                            if (!string.IsNullOrEmpty(hash))
                            {
                                stageHashGroups.AddOrUpdate(hash, 
                                    new System.Collections.Concurrent.ConcurrentBag<Models.FileInfo> { file },
                                    (k, v) => { v.Add(file); return v; });
                            }
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError($"Failed to hash file {file.FullPath}", ex);
                        }
                    });

                    // Stage 2: full hash only for groups that still have duplicates after partial hash
                    if (options.UsePartialHash)
                    {
                        var groupsToProcess = stageHashGroups.Where(g => g.Value.Count > 1).ToList();
                        
                        await Parallel.ForEachAsync(groupsToProcess, parallelOptions, async (partialGroup, ct) =>
                        {
                            if (ct.IsCancellationRequested) return;

                            foreach (var file in partialGroup.Value)
                            {
                                if (ct.IsCancellationRequested) break;

                                try
                                {
                                    var fullHash = await _hashService.ComputeFullHashAsync(file.FullPath, ct);
                                    file.FullHash = fullHash;
                                    Interlocked.Add(ref bytesProcessed, file.Size);

                                    OnScanProgress(processedFiles, totalFiles, Interlocked.Read(ref bytesProcessed), totalBytes, file.FullPath, stopwatch.Elapsed);

                                    if (!string.IsNullOrEmpty(fullHash))
                                    {
                                         // Just use lock for simple dictionary update here as it is shared across stage 2
                                         lock (finalHashGroups)
                                         {
                                             if (!finalHashGroups.TryGetValue(fullHash, out var list))
                                             {
                                                 list = new List<Models.FileInfo>();
                                                 finalHashGroups[fullHash] = list;
                                             }
                                             list.Add(file);
                                         }
                                    }
                                }
                                catch (Exception ex)
                                {
                                    _logger.LogError($"Failed to full hash file {file.FullPath}", ex);
                                }
                            }
                        });
                    }
                    else
                    {
                        // When partial hashing is disabled, stageHashGroups already contains full hashes
                        foreach (var kvp in stageHashGroups)
                        {
                            if (kvp.Value.Count <= 1)
                                continue;

                            finalHashGroups[kvp.Key] = kvp.Value.ToList();
                        }
                    }
                }

                // Create duplicate groups
                foreach (var hashGroup in finalHashGroups.Where(g => g.Value.Count > 1))
                {
                    var duplicateGroup = new DuplicateGroup
                    {
                        Hash = hashGroup.Key
                    };

                    foreach (var file in hashGroup.Value)
                    {
                        duplicateGroup.Files.Add(file);
                    }

                    duplicateGroups.Add(duplicateGroup);
                    OnDuplicateFound(duplicateGroup);
                }

                stopwatch.Stop();
                OnScanProgress(totalFiles, totalFiles, bytesProcessed, totalBytes, "Complete", stopwatch.Elapsed);

                return duplicateGroups;
            }
            catch (Exception ex)
            {
                _logger.LogError("Scan failed", ex);
                throw;
            }
        }

        public void CancelScan()
        {
            _cancellationTokenSource?.Cancel();
        }

        private List<Models.FileInfo> EnumerateFiles(string directory, ScanOptions options)
        {
            var files = new List<Models.FileInfo>();

            var pending = new Stack<string>();
            pending.Push(directory);

            while (pending.Count > 0)
            {
                var currentDir = pending.Pop();

                if (string.IsNullOrWhiteSpace(currentDir))
                    continue;

                if (options.ExcludeSystemFiles && IsSystemDirectoryPath(currentDir))
                    continue;

                IEnumerable<string> filePaths;
                try
                {
                    filePaths = Directory.EnumerateFiles(currentDir, "*.*", SearchOption.TopDirectoryOnly);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning($"Failed to enumerate files in {currentDir}: {ex.Message}");
                    continue;
                }

                foreach (var filePath in filePaths)
                {
                    try
                    {
                        var fileInfo = new System.IO.FileInfo(filePath);

                        var file = new Models.FileInfo
                        {
                            FullPath = filePath,
                            Size = fileInfo.Length,
                            CreatedDate = fileInfo.CreationTime,
                            ModifiedDate = fileInfo.LastWriteTime,
                            Attributes = fileInfo.Attributes
                        };

                        if (_fileSystemService.IsExcludedFile(file, options))
                            continue;

                        files.Add(file);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning($"Failed to process file {filePath}: {ex.Message}");
                    }
                }

                if (!options.IncludeSubdirectories)
                    continue;

                IEnumerable<string> subDirs;
                try
                {
                    subDirs = Directory.EnumerateDirectories(currentDir, "*", SearchOption.TopDirectoryOnly);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning($"Failed to enumerate directories in {currentDir}: {ex.Message}");
                    continue;
                }

                foreach (var sub in subDirs)
                {
                    if (options.ExcludeSystemFiles && IsSystemDirectoryPath(sub))
                        continue;

                    pending.Push(sub);
                }
            }

            return files;
        }

        private static bool IsSystemDirectoryPath(string path)
        {
            try
            {
                path = path.Trim().Replace('/', '\\');
                if (path.StartsWith(@"\\"))
                    return false;

                var windowsDir = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
                var programFilesDir = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
                var programFilesX86Dir = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
                var programDataDir = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);

                if (string.IsNullOrWhiteSpace(windowsDir)) windowsDir = @"C:\\Windows";
                if (string.IsNullOrWhiteSpace(programFilesDir)) programFilesDir = @"C:\\Program Files";
                if (string.IsNullOrWhiteSpace(programFilesX86Dir)) programFilesX86Dir = @"C:\\Program Files (x86)";
                if (string.IsNullOrWhiteSpace(programDataDir)) programDataDir = @"C:\\ProgramData";

                var systemDrive = Path.GetPathRoot(windowsDir) ?? @"C:\\";
                var systemVolumeInfoDir = Path.Combine(systemDrive, "System Volume Information");
                var recycleBinDir = Path.Combine(systemDrive, "$Recycle.Bin");

                static bool StartsWithDir(string fullPath, string dir)
                {
                    if (string.IsNullOrWhiteSpace(dir)) return false;
                    dir = dir.TrimEnd('\\') + "\\";
                    return fullPath.StartsWith(dir, StringComparison.OrdinalIgnoreCase);
                }

                return StartsWithDir(path, windowsDir)
                       || StartsWithDir(path, programFilesDir)
                       || StartsWithDir(path, programFilesX86Dir)
                       || StartsWithDir(path, programDataDir)
                       || StartsWithDir(path, systemVolumeInfoDir)
                       || StartsWithDir(path, recycleBinDir);
            }
            catch
            {
                return false;
            }
        }

        private void OnScanProgress(int processed, int total, long bytesProcessed, long totalBytes, string currentFile, TimeSpan elapsed)
        {
            var estimatedRemaining = TimeSpan.Zero;
            if (processed > 0)
            {
                var avgTicksPerFile = elapsed.Ticks / processed;
                estimatedRemaining = TimeSpan.FromTicks(avgTicksPerFile * Math.Max(0, total - processed));
            }

            ScanProgressChanged?.Invoke(this, new ScanProgressEventArgs
            {
                FilesProcessed = processed,
                TotalFiles = total,
                BytesProcessed = bytesProcessed,
                TotalBytes = totalBytes,
                CurrentFile = currentFile,
                ElapsedTime = elapsed,
                EstimatedTimeRemaining = estimatedRemaining,
                Status = $"Processing {processed}/{total} files"
            });
        }

        private void OnDuplicateFound(DuplicateGroup duplicateGroup)
        {
            DuplicateFound?.Invoke(this, new DuplicateFoundEventArgs
            {
                DuplicateGroup = duplicateGroup
            });
        }
    }
}
