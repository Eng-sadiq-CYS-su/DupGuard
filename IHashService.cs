using System.Threading;
using System.Threading.Tasks;

namespace DupGuard.Services
{
    /// <summary>
    /// Service for computing file hashes
    /// </summary>
    public interface IHashService
    {
        Task<string> ComputePartialHashAsync(string filePath, int bytesToRead, CancellationToken cancellationToken = default);
        Task<string> ComputeFullHashAsync(string filePath, CancellationToken cancellationToken = default);
        string GetHashAlgorithmName();
    }
}
