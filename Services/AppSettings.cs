using System;

namespace Services
{
    public class AppSettings
    {
        public ScanOptions ScanOptions { get; set; } = new ScanOptions();

        public string LastPickerPath { get; set; } = string.Empty;

        public string LastScanPath { get; set; } = string.Empty;

        public DateTime LastUpdatedUtc { get; set; } = DateTime.UtcNow;
    }
}
