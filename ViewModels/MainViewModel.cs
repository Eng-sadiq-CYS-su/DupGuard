using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Forms;
using System.Windows.Input;
using System.Windows.Data;
using Models;
using Services;
using DupGuard.Utilities;
using Microsoft.Extensions.DependencyInjection;

namespace DupGuard.ViewModels
{
    /// <summary>
    /// Main view model for the duplicate file detector
    /// </summary>
    public class MainViewModel : INotifyPropertyChanged
    {
        // PropertyChanged is likely implemented in a base class or further down, or I added it twice.
        // Let's assume I added it near the top.


        private readonly IScanningService _scanningService;
        private readonly IFileSystemService _fileSystemService;
        private readonly ISettingsService _settingsService;
        private readonly ILogger _logger;
        private CancellationTokenSource? _cancellationTokenSource;

        private AppSettings _settings = new AppSettings();

        private bool _isScanning;
        private string _statusMessage = "جاهز للبدء";
        private int _filesProcessed;
        private int _totalFiles;
        private int _duplicatesFound;
        private string _currentFile = string.Empty;
        private double _progressPercentage;
        private TimeSpan _elapsedTime;
        private TimeSpan _estimatedTimeRemaining;

        private string _resultsSearchText = string.Empty;
        private string _resultsExtensionFilter = string.Empty;
        private string _resultsMinSizeKb = string.Empty;

        private bool _includeSubdirectories = true;
        private bool _excludeSystemFiles = true;
        private bool _excludeHiddenFiles = true;
        private int _minFileSize = 1; // KB
        private bool _usePartialHash = true;
        private int _partialHashSizeKb = 64;
        private bool _lowResourceMode;

        private string? _selectedDrive;

        public ObservableCollection<string> FileSizeUnits { get; } = new ObservableCollection<string> { "KB", "MB", "GB" };

        private string _selectedFileSizeUnit = "KB";
        public string SelectedFileSizeUnit
        {
            get => _selectedFileSizeUnit;
            set
            {
                if (_selectedFileSizeUnit != value)
                {
                    // Convert old value to new unit to keep the rough size the same? 
                    // Or just let the user re-enter? Better UX: just keep the number, meaning the user is changing unit.
                    // But WAIT, MinFileSize is in KB (internal). 
                    // If user had 1000 KB and changes to MB, should it become 1 MB? Or 1000 MB?
                    // Typically changing unit changes the interpretation of the number. 
                    // Let's reset input or keep input? 
                    // Common behavior: The text box number stays, the unit changes, so the ACTUAL value changes.
                    _selectedFileSizeUnit = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(MinFileSizeInput)); // Re-calculate or trigger update
                }
            }
        }

        public double MinFileSizeInput
        {
            get
            {
                // Return value converted FROM KB TO SelectedUnit
                // _minFileSize is in KB
                return _selectedFileSizeUnit switch
                {
                    "MB" => _minFileSize / 1024.0,
                    "GB" => _minFileSize / (1024.0 * 1024.0),
                    _ => _minFileSize
                };
            }
            set
            {
                // Convert FROM SelectedUnit TO KB and store in _minFileSize
                _minFileSize = _selectedFileSizeUnit switch
                {
                    "MB" => (int)(value * 1024),
                    "GB" => (int)(value * 1024 * 1024),
                    _ => (int)value
                };
                OnPropertyChanged();
            }
        }
        
        public ICommand SelectSmartCommand { get; }
        
        // ... in Constructor ...
        // SelectSmartCommand = new RelayCommand(SelectSmart);

        public MainViewModel()
        {
            _scanningService = App.ServiceProvider.GetRequiredService<IScanningService>();
            _fileSystemService = App.ServiceProvider.GetRequiredService<IFileSystemService>();
            _settingsService = App.ServiceProvider.GetRequiredService<ISettingsService>();
            _logger = App.ServiceProvider.GetRequiredService<ILogger>();

            _scanningService.ScanProgressChanged += OnScanProgressChanged;
            _scanningService.DuplicateFound += OnDuplicateFound;

            ScanCommand = new RelayCommand(async () => await StartScanAsync(), () => !IsScanning);
            CancelCommand = new RelayCommand(CancelScan, () => IsScanning);
            SelectDirectoriesCommand = new RelayCommand(SelectDirectories);
            SelectDriveCommand = new RelayCommand(SelectDrive, () => !string.IsNullOrWhiteSpace(SelectedDrive));
            OpenFileCommand = new RelayCommand(async p => await OpenFileAsync(p), p => p is Models.FileInfo);
            OpenContainingFolderCommand = new RelayCommand(p => OpenContainingFolder(p), p => p is Models.FileInfo);
            DeleteFileCommand = new RelayCommand(async p => await DeleteFileAsync(p), p => p is Models.FileInfo);
            DeleteSelectedFilesCommand = new RelayCommand(async _ => await DeleteSelectedFilesAsync());
            ClearResultsFiltersCommand = new RelayCommand(ClearResultsFilters);
            ExportReportCommand = new RelayCommand(async _ => await ExportReportAsync());
            CopyPathCommand = new RelayCommand(p => CopyPath(p), p => p is Models.FileInfo);
            ShowFilePropertiesCommand = new RelayCommand(p => ShowFileProperties(p), p => p is Models.FileInfo);
            SelectSmartCommand = new RelayCommand(SelectSmart);

            DuplicateGroups = new ObservableCollection<DuplicateGroup>();
            DuplicateGroupsView = CollectionViewSource.GetDefaultView(DuplicateGroups);
            DuplicateGroupsView.Filter = FilterDuplicateGroup;

            AvailableDrives = new ObservableCollection<string>(DriveInfo.GetDrives()
                .Where(d => d.IsReady)
                .Select(d => d.RootDirectory.FullName));

            _ = LoadSettingsAsync();
        }

        // Properties
        public ObservableCollection<string> SelectedDirectories { get; } = new();
        public ObservableCollection<DuplicateGroup> DuplicateGroups { get; }
        public ICollectionView DuplicateGroupsView { get; }

        public ObservableCollection<string> AvailableDrives { get; }

        public string? SelectedDrive
        {
            get => _selectedDrive;
            set
            {
                if (_selectedDrive == value) return;
                _selectedDrive = value;
                OnPropertyChanged();
                ((RelayCommand)SelectDriveCommand).RaiseCanExecuteChanged();
            }
        }

        public string ResultsSearchText
        {
            get => _resultsSearchText;
            set
            {
                if (_resultsSearchText == value) return;
                _resultsSearchText = value;
                OnPropertyChanged();
                DuplicateGroupsView.Refresh();
            }
        }

        public string ResultsExtensionFilter
        {
            get => _resultsExtensionFilter;
            set
            {
                if (_resultsExtensionFilter == value) return;
                _resultsExtensionFilter = value;
                OnPropertyChanged();
                DuplicateGroupsView.Refresh();
            }
        }

        public string ResultsMinSizeKb
        {
            get => _resultsMinSizeKb;
            set
            {
                if (_resultsMinSizeKb == value) return;
                _resultsMinSizeKb = value;
                OnPropertyChanged();
                DuplicateGroupsView.Refresh();
            }
        }

        public bool IsScanning
        {
            get => _isScanning;
            set
            {
                _isScanning = value;
                OnPropertyChanged();
                ((RelayCommand)ScanCommand).RaiseCanExecuteChanged();
                ((RelayCommand)CancelCommand).RaiseCanExecuteChanged();
            }
        }

        private async Task OpenFileAsync(object? parameter)
        {
            if (parameter is not Models.FileInfo file)
                return;

            try
            {
                if (!System.IO.File.Exists(file.FullPath))
                {
                    System.Windows.MessageBox.Show("الملف غير موجود", "تنبيه", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);
                    return;
                }

                Process.Start(new ProcessStartInfo(file.FullPath)
                {
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                _logger.LogError("Failed to open file", ex);
                System.Windows.MessageBox.Show($"تعذر فتح الملف: {ex.Message}", "خطأ", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
            }

            await Task.CompletedTask;
        }

        private void OpenContainingFolder(object? parameter)
        {
            if (parameter is not Models.FileInfo file)
                return;

            try
            {
                var args = $"/select,\"{file.FullPath}\"";
                Process.Start(new ProcessStartInfo("explorer.exe", args)
                {
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                _logger.LogError("Failed to open containing folder", ex);
                System.Windows.MessageBox.Show($"تعذر فتح المجلد: {ex.Message}", "خطأ", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
            }
        }

        private async Task DeleteFileAsync(object? parameter)
        {
            if (parameter is not Models.FileInfo file)
                return;

            var owningGroup = DuplicateGroups.FirstOrDefault(g => g.Files.Contains(file));
            if (owningGroup != null && owningGroup.Files.Count <= 1)
            {
                System.Windows.MessageBox.Show("لا يمكن حذف آخر نسخة", "تنبيه", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);
                return;
            }

            var confirm = System.Windows.MessageBox.Show(
                $"هل تريد حذف الملف؟\n\n{file.FullPath}",
                "تأكيد الحذف",
                System.Windows.MessageBoxButton.YesNo,
                System.Windows.MessageBoxImage.Warning);

            if (confirm != System.Windows.MessageBoxResult.Yes)
                return;

            try
            {
                await _fileSystemService.DeleteFileToRecycleBinAsync(file.FullPath);
                RemoveFileFromResults(file);
                StatusMessage = "تم حذف الملف";
            }
            catch (Exception ex)
            {
                _logger.LogError("Failed to delete file", ex);
                System.Windows.MessageBox.Show($"تعذر حذف الملف: {ex.Message}", "خطأ", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
            }
        }

        private void RemoveFileFromResults(Models.FileInfo file)
        {
            var groupsToRemove = DuplicateGroups
                .Where(g => g.Files.Contains(file))
                .ToList();

            foreach (var group in groupsToRemove)
            {
                var existing = group.Files.FirstOrDefault(f => f.Equals(file));
                if (existing != null)
                    group.Files.Remove(existing);

                if (group.Files.Count < 2)
                    DuplicateGroups.Remove(group);
            }

            DuplicateGroupsView.Refresh();
        }

        private bool FilterDuplicateGroup(object obj)
        {
            if (obj is not DuplicateGroup group)
                return true;

            var search = ResultsSearchText?.Trim();
            var ext = ResultsExtensionFilter?.Trim();
            var minKbStr = ResultsMinSizeKb?.Trim();

            long? minBytes = null;
            if (!string.IsNullOrWhiteSpace(minKbStr) && long.TryParse(minKbStr, out var kb) && kb > 0)
                minBytes = kb * 1024;

            string? extNormalized = null;
            if (!string.IsNullOrWhiteSpace(ext))
                extNormalized = ext.StartsWith(".") ? ext.ToLowerInvariant() : "." + ext.ToLowerInvariant();

            return group.Files.Any(f =>
            {
                if (minBytes.HasValue && f.Size < minBytes.Value)
                    return false;

                if (extNormalized != null && f.Extension != extNormalized)
                    return false;

                if (string.IsNullOrWhiteSpace(search))
                    return true;

                return f.FileName.Contains(search, StringComparison.OrdinalIgnoreCase)
                       || f.FullPath.Contains(search, StringComparison.OrdinalIgnoreCase)
                       || f.Directory.Contains(search, StringComparison.OrdinalIgnoreCase);
            });
        }

        private void ClearResultsFilters()
        {
            ResultsSearchText = string.Empty;
            ResultsExtensionFilter = string.Empty;
            ResultsMinSizeKb = string.Empty;
            StatusMessage = "تم مسح الفلاتر";
        }

        private void SelectSmart(object? parameter)
        {
            int count = 0;
            foreach (var group in DuplicateGroups)
            {
                var newest = group.GetNewestFile();
                foreach (var file in group.Files)
                {
                    if (file == newest)
                        file.IsSelected = false;
                    else
                    {
                        file.IsSelected = true;
                        count++;
                    }
                }
            }
            StatusMessage = $"تم تحديد {count} ملف (نسخ قديمة)";
        }

        private async Task DeleteSelectedFilesAsync()
        {
            var selectedFilesAll = DuplicateGroups.SelectMany(g => g.Files.Where(f => f.IsSelected)).ToList();
            var selectedCount = selectedFilesAll.Count;
            
            if (selectedCount == 0)
            {
                System.Windows.MessageBox.Show("لم يتم تحديد أي ملفات", "تنبيه", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Information);
                return;
            }

            long totalSize = selectedFilesAll.Sum(f => f.Size);
            string sizeStr = FormatBytes(totalSize);

            // Safety check: ensure we don't delete all files in a group
            // (Smart users might select all manually)
            // Actually, let's just warn them. Logic below handles "keeping newest" if ALL are selected, but what if they selected all manually?
            // The logic below says: "if selectedFiles.Count >= group.Files.Count ... selectedFiles.Remove(newest)". 
            // So it already protects the newest file if ALL are selected. Good.

            var confirm = System.Windows.MessageBox.Show(
                $"سيتم حذف {selectedCount} ملف.\nالحجم الإجمالي: {sizeStr}\n\n(سيتم النقل إلى سلة المحذوفات)\n\nهل أنت متأكد؟",
                "تأكيد الحذف النهائي",
                System.Windows.MessageBoxButton.YesNo,
                System.Windows.MessageBoxImage.Warning);

            if (confirm != System.Windows.MessageBoxResult.Yes)
                return;

            var groupsSnapshot = DuplicateGroups.ToList();
            int deleted = 0;
            long deletedSize = 0;

            foreach (var group in groupsSnapshot)
            {
                var selectedFiles = group.Files.Where(f => f.IsSelected).ToList();
                if (selectedFiles.Count == 0)
                    continue;

                // Safety: Ensure at least one file remains if they tried to delete all
                if (selectedFiles.Count >= group.Files.Count)
                {
                    var newest = group.GetNewestFile();
                    if (newest != null)
                        selectedFiles.Remove(newest);
                }

                foreach (var file in selectedFiles)
                {
                    await _fileSystemService.DeleteFileToRecycleBinAsync(file.FullPath);
                    group.Files.Remove(file);
                    deleted++;
                    deletedSize += file.Size;
                }

                foreach (var remaining in group.Files)
                    remaining.IsSelected = false;

                if (group.Files.Count < 2)
                    DuplicateGroups.Remove(group);
            }

            DuplicateGroupsView.Refresh();
            StatusMessage = $"تم حذف {deleted} ملف ({FormatBytes(deletedSize)})";
        }

        private async Task ExportReportAsync()
        {
            var dialog = new Microsoft.Win32.SaveFileDialog
            {
                Title = "تصدير تقرير النتائج",
                Filter = "JSON (*.json)|*.json|CSV (*.csv)|*.csv",
                FileName = "DupGuard_Report"
            };

            if (dialog.ShowDialog() != true)
                return;

            var groups = DuplicateGroupsView.Cast<DuplicateGroup>().ToList();
            if (groups.Count == 0)
            {
                System.Windows.MessageBox.Show("لا توجد نتائج لتصديرها", "تنبيه", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Information);
                return;
            }

            try
            {
                var ext = System.IO.Path.GetExtension(dialog.FileName).ToLowerInvariant();

                if (ext == ".csv")
                {
                    var sb = new StringBuilder();
                    sb.AppendLine("Hash,FullPath,Size,ModifiedDate");
                    foreach (var g in groups)
                    {
                        foreach (var f in g.Files)
                        {
                            sb.Append('"').Append(g.Hash.Replace("\"", "\"\"")).Append("\",");
                            sb.Append('"').Append(f.FullPath.Replace("\"", "\"\"")).Append("\",");
                            sb.Append(f.Size).Append(',');
                            sb.Append('"').Append(f.ModifiedDate.ToString("O")).Append('"');
                            sb.AppendLine();
                        }
                    }

                    System.IO.File.WriteAllText(dialog.FileName, sb.ToString(), Encoding.UTF8);
                }
                else
                {
                    var report = groups.Select(g => new
                    {
                        g.Hash,
                        FileCount = g.FileCount,
                        TotalSize = g.TotalSize,
                        PotentialSavingsIfKeepNewest = g.PotentialSavingsIfKeepNewest,
                        Files = g.Files.Select(f => new
                        {
                            f.FullPath,
                            f.Size,
                            f.CreatedDate,
                            f.ModifiedDate
                        }).ToList()
                    }).ToList();

                    var json = JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true });
                    System.IO.File.WriteAllText(dialog.FileName, json, Encoding.UTF8);
                }

                StatusMessage = "تم تصدير التقرير";
                System.Windows.MessageBox.Show("تم حفظ التقرير بنجاح", "تم", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                _logger.LogError("Failed to export report", ex);
                System.Windows.MessageBox.Show($"تعذر تصدير التقرير: {ex.Message}", "خطأ", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
            }

            await Task.CompletedTask;
        }

        private void CopyPath(object? parameter)
        {
            if (parameter is not Models.FileInfo file)
                return;

            try
            {
                System.Windows.Clipboard.SetText(file.FullPath);
                StatusMessage = "تم نسخ المسار";
            }
            catch (Exception ex)
            {
                _logger.LogError("Failed to copy path", ex);
                System.Windows.MessageBox.Show($"تعذر نسخ المسار: {ex.Message}", "خطأ", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
            }
        }

        private void ShowFileProperties(object? parameter)
        {
            if (parameter is not Models.FileInfo file)
                return;

            try
            {
                Process.Start(new ProcessStartInfo(file.FullPath)
                {
                    UseShellExecute = true,
                    Verb = "properties"
                });
            }
            catch (Exception ex)
            {
                _logger.LogError("Failed to show file properties", ex);
                System.Windows.MessageBox.Show($"تعذر فتح خصائص الملف: {ex.Message}", "خطأ", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
            }
        }

        public string StatusMessage
        {
            get => _statusMessage;
            set
            {
                _statusMessage = value;
                OnPropertyChanged();
            }
        }

        public int FilesProcessed
        {
            get => _filesProcessed;
            set
            {
                _filesProcessed = value;
                OnPropertyChanged();
                UpdateProgress();
            }
        }

        public int TotalFiles
        {
            get => _totalFiles;
            set
            {
                _totalFiles = value;
                OnPropertyChanged();
                UpdateProgress();
            }
        }

        public int DuplicatesFound
        {
            get => _duplicatesFound;
            set
            {
                _duplicatesFound = value;
                OnPropertyChanged();
            }
        }

        public string CurrentFile
        {
            get => _currentFile;
            set
            {
                _currentFile = value;
                OnPropertyChanged();
            }
        }

        public double ProgressPercentage
        {
            get => _progressPercentage;
            set
            {
                _progressPercentage = value;
                OnPropertyChanged();
            }
        }

        public TimeSpan ElapsedTime
        {
            get => _elapsedTime;
            set
            {
                _elapsedTime = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(ElapsedTimeDisplay));
            }
        }

        public TimeSpan EstimatedTimeRemaining
        {
            get => _estimatedTimeRemaining;
            set
            {
                _estimatedTimeRemaining = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(EstimatedTimeRemainingDisplay));
            }
        }

        public string ElapsedTimeDisplay => ElapsedTime.ToString(@"hh\:mm\:ss");
        public string EstimatedTimeRemainingDisplay => EstimatedTimeRemaining.ToString(@"hh\:mm\:ss");

        // Scan options
        public bool IncludeSubdirectories
        {
            get => _includeSubdirectories;
            set
            {
                if (_includeSubdirectories == value) return;
                _includeSubdirectories = value;
                OnPropertyChanged();
            }
        }

        public bool ExcludeSystemFiles
        {
            get => _excludeSystemFiles;
            set
            {
                if (_excludeSystemFiles == value) return;
                _excludeSystemFiles = value;
                OnPropertyChanged();
            }
        }

        public bool ExcludeHiddenFiles
        {
            get => _excludeHiddenFiles;
            set
            {
                if (_excludeHiddenFiles == value) return;
                _excludeHiddenFiles = value;
                OnPropertyChanged();
            }
        }

        public int MinFileSize
        {
            get => _minFileSize;
            set
            {
                if (_minFileSize == value) return;
                _minFileSize = value;
                OnPropertyChanged();
            }
        }

        public bool UsePartialHash
        {
            get => _usePartialHash;
            set
            {
                if (_usePartialHash == value) return;
                _usePartialHash = value;
                OnPropertyChanged();
            }
        }

        public int PartialHashSizeKB
        {
            get => _partialHashSizeKb;
            set
            {
                if (_partialHashSizeKb == value) return;
                _partialHashSizeKb = value;
                OnPropertyChanged();
            }
        }

        public bool LowResourceMode
        {
            get => _lowResourceMode;
            set
            {
                if (_lowResourceMode == value) return;
                _lowResourceMode = value;
                OnPropertyChanged();
            }
        }

        // Commands
        public ICommand ScanCommand { get; }
        public ICommand CancelCommand { get; }
        public ICommand SelectDirectoriesCommand { get; }
        public ICommand SelectDriveCommand { get; }
        public ICommand OpenFileCommand { get; }
        public ICommand OpenContainingFolderCommand { get; }
        public ICommand DeleteFileCommand { get; }
        public ICommand DeleteSelectedFilesCommand { get; }
        public ICommand ClearResultsFiltersCommand { get; }
        public ICommand ExportReportCommand { get; }
        public ICommand CopyPathCommand { get; }
        public ICommand ShowFilePropertiesCommand { get; }

        // Methods
        private async Task StartScanAsync()
        {
            if (SelectedDirectories.Count == 0)
            {
                System.Windows.MessageBox.Show("يرجى اختيار مجلد واحد على الأقل للفحص", "تحذير", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);
                return;
            }

            try
            {
                IsScanning = true;
                StatusMessage = "جاري الفحص...";
                ProgressPercentage = 0;
                FilesProcessed = 0;
                TotalFiles = 0;
                ElapsedTime = TimeSpan.Zero;
                EstimatedTimeRemaining = TimeSpan.Zero;
                DuplicateGroups.Clear();

                _cancellationTokenSource = new CancellationTokenSource();
                var token = _cancellationTokenSource.Token;

                var selectedRoot = SelectedDirectories.FirstOrDefault() ?? string.Empty;
                var isDriveRoot = IsDriveRootPath(selectedRoot);

                var options = new ScanOptions
                {
                    IncludeSubdirectories = IncludeSubdirectories,
                    ExcludeSystemFiles = isDriveRoot ? true : ExcludeSystemFiles,
                    ExcludeHiddenFiles = ExcludeHiddenFiles,
                    MinFileSize = MinFileSize * 1024, // Convert KB to bytes
                    UsePartialHash = UsePartialHash,
                    PartialHashSizeKB = PartialHashSizeKB,
                    LowResourceMode = LowResourceMode
                };

                await SaveSettingsAsync(selectedRoot, options);

                // Run scanning in background
                var groups = await Task.Run(async () =>
                {
                    return await _scanningService.ScanDirectoriesAsync(
                        SelectedDirectories,
                        options,
                        token);
                });

                if (token.IsCancellationRequested)
                {
                    StatusMessage = "تم إلغاء الفحص";
                }
                else
                {
                    DuplicateGroups.Clear();
                    foreach (var group in groups.OrderByDescending(g => g.TotalSize))
                    {
                        DuplicateGroups.Add(group);
                    }
                    
                    StatusMessage = $"تم الانتهاء. وجد {DuplicateGroups.Count} مجموعة مكررة ({FormatBytes(DuplicateGroups.Sum(g => g.TotalSize))})";

                    if (DuplicateGroups.Count == 0)
                    {
                        System.Windows.MessageBox.Show("لم يتم العثور على ملفات مكررة", "نتائج الفحص", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Information);
                    }
                }
            }
            catch (OperationCanceledException)
            {
                StatusMessage = "تم إلغاء الفحص";
            }
            catch (Exception ex)
            {
                _logger.LogError("Scan failed", ex);
                StatusMessage = "فشل الفحص: " + ex.Message;
                System.Windows.MessageBox.Show($"حدث خطأ أثناء الفحص: {ex.Message}", "خطأ", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
            }
            finally
            {
                IsScanning = false;
                _cancellationTokenSource?.Dispose();
                _cancellationTokenSource = null;
                ((RelayCommand)ScanCommand).RaiseCanExecuteChanged();
                ((RelayCommand)CancelCommand).RaiseCanExecuteChanged();
            }
        }

        private string FormatBytes(long bytes)
        {
            string[] suffixes = { "B", "KB", "MB", "GB", "TB" };
            int counter = 0;
            decimal number = (decimal)bytes;
            while (Math.Round(number / 1024) >= 1)
            {
                number = number / 1024;
                counter++;
            }
            return string.Format("{0:n1} {1}", number, suffixes[counter]);
        }
        public void CancelScan()
        {
            _scanningService.CancelScan();
            StatusMessage = "تم إلغاء الفحص";
            IsScanning = false;
        }

        private void SelectDirectories()
        {
            var dialog = new FolderBrowserDialog
            {
                Description = "اختر المجلدات التي تريد فحصها",
                ShowNewFolderButton = false,
                SelectedPath = GetPickerStartPath()
            };

            if (dialog.ShowDialog() == DialogResult.OK)
            {
                SelectedDrive = null;
                SelectedDirectories.Clear();
                SelectedDirectories.Add(dialog.SelectedPath);
                _ = SaveLastPickerPathAsync(dialog.SelectedPath);
            }
        }

        private void SelectDrive()
        {
            if (string.IsNullOrWhiteSpace(SelectedDrive))
                return;

            SelectedDirectories.Clear();
            SelectedDirectories.Add(SelectedDrive);

            // As requested: when scanning a full drive, enforce excluding system files/paths.
            ExcludeSystemFiles = true;
            _ = SaveLastPickerPathAsync(SelectedDrive);
        }

        private async Task LoadSettingsAsync()
        {
            try
            {
                var loaded = await _settingsService.LoadAsync();
                _settings = loaded;

                var options = loaded.ScanOptions ?? new ScanOptions();
                IncludeSubdirectories = options.IncludeSubdirectories;
                ExcludeSystemFiles = options.ExcludeSystemFiles;
                ExcludeHiddenFiles = options.ExcludeHiddenFiles;
                MinFileSize = (int)Math.Max(0, options.MinFileSize / 1024);
                UsePartialHash = options.UsePartialHash;
                PartialHashSizeKB = options.PartialHashSizeKB;
                LowResourceMode = options.LowResourceMode;

                if (!string.IsNullOrWhiteSpace(loaded.LastScanPath) && Directory.Exists(loaded.LastScanPath))
                {
                    SelectedDirectories.Clear();
                    SelectedDirectories.Add(loaded.LastScanPath);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"Failed to load settings into UI: {ex.Message}");
            }
        }

        private async Task SaveSettingsAsync(string selectedRoot, ScanOptions options)
        {
            try
            {
                _settings.ScanOptions = options;
                _settings.LastScanPath = selectedRoot;
                await _settingsService.SaveAsync(_settings);
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"Failed to save settings: {ex.Message}");
            }
        }

        private async Task SaveLastPickerPathAsync(string path)
        {
            try
            {
                _settings.LastPickerPath = path;
                await _settingsService.SaveAsync(_settings);
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"Failed to save last picker path: {ex.Message}");
            }
        }

        private string GetPickerStartPath()
        {
            try
            {
                if (!string.IsNullOrWhiteSpace(_settings.LastPickerPath))
                {
                    var p = _settings.LastPickerPath;
                    if (Directory.Exists(p))
                        return p;

                    var root = Path.GetPathRoot(p);
                    if (!string.IsNullOrWhiteSpace(root) && Directory.Exists(root))
                        return root;
                }
            }
            catch
            {
                // ignore
            }

            return Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        }

        private static bool IsDriveRootPath(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
                return false;

            try
            {
                var root = Path.GetPathRoot(path);
                if (string.IsNullOrWhiteSpace(root))
                    return false;

                return string.Equals(Path.GetFullPath(path).TrimEnd('\\') + "\\", root.TrimEnd('\\') + "\\", StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                return false;
            }
        }

        private DateTime _lastUpdate = DateTime.MinValue;
        private readonly object _updateLock = new object();

        private void OnScanProgressChanged(object? sender, ScanProgressEventArgs e)
        {
            // Always update on 100% or if enough time has passed (e.g., 100ms)
            var now = DateTime.UtcNow;
            bool shouldUpdate = false;

            lock (_updateLock)
            {
                if ((now - _lastUpdate).TotalMilliseconds > 100 || e.FilesProcessed == e.TotalFiles)
                {
                    _lastUpdate = now;
                    shouldUpdate = true;
                }
            }

            if (shouldUpdate)
            {
                System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    FilesProcessed = e.FilesProcessed;
                    TotalFiles = e.TotalFiles;
                    DuplicatesFound = e.DuplicatesFound;
                    CurrentFile = e.CurrentFile;
                    ElapsedTime = e.ElapsedTime;
                    EstimatedTimeRemaining = e.EstimatedTimeRemaining;
                    StatusMessage = e.Status;
                }, System.Windows.Threading.DispatcherPriority.Background);
            }
        }

        private void OnDuplicateFound(object? sender, DuplicateFoundEventArgs e)
        {
            DuplicatesFound++;
        }

        private void UpdateProgress()
        {
            if (TotalFiles > 0)
            {
                ProgressPercentage = (double)FilesProcessed / TotalFiles * 100;
            }
            else
            {
                ProgressPercentage = 0;
            }
        }

        // INotifyPropertyChanged implementation
        public event PropertyChangedEventHandler? PropertyChanged;

        protected virtual void OnPropertyChanged([System.Runtime.CompilerServices.CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }

}
