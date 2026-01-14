using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using System;
using Microsoft.VisualBasic.FileIO;

namespace Services
{
    /// <summary>
    /// Implementation of file system service
    /// </summary>
    public class FileSystemService : IFileSystemService
    {
        private readonly ILogger _logger;

        public FileSystemService(ILogger logger)
        {
            _logger = logger;
        }

        public Task<IEnumerable<string>> EnumerateFilesAsync(string directory, string searchPattern = "*.*")
        {
            return Task.FromResult<IEnumerable<string>>(Directory.EnumerateFiles(directory, searchPattern));
        }

        public Task<bool> FileExistsAsync(string path)
        {
            return Task.FromResult(File.Exists(path));
        }

        public Task<long> GetFileSizeAsync(string path)
        {
            return Task.FromResult(new System.IO.FileInfo(path).Length);
        }

        public Task DeleteFileAsync(string path)
        {
            File.Delete(path);
            return Task.CompletedTask;
        }

        public Task DeleteFileToRecycleBinAsync(string path)
        {
            FileSystem.DeleteFile(path, UIOption.OnlyErrorDialogs, RecycleOption.SendToRecycleBin);
            return Task.CompletedTask;
        }

        public bool IsSystemFile(Models.FileInfo fileInfo)
        {
            if (string.IsNullOrWhiteSpace(fileInfo.FullPath))
                return false;

            var path = fileInfo.FullPath.Trim().Replace('/', '\\');

            // UNC paths are treated as non-system by default
            if (path.StartsWith(@"\\"))
                return false;

            var windowsDir = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
            var programFilesDir = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
            var programFilesX86Dir = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
            var programDataDir = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);

            // Fallback to common defaults if environment APIs return empty.
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
                   || StartsWithDir(path, recycleBinDir)
                   || (fileInfo.Attributes & FileAttributes.System) != 0;
        }

        public bool IsExcludedFile(Models.FileInfo fileInfo, ScanOptions options)
        {
            fileInfo.IsExcluded = false;
            fileInfo.ExclusionReason = null;

            try
            {
                if (options.ExcludeSystemFiles && IsSystemFile(fileInfo))
                {
                    fileInfo.IsExcluded = true;
                    fileInfo.ExclusionReason = "ملف نظام";
                    return true;
                }

                if (options.ExcludeHiddenFiles && (fileInfo.Attributes & FileAttributes.Hidden) != 0)
                {
                    fileInfo.IsExcluded = true;
                    fileInfo.ExclusionReason = "ملف مخفي";
                    return true;
                }

                if (fileInfo.Size < options.MinFileSize || fileInfo.Size > options.MaxFileSize)
                {
                    fileInfo.IsExcluded = true;
                    fileInfo.ExclusionReason = "حجم الملف خارج النطاق";
                    return true;
                }

                var ext = fileInfo.Extension;
                if (options.ExcludedExtensions.Contains(ext))
                {
                    fileInfo.IsExcluded = true;
                    fileInfo.ExclusionReason = "امتداد مستبعد";
                    return true;
                }

                if (options.IncludedExtensions.Count > 0 && !options.IncludedExtensions.Contains(ext))
                {
                    fileInfo.IsExcluded = true;
                    fileInfo.ExclusionReason = "امتداد غير مدعوم";
                    return true;
                }

                return false;
            }
            catch (System.Exception ex)
            {
                _logger.LogWarning($"Failed to evaluate exclusion rules for {fileInfo.FullPath}: {ex.Message}");
                return false;
            }
        }
    }
}
