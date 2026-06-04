using CvAut.WpfApp.Services;

namespace CvAut.WpfApp.ViewModels
{
    public class ClanGamesViewModel : ViewModelBase
    {
        private readonly IBotService _botService;

        public ClanGamesViewModel(IBotService botService)
        {
            _botService = botService;
        }
    }
}
