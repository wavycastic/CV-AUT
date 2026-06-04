using CvAut.WpfApp.Services;

namespace CvAut.WpfApp.ViewModels
{
    public class ClanCapitalViewModel : ViewModelBase
    {
        private readonly IBotService _botService;

        public ClanCapitalViewModel(IBotService botService)
        {
            _botService = botService;
        }
    }
}
