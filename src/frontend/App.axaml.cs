using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using CvAut.Services;
using CvAut.Services.Emulators;
using CvAut.ViewModels;
using CvAut.ViewModels.Settings;
using CvAut.Views;
using Microsoft.Extensions.DependencyInjection;

namespace CvAut
{
    public partial class App : Application
    {
        public override void Initialize()
        {
            AvaloniaXamlLoader.Load(this);
        }

        public override void OnFrameworkInitializationCompleted()
        {
            if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            {
                var services = new ServiceCollection();
                ConfigureServices(services);
                ServiceProvider provider = services.BuildServiceProvider();

                desktop.MainWindow = new MainWindow
                {
                    DataContext = provider.GetRequiredService<MainWindowViewModel>(),
                };
            }

            base.OnFrameworkInitializationCompleted();
        }

        /// <summary>
        /// DI composition root. Manual registration only (no assembly scanning) to stay
        /// Native AOT / trimming safe.
        /// </summary>
        private static void ConfigureServices(IServiceCollection services)
        {
            // App-scoped state + session manager.
            services.AddSingleton<AppStateService>();
            services.AddSingleton<IConfigStore, ConfigStore>();

            // Opt-in notifications (disabled by default; user pastes their own Discord webhook).
            // The settings provider re-reads on each send so toggling in Settings takes effect live.
            services.AddSingleton<CvAut.Services.Notifications.INotificationService>(sp =>
            {
                var store = sp.GetRequiredService<IConfigStore>();
                return new CvAut.Services.Notifications.DiscordWebhookNotificationService(() => store.LoadNotificationSettings());
            });

            // Device discovery: scanners + orchestrator. Adding a new emulator scanner
            // here is the only registration step — Dashboard and Setup Wizard consume
            // IEmulatorDiscovery and never change.
            services.AddSingleton<IDeviceScanner, CvAut.Services.Emulators.Scanners.AdbConnectedDeviceScanner>();
            services.AddSingleton<IDeviceScanner, CvAut.Services.Emulators.Scanners.BlueStacksScanner>();
            services.AddSingleton<IDeviceScanner, CvAut.Services.Emulators.Scanners.BlueStacksInstallScanner>();
            services.AddSingleton<IDeviceScanner, CvAut.Services.Emulators.Scanners.LdPlayerScanner>();
            services.AddSingleton<IDeviceScanner, CvAut.Services.Emulators.Scanners.MemuScanner>();
            services.AddSingleton<IDeviceScanner, CvAut.Services.Emulators.Scanners.AndroidSdkEmulatorScanner>();
            services.AddSingleton<IDeviceScanner, CvAut.Services.Emulators.Scanners.CommonPortScanner>();
            services.AddSingleton<IEmulatorDiscovery, AdbEmulatorDiscovery>();

            services.AddSingleton<CvAut.Services.Sessions.IDeviceSessionManager, CvAut.Services.Sessions.DeviceSessionManager>();

            // Settings sub-page view models.
            services.AddTransient<MainVillageViewModel>();
            services.AddTransient<NightVillageViewModel>();
            services.AddTransient<ClanGamesViewModel>();
            services.AddTransient<ClanCapitalViewModel>();

            // Page view models.
            services.AddTransient<DashboardViewModel>();
            services.AddTransient<SettingsViewModel>();
            services.AddTransient<LogsViewModel>();
            services.AddTransient<LicenseViewModel>();
            services.AddTransient<AdvancedViewModel>();
            services.AddTransient<SetupWizardViewModel>();

            // Shell host (owns TopBar + Sidebar + page tree).
            services.AddTransient<MainWindowViewModel>();

        }
    }
}
