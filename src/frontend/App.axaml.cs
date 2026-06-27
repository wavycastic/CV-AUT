using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using CvAut.Services;
using CvAut.ViewModels;
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
            services.AddSingleton<AppStateService>();
            services.AddTransient<MainWindowViewModel>();
            // Phase 0 page/shell view models. Resolvable now; wired into the shell in Phase 1.
            services.AddTransient<ShellViewModel>();
            services.AddTransient<DashboardViewModel>();
            services.AddTransient<SettingsViewModel>();
            services.AddTransient<AccountsViewModel>();
            services.AddTransient<AdvancedViewModel>();
            services.AddTransient<LogsViewModel>();
            services.AddTransient<LicenseViewModel>();
        }
    }
}
