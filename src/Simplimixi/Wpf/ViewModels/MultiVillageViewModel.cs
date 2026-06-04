using CvAut.WpfApp.Services;

namespace CvAut.WpfApp.ViewModels
{
    /// <summary>
    /// ViewModel cho màn hình "Nhiều Làng" (MultiVillageView.xaml).
    /// Quản lý dữ liệu và thứ tự điều khiển xoay vòng cho nhiều tài khoản làng khác nhau.
    /// (Hiện tại đóng vai trò là ViewModel giữ chỗ/mở rộng trong tương lai).
    /// </summary>
    public class MultiVillageViewModel : ViewModelBase
    {
        // Dịch vụ quản lý bot
        private readonly IBotService _botService;

        /// <summary>
        /// Khởi tạo một thực thể mới của MultiVillageViewModel.
        /// </summary>
        /// <param name="botService">Dịch vụ điều khiển bot.</param>
        public MultiVillageViewModel(IBotService botService)
        {
            _botService = botService;
        }
    }
}
