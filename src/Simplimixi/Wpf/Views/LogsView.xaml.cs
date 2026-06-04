using CvAut.WpfApp.ViewModels;

namespace CvAut.WpfApp.Views
{
    /// <summary>
    /// Logic xử lý (Code-Behind) của LogsView.xaml.
    /// Hiển thị bảng nhật ký chi tiết cho người dùng và cung cấp nút xóa logs nhanh.
    /// </summary>
    public partial class LogsView : System.Windows.Controls.UserControl
    {
        /// <summary>
        /// Khởi tạo một thực thể mới của LogsView.
        /// </summary>
        public LogsView()
        {
            InitializeComponent();
        }

        /// <summary>
        /// Xử lý sự kiện khi nhấn nút Clear Logs trên giao diện.
        /// Gọi phương thức ClearLogs trong ViewModel tương ứng.
        /// </summary>
        private void OnClearClick(object sender, System.Windows.RoutedEventArgs e)
        {
            if (DataContext is LogsViewModel vm)
            {
                vm.ClearLogs();
            }
        }
    }
}
