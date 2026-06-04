using System.Windows;
using System.Windows.Input;
using CvAut.WpfApp.ViewModels;
using Wpf.Ui.Controls;

namespace CvAut.WpfApp.Views
{
    /// <summary>
    /// Lớp Logic (Code-Behind) của cửa sổ chính MainWindow.xaml.
    /// Kế thừa từ FluentWindow của thư viện Wpf.Ui để cung cấp giao diện Fluent Design chuẩn Windows 11.
    /// </summary>
    public partial class MainWindow : FluentWindow
    {
        /// <summary>
        /// Khởi tạo một thể hiện mới của lớp MainWindow.
        /// Thiết lập các sự kiện Loaded và sự kiện kéo thả TitleBar.
        /// </summary>
        public MainWindow()
        {
            InitializeComponent();
            
            // Thiết lập sự kiện khi cửa sổ được tải hoàn tất
            Loaded += MainWindow_Loaded;
            
            // Cho phép kéo thả cửa sổ thông qua thanh tiêu đề AppTitleBar
            AppTitleBar.MouseLeftButtonDown += AppTitleBar_MouseLeftButtonDown;
        }

        /// <summary>
        /// Xử lý sự kiện kéo chuột trái trên thanh tiêu đề để di chuyển cửa sổ ứng dụng.
        /// </summary>
        private void AppTitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ButtonState == MouseButtonState.Pressed)
            {
                DragMove();
            }
        }

        /// <summary>
        /// Xử lý sự kiện Loaded để hiển thị một hộp thoại (MessageBox) thông báo cho người dùng
        /// về tính chất miễn phí của phần mềm nhằm chống lừa đảo.
        /// </summary>
        private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            var messageBox = new Wpf.Ui.Controls.MessageBox
            {
                Owner = this,
                Title = "Thông báo quan trọng",
                Content = "Phần mềm này hoàn toàn MIỄN PHÍ.\nNếu bạn đã mua nó từ bất kỳ ai khác, điều đó có nghĩa là bạn đã bị lừa đảo!",
                CloseButtonText = "Tôi đã hiểu",
                WindowStartupLocation = WindowStartupLocation.CenterOwner
            };

            await messageBox.ShowDialogAsync();
        }
    }
}
