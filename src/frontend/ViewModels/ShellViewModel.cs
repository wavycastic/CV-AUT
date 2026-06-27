using CommunityToolkit.Mvvm.ComponentModel;

namespace CvAut.ViewModels
{
    /// <summary>
    /// Shell host view model. Phase 0 skeleton — TopBar/Sidebar/ContentControl wiring is Phase 1.
    /// </summary>
    public partial class ShellViewModel : ViewModelBase
    {
        [ObservableProperty]
        private string _title = "SimpliMixi";
    }
}
