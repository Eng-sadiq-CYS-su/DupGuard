using System;
using System.ComponentModel;
using System.IO;

namespace Models
{
    /// <summary>
    /// Represents a file with metadata for duplicate detection
    /// </summary>
    public class FileInfo : IEquatable<FileInfo>, INotifyPropertyChanged
    {
        private bool _isSelected;

        public string FullPath { get; set; } = string.Empty;
        public string FileName => Path.GetFileName(FullPath);
        public string Directory => Path.GetDirectoryName(FullPath) ?? string.Empty;
        public string Extension => Path.GetExtension(FullPath).ToLowerInvariant();
        public long Size { get; set; }
        public DateTime CreatedDate { get; set; }
        public DateTime ModifiedDate { get; set; }
        public FileAttributes Attributes { get; set; }

        // Hash values for different stages
        public string? PartialHash { get; set; }
        public string? FullHash { get; set; }

        // Processing status
        public bool IsProcessed { get; set; }
        public bool IsExcluded { get; set; }
        public string? ExclusionReason { get; set; }

        public bool IsSelected
        {
            get => _isSelected;
            set
            {
                if (_isSelected == value) return;
                _isSelected = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsSelected)));
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        public bool Equals(FileInfo? other)
        {
            return other != null && string.Equals(FullPath, other.FullPath, StringComparison.OrdinalIgnoreCase);
        }

        public override bool Equals(object? obj)
        {
            return Equals(obj as FileInfo);
        }

        public override int GetHashCode()
        {
            return StringComparer.OrdinalIgnoreCase.GetHashCode(FullPath);
        }

        public override string ToString()
        {
            return $"{FileName} ({Size.ToFileSizeString()})";
        }
    }

    /// <summary>
    /// Extension methods for file size formatting
    /// </summary>
    public static class FileSizeExtensions
    {
        private const long KB = 1024;
        private const long MB = KB * 1024;
        private const long GB = MB * 1024;
        private const long TB = GB * 1024;

        public static string ToFileSizeString(this long bytes)
        {
            if (bytes < KB) return $"{bytes} B";
            if (bytes < MB) return $"{bytes / (double)KB:F1} KB";
            if (bytes < GB) return $"{bytes / (double)MB:F1} MB";
            if (bytes < TB) return $"{bytes / (double)GB:F1} GB";
            return $"{bytes / (double)TB:F1} TB";
        }
    }
}
