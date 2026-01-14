using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Collections.Specialized;

namespace Models
{
    /// <summary>
    /// Represents a group of duplicate files
    /// </summary>
    public class DuplicateGroup : INotifyPropertyChanged
    {
        public string Hash { get; set; } = string.Empty;
        public ObservableCollection<FileInfo> Files { get; } = new();
        public long TotalSize => Files.Sum(f => f.Size);
        public int FileCount => Files.Count;

        public DuplicateGroup()
        {
            Files.CollectionChanged += OnFilesCollectionChanged;
        }

        public long PotentialSavingsIfKeepNewest
        {
            get
            {
                var newest = GetNewestFile();
                return TotalSize - (newest?.Size ?? 0);
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        private void OnFilesCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(TotalSize)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(FileCount)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(PotentialSavingsIfKeepNewest)));
        }

        public FileInfo? GetNewestFile()
        {
            return Files.OrderByDescending(f => f.ModifiedDate).FirstOrDefault();
        }

        public FileInfo? GetOldestFile()
        {
            return Files.OrderBy(f => f.ModifiedDate).FirstOrDefault();
        }

        public FileInfo? GetLargestFile()
        {
            return Files.OrderByDescending(f => f.Size).FirstOrDefault();
        }

        public FileInfo? GetSmallestFile()
        {
            return Files.OrderBy(f => f.Size).FirstOrDefault();
        }

        public override string ToString()
        {
            return $"{FileCount} files, {TotalSize.ToFileSizeString()}";
        }
    }
}
