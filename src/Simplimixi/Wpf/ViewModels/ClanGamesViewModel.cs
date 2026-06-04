using CvAut.WpfApp.Services;

namespace CvAut.WpfApp.ViewModels
{
    /// <summary>
    /// ViewModel cho màn hình "Clan Games" (ClanGamesView.xaml).
    /// Quản lý dữ liệu và logic tự động hóa các nhiệm vụ Trò chơi Clan.
    /// (Hiện tại đóng vai trò là ViewModel giữ chỗ/mở rộng trong tương lai).
    /// </summary>
    public class ClanGamesViewModel : ViewModelBase
    {
        // Dịch vụ quản lý bot
        private readonly IBotService _botService;

        /// <summary>
        /// Khởi tạo một thực thể mới của ClanGamesViewModel.
        /// </summary>
        /// <param name="botService">Dịch vụ điều khiển bot.</param>
        public ClanGamesViewModel(IBotService botService)
        {
            _botService = botService;
        }
    }
}
