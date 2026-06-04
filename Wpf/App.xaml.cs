using System.Windows;
using CvAut.WpfApp.Services;
using CvAut.WpfApp.ViewModels;
using CvAut.WpfApp.Views;
using Wpf.Ui.Appearance;
using Wpf.Ui.Controls;

namespace CvAut.WpfApp
{
    public partial class App : System.Windows.Application
    {
        private IBotService? _botService;

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            _botService = new BotService();
            var mainViewModel = new MainViewModel(_botService);

            ApplicationThemeManager.Apply(ApplicationTheme.Dark, WindowBackdropType.Mica);

            var mainWindow = new MainWindow
            {
                DataContext = mainViewModel
            };

            mainWindow.Show();
        }

        protected override void OnExit(ExitEventArgs e)
        {
            _botService?.StopBot();
            base.OnExit(e);
        }
    }
}
