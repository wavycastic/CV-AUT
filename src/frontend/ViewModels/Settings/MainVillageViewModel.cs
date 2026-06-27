using CommunityToolkit.Mvvm.ComponentModel;

namespace CvAut.ViewModels.Settings
{
    /// <summary>Main Village settings page. Phase 1 skeleton — attack/donate/wall config in Phase 2.</summary>
    public partial class MainVillageViewModel : ViewModelBase
    {
        [ObservableProperty] private string _title = "Main Village";
    }
}
