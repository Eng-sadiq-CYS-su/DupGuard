using System.Collections.Generic;
using System.Threading.Tasks;

namespace Services
{
    /// <summary>
    /// Service for file system operations
    /// </summary>
    public interface IFileSystemService
    {
        Task<IEnumerable<string>> EnumerateFilesAsync(string directory, string searchPattern = "*.*");
        Task<bool> FileExistsAsync(string path);
        Task<long> GetFileSizeAsync(string path);
        Task DeleteFileAsync(string path);
        Task DeleteFileToRecycleBinAsync(string path);

        bool IsSystemFile(Models.FileInfo fileInfo);
        bool IsExcludedFile(Models.FileInfo fileInfo, ScanOptions options);
    }
}
