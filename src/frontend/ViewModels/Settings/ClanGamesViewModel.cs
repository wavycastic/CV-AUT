using CommunityToolkit.Mvvm.ComponentModel;

namespace CvAut.ViewModels.Settings
{
    /// <summary>Clan Games settings page. Phase 1 skeleton — village pick/mission filter in Phase 2.</summary>
    public partial class ClanGamesViewModel : ViewModelBase
    {
        [ObservableProperty] private string _title = "Clan Games";
    }
}
