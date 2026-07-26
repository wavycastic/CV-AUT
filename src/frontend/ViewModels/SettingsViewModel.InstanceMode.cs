using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace CvAut.ViewModels
{
    /// <summary>
    /// Instance mode: the same settings view model rendered as a per-device dialog. In this mode
    /// the tab strip collapses to the device's play mode and Save/Cancel are delegated to the host.
    /// </summary>
    public partial class SettingsViewModel
    {
        [ObservableProperty]
        private bool _isInstanceMode;

        /// <summary>
        /// Raised when the instance-mode Save/Cancel buttons are pressed. The dialog renders this
        /// SettingsViewModel directly (inside MainWindow's overlay, NOT under DashboardView), so
        /// the buttons must bind to commands on THIS view model. DashboardViewModel wires these
        /// events to its own save/cancel flow.
        /// </summary>
        public event System.Action? InstanceSaveRequested;
        public event System.Action? InstanceCancelRequested;

        [RelayCommand]
        private void InstanceSave() => InstanceSaveRequested?.Invoke();

        [RelayCommand]
        private void InstanceCancel() => InstanceCancelRequested?.Invoke();
    }
}
