using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using CvAut.WpfApp.Services;
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

            // Thiết lập sự kiện khi cửa sổ được vẽ và hiển thị hoàn tất
            ContentRendered += MainWindow_ContentRendered;

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
        /// Xử lý sự kiện ContentRendered để thực hiện kiểm tra cập nhật và hiển thị một hộp thoại thông báo
        /// cho người dùng về tính chất miễn phí của phần mềm nhằm chống lừa đảo.
        /// </summary>
        private async void MainWindow_ContentRendered(object? sender, EventArgs e)
        {
            ContentRendered -= MainWindow_ContentRendered;

            await CheckForUpdateAsync();
        }

        private async Task CheckForUpdateAsync()
        {
            try
            {
                var updateDecision = await new UpdateService().CheckForUpdateAsync();
                if (updateDecision == null)
                {
                    return;
                }

                await Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    var updateDialog = new UpdateDialog(updateDecision)
                    {
                        Owner = this
                    };

                    updateDialog.ShowDialog();
                });
            }
            catch (Exception ex)
            {
                UpdateService.WriteLog($"check_failed error=\"{ex}\"");
                Console.WriteLine($"[UPDATE] status=skip reason=check_failed error=\"{ex.Message}\"");
            }
        }

        private void StrategyDockComboBox_Loaded(object sender, RoutedEventArgs e)
        {
            if (sender is ComboBox comboBox)
            {
                ConfigureDropDownToOpenUpward(comboBox);
            }
        }

        private void StrategyDockComboBox_DropDownOpened(object sender, EventArgs e)
        {
            if (sender is ComboBox comboBox)
            {
                ConfigureDropDownToOpenUpward(comboBox);
            }
        }

        private static void ConfigureDropDownToOpenUpward(ComboBox comboBox)
        {
            comboBox.ApplyTemplate();

            if (FindPopup(comboBox) is not { } popup)
            {
                return;
            }

            popup.PlacementTarget = comboBox;
            popup.Placement = PlacementMode.Top;
            popup.VerticalOffset = -4;
            popup.HorizontalOffset = 0;
            popup.PopupAnimation = PopupAnimation.Fade;
        }

        private static Popup? FindPopup(DependencyObject parent)
        {
            if (parent is Popup popup)
            {
                return popup;
            }

            var childCount = VisualTreeHelper.GetChildrenCount(parent);
            for (var i = 0; i < childCount; i++)
            {
                var child = VisualTreeHelper.GetChild(parent, i);
                if (FindPopup(child) is { } childPopup)
                {
                    return childPopup;
                }
            }

            return null;
        }
    }
}
