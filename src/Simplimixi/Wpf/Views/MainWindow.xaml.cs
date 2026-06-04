using System.Windows;
using System.Windows.Input;
using CvAut.WpfApp.ViewModels;
using Wpf.Ui.Controls;

namespace CvAut.WpfApp.Views
{
    public partial class MainWindow : FluentWindow
    {
        public MainWindow()
        {
            InitializeComponent();
            Loaded += MainWindow_Loaded;
            AppTitleBar.MouseLeftButtonDown += AppTitleBar_MouseLeftButtonDown;
        }

        private void AppTitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ButtonState == MouseButtonState.Pressed)
            {
                DragMove();
            }
        }

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
