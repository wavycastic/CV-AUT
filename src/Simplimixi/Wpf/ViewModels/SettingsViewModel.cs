using CvAut.WpfApp.Services;

namespace CvAut.WpfApp.ViewModels
{
    /// <summary>
    /// ViewModel cho màn hình "Information" (SettingsView.xaml).
    /// Quản lý thông tin bản quyền/an toàn của phần mềm.
    /// </summary>
    public class SettingsViewModel : ViewModelBase
    {
        private readonly IBotService _botService;

        public string SecurityAlertTitle => "Security Alert";

        public string SecurityAlertContent => "This software is currently FREE.\nIf you paid any amount for this current version, please verify the source before continuing.";

        /// <summary>
        /// Khởi tạo một thực thể mới của SettingsViewModel.
        /// </summary>
        /// <param name="botService">Dịch vụ điều khiển bot.</param>
        public SettingsViewModel(IBotService botService)
        {
            _botService = botService;
        }
    }
}
