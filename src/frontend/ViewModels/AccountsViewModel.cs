using CommunityToolkit.Mvvm.ComponentModel;

namespace CvAut.ViewModels
{
    /// <summary>
    /// Accounts page view model. Phase 0 skeleton — CoC account roster wired in Phase 2.
    /// </summary>
    public partial class AccountsViewModel : ViewModelBase
    {
        [ObservableProperty]
        private string _title = "Accounts";
    }
}
