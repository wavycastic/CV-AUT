using CommunityToolkit.Mvvm.ComponentModel;

namespace CvAut.ViewModels.Settings
{
    /// <summary>Night Village (Builder Base) settings page. Phase 1 skeleton — Phase 2 fills farm modes.</summary>
    public partial class NightVillageViewModel : ViewModelBase
    {
        [ObservableProperty] private string _title = "Night Village";
    }
}
