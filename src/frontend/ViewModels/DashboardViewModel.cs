using CommunityToolkit.Mvvm.ComponentModel;

namespace CvAut.ViewModels
{
    /// <summary>
    /// Dashboard page view model. In single mode it renders the active device's
    /// <see cref="DeviceViewModel"/> via <c>DevicePanelView</c>; grid mode (Phase 3) renders the
    /// whole <c>Devices</c> collection. The active device is pushed here by the shell host
    /// (<c>MainWindowViewModel</c>) so the page stays self-contained for binding.
    /// </summary>
    public partial class DashboardViewModel : ViewModelBase
    {
        [ObservableProperty]
        private string _title = "Dashboard";

        [ObservableProperty]
        private DeviceViewModel? _activeDevice;
    }
}
