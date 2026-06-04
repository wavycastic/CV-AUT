using CvAut.WpfApp.Services;

namespace CvAut.WpfApp.ViewModels
{
    public class MultiVillageViewModel : ViewModelBase
    {
        private readonly IBotService _botService;

        public MultiVillageViewModel(IBotService botService)
        {
            _botService = botService;
        }
    }
}
