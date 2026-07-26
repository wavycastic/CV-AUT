using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using CvAut.Services;
using CvAut.Services.Configuration;
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

        private static void ConfigureServices(IServiceCollection services)
        {
            services.AddSingleton<AppStateService>();
            services.AddSingleton<IAppPreferences, JsonAppPreferences>();
            services.AddSingleton<IConfigStore, ConfigStore>();
            services.AddSingleton<IProfileConfigSnapshotProvider, ProfileConfigSnapshotProvider>();

            services.AddSingleton<CvAut.Services.Notifications.INotificationService>(sp =>
            {
                var store = sp.GetRequiredService<IConfigStore>();
                return new CvAut.Services.Notifications.DiscordWebhookNotificationService(() => store.LoadNotificationSettings());
            });

            services.AddSingleton<IDeviceScanner, CvAut.Services.Emulators.Scanners.AdbConnectedDeviceScanner>();
            services.AddSingleton<IDeviceScanner, CvAut.Services.Emulators.Scanners.BlueStacksScanner>();
            services.AddSingleton<IDeviceScanner, CvAut.Services.Emulators.Scanners.BlueStacksInstallScanner>();
            services.AddSingleton<IDeviceScanner, CvAut.Services.Emulators.Scanners.LdPlayerScanner>();
            services.AddSingleton<IDeviceScanner, CvAut.Services.Emulators.Scanners.MemuScanner>();
            services.AddSingleton<IDeviceScanner, CvAut.Services.Emulators.Scanners.AndroidSdkEmulatorScanner>();
            services.AddSingleton<IDeviceScanner, CvAut.Services.Emulators.Scanners.CommonPortScanner>();
            services.AddSingleton<IEmulatorDiscovery, AdbEmulatorDiscovery>();
            services.AddSingleton<CvAut.Services.Sessions.IDeviceSessionManager, CvAut.Services.Sessions.DeviceSessionManager>();

            services.AddTransient<MainVillageViewModel>();
            services.AddTransient<NightVillageViewModel>();
            services.AddTransient<ClanGamesViewModel>();
            services.AddTransient<ClanCapitalViewModel>();
            services.AddTransient<DashboardViewModel>();
            services.AddTransient<SettingsViewModel>();
            services.AddTransient<LogsViewModel>();
            services.AddTransient<LicenseViewModel>();
            services.AddTransient<AdvancedViewModel>();
            services.AddTransient<MainWindowViewModel>();
        }
    }
}
