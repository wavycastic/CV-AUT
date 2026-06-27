using CommunityToolkit.Mvvm.ComponentModel;

namespace CvAut.ViewModels
{
    /// <summary>
    /// Dashboard page view model. Phase 0 skeleton — single-device panel wired in Phase 1/2.
    /// </summary>
    public partial class DashboardViewModel : ViewModelBase
    {
        [ObservableProperty]
        private string _title = "Dashboard";
    }
}
