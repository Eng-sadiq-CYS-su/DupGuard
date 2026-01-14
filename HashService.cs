using System;
using System.IO;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using Blake3;

namespace DupGuard.Services
{
    /// <summary>
    /// Implementation of hash service using BLAKE3 with SHA-256 fallback
    /// </summary>
    public class HashService : IHashService
    {
        private readonly ILogger _logger;
        private readonly bool _useBlake3;

        public HashService(ILogger logger)
        {
            _logger = logger;
            _useBlake3 = IsBlake3Available();
        }

        public async Task<string> ComputePartialHashAsync(string filePath, int bytesToRead, CancellationToken cancellationToken = default)
        {
            try
            {
                using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read, 8192, true);
                var buffer = new byte[Math.Min(bytesToRead, stream.Length)];

                var bytesRead = await stream.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken);
                if (bytesRead == 0)
                    return string.Empty;

                if (_useBlake3)
                {
                    return Blake3.Hasher.Hash(buffer.AsSpan(0, bytesRead)).ToString();
                }
                else
                {
                    using var sha256 = SHA256.Create();
                    var hashBytes = sha256.ComputeHash(buffer, 0, bytesRead);
                    return BitConverter.ToString(hashBytes).Replace("-", "").ToLowerInvariant();
                }
            }
            catch (Exception ex)
            {
                _logger.LogError($"Failed to compute partial hash for {filePath}", ex);
                throw;
            }
        }

        public async Task<string> ComputeFullHashAsync(string filePath, CancellationToken cancellationToken = default)
        {
            try
            {
                if (_useBlake3)
                {
                    return await ComputeBlake3HashAsync(filePath, cancellationToken);
                }
                else
                {
                    return await ComputeSha256HashAsync(filePath, cancellationToken);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError($"Failed to compute full hash for {filePath}", ex);
                throw;
            }
        }

        public string GetHashAlgorithmName()
        {
            return _useBlake3 ? "BLAKE3" : "SHA-256";
        }

        private async Task<string> ComputeBlake3HashAsync(string filePath, CancellationToken cancellationToken)
        {
            using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read, 8192, true);
            using var hasher = Blake3.Hasher.New();

            var buffer = new byte[8192];
            int bytesRead;

            while ((bytesRead = await stream.ReadAsync(buffer, cancellationToken)) > 0)
            {
                hasher.Update(buffer.AsSpan(0, bytesRead));
            }

            return hasher.Finalize().ToString();
        }

        private async Task<string> ComputeSha256HashAsync(string filePath, CancellationToken cancellationToken)
        {
            using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read, 8192, true);
            using var sha256 = SHA256.Create();

            var buffer = new byte[8192];
            int bytesRead;

            while ((bytesRead = await stream.ReadAsync(buffer, cancellationToken)) > 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
                sha256.TransformBlock(buffer, 0, bytesRead, null, 0);
            }

            sha256.TransformFinalBlock(Array.Empty<byte>(), 0, 0);
            return BitConverter.ToString(sha256.Hash!).Replace("-", "").ToLowerInvariant();
        }

        private static bool IsBlake3Available()
        {
            try
            {
                // Test if BLAKE3 is available
                using var hasher = Blake3.Hasher.New();
                hasher.Update(new byte[] { 1, 2, 3 });
                hasher.Finalize();
                return true;
            }
            catch
            {
                return false;
            }
        }
    }
}
