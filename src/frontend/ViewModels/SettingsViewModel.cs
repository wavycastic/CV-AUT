using CommunityToolkit.Mvvm.ComponentModel;

namespace CvAut.ViewModels
{
    /// <summary>
    /// Settings page view model. Phase 0 skeleton — ADB/config options wired in Phase 2.
    /// </summary>
    public partial class SettingsViewModel : ViewModelBase
    {
        [ObservableProperty]
        private string _title = "Settings";
    }
}
