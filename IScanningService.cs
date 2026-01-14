using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Models;

namespace DupGuard.Services
{
    /// <summary>
    /// Service for scanning files and detecting duplicates
    /// </summary>
    public interface IScanningService
    {
        event EventHandler<ScanProgressEventArgs> ScanProgressChanged;
        event EventHandler<DuplicateFoundEventArgs> DuplicateFound;

        Task<List<DuplicateGroup>> ScanDirectoriesAsync(
            IEnumerable<string> directories,
            ScanOptions options,
            CancellationToken cancellationToken = default);

        void CancelScan();
    }

    /// <summary>
    /// Scan configuration options
    /// </summary>
    public class ScanOptions
    {
        public bool IncludeSubdirectories { get; set; } = true;
        public bool ExcludeSystemFiles { get; set; } = true;
        public bool ExcludeHiddenFiles { get; set; } = true;
        public long MinFileSize { get; set; } = 1024; // 1KB minimum
        public long MaxFileSize { get; set; } = long.MaxValue;
        public HashSet<string> ExcludedExtensions { get; } = new();
        public HashSet<string> IncludedExtensions { get; } = new();
        public bool UsePartialHash { get; set; } = true;
        public int PartialHashSizeKB { get; set; } = 64; // 64KB partial hash
        public int MaxThreads { get; set; } = Environment.ProcessorCount;
        public bool LowResourceMode { get; set; } = false;
    }

    /// <summary>
    /// Event args for scan progress updates
    /// </summary>
    public class ScanProgressEventArgs : EventArgs
    {
        public int FilesProcessed { get; set; }
        public int TotalFiles { get; set; }
        public long BytesProcessed { get; set; }
        public long TotalBytes { get; set; }
        public int DuplicatesFound { get; set; }
        public string CurrentFile { get; set; } = string.Empty;
        public TimeSpan ElapsedTime { get; set; }
        public TimeSpan EstimatedTimeRemaining { get; set; }
        public string Status { get; set; } = string.Empty;
    }

    /// <summary>
    /// Event args for duplicate detection
    /// </summary>
    public class DuplicateFoundEventArgs : EventArgs
    {
        public DuplicateGroup DuplicateGroup { get; set; } = null!;
    }
}
