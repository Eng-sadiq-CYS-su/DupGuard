using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;

namespace DupGuard.Services
{
    /// <summary>
    /// Implementation of file system service
    /// </summary>
    public class FileSystemService : IFileSystemService
    {
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
            return Task.FromResult(new FileInfo(path).Length);
        }

        public Task DeleteFileAsync(string path)
        {
            File.Delete(path);
            return Task.CompletedTask;
        }
    }
}
