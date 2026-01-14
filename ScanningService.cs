using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Models;
using DupGuard.Services;

namespace DupGuard.Services
{
    /// <summary>
    /// Implementation of the scanning service with basic duplicate detection
    /// </summary>
    public class ScanningService : IScanningService
    {
        public event EventHandler<ScanProgressEventArgs>? ScanProgressChanged;
        public event EventHandler<DuplicateFoundEventArgs>? DuplicateFound;

        private readonly IHashService _hashService;
        private readonly ILogger _logger;
        private CancellationTokenSource? _cancellationTokenSource;

        public ScanningService(IHashService hashService, ILogger logger)
        {
            _hashService = hashService;
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

                // Group files by size first
                var sizeGroups = allFiles
                    .Where(f => f.Size >= options.MinFileSize && f.Size <= options.MaxFileSize)
                    .GroupBy(f => f.Size)
                    .Where(g => g.Count() > 1)
                    .ToDictionary(g => g.Key, g => g.ToList());

                var hashGroups = new Dictionary<string, List<Models.FileInfo>>();

                // Process each size group
                foreach (var sizeGroup in sizeGroups)
                {
                    if (_cancellationTokenSource.IsCancellationRequested)
                        break;

                    foreach (var file in sizeGroup.Value)
                    {
                        if (_cancellationTokenSource.IsCancellationRequested)
                            break;

                        processedFiles++;
                        OnScanProgress(processedFiles, totalFiles, file.FullPath, stopwatch.Elapsed);

                        try
                        {
                            string hash;
                            if (options.UsePartialHash)
                            {
                                hash = await _hashService.ComputePartialHashAsync(file.FullPath, options.PartialHashSizeKB * 1024, _cancellationTokenSource.Token);
                            }
                            else
                            {
                                hash = await _hashService.ComputeFullHashAsync(file.FullPath, _cancellationTokenSource.Token);
                            }

                            if (!string.IsNullOrEmpty(hash))
                            {
                                if (!hashGroups.ContainsKey(hash))
                                    hashGroups[hash] = new List<Models.FileInfo>();
                                hashGroups[hash].Add(file);
                            }
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError($"Failed to hash file {file.FullPath}", ex);
                        }
                    }
                }

                // Create duplicate groups
                foreach (var hashGroup in hashGroups.Where(g => g.Value.Count > 1))
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
                OnScanProgress(totalFiles, totalFiles, "Complete", stopwatch.Elapsed);

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
            var searchOption = options.IncludeSubdirectories ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;

            try
            {
                foreach (var filePath in Directory.EnumerateFiles(directory, "*.*", searchOption))
                {
                    try
                    {
                        var fileInfo = new System.IO.FileInfo(filePath);

                        // Apply filters
                        if (options.ExcludeHiddenFiles && (fileInfo.Attributes & FileAttributes.Hidden) != 0)
                            continue;
                        if (options.ExcludeSystemFiles && (fileInfo.Attributes & FileAttributes.System) != 0)
                            continue;
                        if (options.ExcludedExtensions.Contains(fileInfo.Extension.ToLowerInvariant()))
                            continue;
                        if (options.IncludedExtensions.Count > 0 && !options.IncludedExtensions.Contains(fileInfo.Extension.ToLowerInvariant()))
                            continue;

                        var file = new Models.FileInfo
                        {
                            FullPath = filePath,
                            Size = fileInfo.Length,
                            CreatedDate = fileInfo.CreationTime,
                            ModifiedDate = fileInfo.LastWriteTime,
                            Attributes = fileInfo.Attributes
                        };

                        files.Add(file);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning($"Failed to process file {filePath}: {ex.Message}");
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError($"Failed to enumerate directory {directory}", ex);
            }

            return files;
        }

        private void OnScanProgress(int processed, int total, string currentFile, TimeSpan elapsed)
        {
            ScanProgressChanged?.Invoke(this, new ScanProgressEventArgs
            {
                FilesProcessed = processed,
                TotalFiles = total,
                CurrentFile = currentFile,
                ElapsedTime = elapsed,
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
