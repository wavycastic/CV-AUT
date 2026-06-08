using System.Windows;
using CvAut.WpfApp.Services;
using CvAut.WpfApp.ViewModels;
using CvAut.WpfApp.Views;
using Wpf.Ui.Appearance;
using Wpf.Ui.Controls;

namespace CvAut.WpfApp
{
    /// <summary>
    /// Lớp khởi động ứng dụng WPF (App.xaml.cs).
    /// Quản lý vòng đời ứng dụng, thiết lập Theme giao diện, khởi tạo dịch vụ BotService và MainWindow.
    /// </summary>
    public partial class App : System.Windows.Application
    {
        // Thực thể duy nhất (Singleton-like) quản lý hoạt động của Bot
        private IBotService? _botService;

        /// <summary>
        /// Xử lý sự kiện khi ứng dụng bắt đầu khởi chạy.
        /// Thực hiện khởi tạo dịch vụ, ViewModel, áp dụng giao diện tối (Dark Theme Mica) và hiển thị cửa sổ chính.
        /// </summary>
        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            // 1. Khởi tạo dịch vụ BotService và ViewModel chính
            _botService = new BotService();
            var mainViewModel = new MainViewModel(_botService);

            // 2. Áp dụng phong cách tối (Dark Theme) và hiệu ứng kính mờ Mica của thư viện Wpf.Ui
            ApplicationThemeManager.Apply(ApplicationTheme.Dark, WindowBackdropType.Mica);

            // 3. Khởi tạo MainWindow và liên kết dữ liệu DataContext với mainViewModel
            var mainWindow = new MainWindow
            {
                DataContext = mainViewModel
            };

            // 4. Hiển thị cửa sổ chính
            mainWindow.Show();
        }

        /// <summary>
        /// Xử lý sự kiện khi ứng dụng kết thúc thoát.
        /// Đảm bảo luồng chạy của Bot được dừng hoàn toàn và phục hồi tài nguyên.
        /// </summary>
        protected override void OnExit(ExitEventArgs e)
        {
            // Yêu cầu dừng bot an toàn nếu đang chạy
            _botService?.StopBot();
            base.OnExit(e);
        }
    }
}
