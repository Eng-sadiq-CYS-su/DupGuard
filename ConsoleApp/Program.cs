using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Services;

namespace DupGuardConsole
{
    internal static class Program
    {
        private static async Task<int> Main(string[] args)
        {
            try
            {
                if (args.Length == 0 || args.Contains("--help") || args.Contains("-h"))
                {
                    PrintUsage();
                    return 0;
                }

                var directories = new List<string>();
                string? jsonOut = null;
                string? csvOut = null;

                var options = new ScanOptions
                {
                    IncludeSubdirectories = true,
                    ExcludeSystemFiles = true,
                    ExcludeHiddenFiles = true,
                    MinFileSize = 1024,
                    UsePartialHash = true,
                    PartialHashSizeKB = 64
                };

                var serviceProvider = BuildServiceProvider();
                var settingsService = serviceProvider.GetRequiredService<ISettingsService>();
                var settings = await settingsService.LoadAsync();

                if (settings.ScanOptions != null)
                    options = settings.ScanOptions;

                for (int i = 0; i < args.Length; i++)
                {
                    var arg = args[i];

                    if (arg.Equals("--dir", StringComparison.OrdinalIgnoreCase) || arg.Equals("-d", StringComparison.OrdinalIgnoreCase))
                    {
                        if (i + 1 >= args.Length)
                            throw new ArgumentException("Missing value for --dir");

                        directories.Add(args[++i]);
                        continue;
                    }

                    if (arg.Equals("--json", StringComparison.OrdinalIgnoreCase))
                    {
                        if (i + 1 >= args.Length)
                            throw new ArgumentException("Missing value for --json");

                        jsonOut = args[++i];
                        continue;
                    }

                    if (arg.Equals("--csv", StringComparison.OrdinalIgnoreCase))
                    {
                        if (i + 1 >= args.Length)
                            throw new ArgumentException("Missing value for --csv");

                        csvOut = args[++i];
                        continue;
                    }

                    if (arg.Equals("--no-subdirs", StringComparison.OrdinalIgnoreCase))
                    {
                        options.IncludeSubdirectories = false;
                        continue;
                    }

                    if (arg.Equals("--include-hidden", StringComparison.OrdinalIgnoreCase))
                    {
                        options.ExcludeHiddenFiles = false;
                        continue;
                    }

                    if (arg.Equals("--include-system", StringComparison.OrdinalIgnoreCase))
                    {
                        options.ExcludeSystemFiles = false;
                        continue;
                    }

                    if (arg.Equals("--min-size-kb", StringComparison.OrdinalIgnoreCase))
                    {
                        if (i + 1 >= args.Length)
                            throw new ArgumentException("Missing value for --min-size-kb");

                        if (!long.TryParse(args[++i], out var kb) || kb < 0)
                            throw new ArgumentException("Invalid value for --min-size-kb");

                        options.MinFileSize = kb * 1024;
                        continue;
                    }

                    if (arg.Equals("--no-partial-hash", StringComparison.OrdinalIgnoreCase))
                    {
                        options.UsePartialHash = false;
                        continue;
                    }

                    if (arg.Equals("--partial-hash-kb", StringComparison.OrdinalIgnoreCase))
                    {
                        if (i + 1 >= args.Length)
                            throw new ArgumentException("Missing value for --partial-hash-kb");

                        if (!int.TryParse(args[++i], out var kb) || kb <= 0)
                            throw new ArgumentException("Invalid value for --partial-hash-kb");

                        options.PartialHashSizeKB = kb;
                        continue;
                    }

                    if (!arg.StartsWith("-", StringComparison.Ordinal) && Directory.Exists(arg))
                    {
                        directories.Add(arg);
                        continue;
                    }

                    throw new ArgumentException($"Unknown argument: {arg}");
                }

                if (directories.Count == 0)
                {
                    if (!string.IsNullOrWhiteSpace(settings.LastScanPath))
                    {
                        directories.Add(settings.LastScanPath);
                    }
                    else
                    {
                        throw new ArgumentException("No directories provided. Use --dir <path>.");
                    }
                }

                var scanningService = serviceProvider.GetRequiredService<IScanningService>();

                scanningService.ScanProgressChanged += (_, e) =>
                {
                    Console.WriteLine($"[{e.ElapsedTime:hh\\:mm\\:ss}] {e.FilesProcessed}/{e.TotalFiles} | {e.DuplicatesFound} duplicates | {e.CurrentFile}");
                };

                scanningService.DuplicateFound += (_, e) =>
                {
                    // event is fired when a group is created in the scanner
                    Console.WriteLine($"Duplicate group found: {e.DuplicateGroup.FileCount} files, hash={e.DuplicateGroup.Hash}");
                };

                Console.WriteLine("Starting scan...");
                Console.WriteLine($"Directories: {string.Join(", ", directories)}");

                var results = await scanningService.ScanDirectoriesAsync(directories, options);

                Console.WriteLine();
                Console.WriteLine($"Done. Duplicate groups: {results.Count}");

                if (!string.IsNullOrWhiteSpace(jsonOut))
                {
                    var report = results.Select(g => new
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

                    var json = JsonSerializer.Serialize(report, new JsonSerializerOptions
                    {
                        WriteIndented = true
                    });

                    File.WriteAllText(jsonOut!, json);
                    Console.WriteLine($"Report saved: {jsonOut}");
                }

                if (!string.IsNullOrWhiteSpace(csvOut))
                {
                    var sb = new StringBuilder();
                    sb.AppendLine("Hash,FullPath,Size,ModifiedDate");
                    foreach (var g in results)
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

                    File.WriteAllText(csvOut!, sb.ToString(), Encoding.UTF8);
                    Console.WriteLine($"Report saved: {csvOut}");
                }

                // Persist last scan path (single value, overwritten)
                settings.LastScanPath = directories[0];
                await settingsService.SaveAsync(settings);

                return 0;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine(ex.Message);
                return 1;
            }
        }

        private static IServiceProvider BuildServiceProvider()
        {
            var services = new ServiceCollection();

            services.AddSingleton<ILogger, Logger>();
            services.AddSingleton<ISettingsService, JsonSettingsService>();
            services.AddSingleton<IHashService, HashService>();
            services.AddSingleton<IFileSystemService, FileSystemService>();
            services.AddSingleton<IScanningService, ScanningService>();

            return services.BuildServiceProvider();
        }

        private static void PrintUsage()
        {
            Console.WriteLine("DupGuard Console");
            Console.WriteLine();
            Console.WriteLine("Usage:");
            Console.WriteLine("  DupGuardConsole --dir <path> [--dir <path2> ...] [options]");
            Console.WriteLine();
            Console.WriteLine("Options:");
            Console.WriteLine("  --json <file>            Save results as JSON");
            Console.WriteLine("  --csv <file>             Save results as CSV");
            Console.WriteLine("  --no-subdirs             Do not include subdirectories");
            Console.WriteLine("  --include-hidden         Include hidden files");
            Console.WriteLine("  --include-system         Include system files");
            Console.WriteLine("  --min-size-kb <n>         Minimum file size in KB");
            Console.WriteLine("  --no-partial-hash        Disable partial hashing");
            Console.WriteLine("  --partial-hash-kb <n>     Partial hash size in KB (default 64)");
            Console.WriteLine();
            Console.WriteLine("Examples:");
            Console.WriteLine("  DupGuardConsole --dir C:\\Users\\%USERNAME%\\Documents --json report.json");
        }
    }
}
