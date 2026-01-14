using System;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Services
{
    public class JsonSettingsService : ISettingsService
    {
        private readonly ILogger _logger;
        private readonly SemaphoreSlim _gate = new SemaphoreSlim(1, 1);

        public JsonSettingsService(ILogger logger)
        {
            _logger = logger;
        }

        public async Task<AppSettings> LoadAsync()
        {
            await _gate.WaitAsync().ConfigureAwait(false);
            try
            {
                var path = GetSettingsFilePath();
                if (!File.Exists(path))
                    return new AppSettings();

                var json = await File.ReadAllTextAsync(path).ConfigureAwait(false);
                if (string.IsNullOrWhiteSpace(json))
                    return new AppSettings();

                var settings = JsonSerializer.Deserialize<AppSettings>(json, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });

                return settings ?? new AppSettings();
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"Failed to load settings: {ex.Message}");
                return new AppSettings();
            }
            finally
            {
                _gate.Release();
            }
        }

        public async Task SaveAsync(AppSettings settings)
        {
            await _gate.WaitAsync().ConfigureAwait(false);
            try
            {
                settings.LastUpdatedUtc = DateTime.UtcNow;

                var path = GetSettingsFilePath();
                var dir = Path.GetDirectoryName(path);
                if (!string.IsNullOrWhiteSpace(dir))
                    Directory.CreateDirectory(dir);

                var json = JsonSerializer.Serialize(settings, new JsonSerializerOptions
                {
                    WriteIndented = true
                });

                await File.WriteAllTextAsync(path, json).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"Failed to save settings: {ex.Message}");
            }
            finally
            {
                _gate.Release();
            }
        }

        private static string GetSettingsFilePath()
        {
            var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            return Path.Combine(appData, "DupGuard", "settings.json");
        }
    }
}
