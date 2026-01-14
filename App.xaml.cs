using System;
using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using Services;
using DupGuard.Views;

namespace DupGuard
{
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App : System.Windows.Application
    {
        public static IServiceProvider ServiceProvider { get; private set; } = null!;

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            // Setup dependency injection
            var services = new Microsoft.Extensions.DependencyInjection.ServiceCollection();

            // Register services
            services.AddSingleton<ILogger, Logger>();
            services.AddSingleton<ISettingsService, JsonSettingsService>();
            services.AddSingleton<IHashService, HashService>();
            services.AddSingleton<IFileSystemService, FileSystemService>();
            services.AddSingleton<IScanningService, ScanningService>();

            ServiceProvider = services.BuildServiceProvider();
        }

        protected override void OnExit(ExitEventArgs e)
        {
            // Cleanup resources
            base.OnExit(e);
        }
    }
}
