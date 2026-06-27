using CommunityToolkit.Mvvm.ComponentModel;

namespace CvAut.ViewModels
{
    /// <summary>
    /// Logs page view model. Phase 0 skeleton — per-device log stream wired in Phase 2.
    /// </summary>
    public partial class LogsViewModel : ViewModelBase
    {
        [ObservableProperty]
        private string _title = "Logs";
    }
}
