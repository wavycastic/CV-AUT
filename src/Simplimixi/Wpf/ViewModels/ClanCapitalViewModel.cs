using CvAut.WpfApp.Services;

namespace CvAut.WpfApp.ViewModels
{
    /// <summary>
    /// ViewModel cho màn hình "Clan Capital" (ClanCapitalView.xaml).
    /// Quản lý dữ liệu và logic tự động hóa các hoạt động trong khu vực Thủ đô Clan.
    /// (Hiện tại đóng vai trò là ViewModel giữ chỗ/mở rộng trong tương lai).
    /// </summary>
    public class ClanCapitalViewModel : ViewModelBase
    {
        // Dịch vụ quản lý bot
        private readonly IBotService _botService;

        /// <summary>
        /// Khởi tạo một thực thể mới của ClanCapitalViewModel.
        /// </summary>
        /// <param name="botService">Dịch vụ điều khiển bot.</param>
        public ClanCapitalViewModel(IBotService botService)
        {
            _botService = botService;
        }
    }
}
