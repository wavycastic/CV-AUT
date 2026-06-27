using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace CvAut.ViewModels
{
    /// <summary>
    /// Accounts page: CoC account roster with add/edit/delete UI skeleton. Phase 1 ships the
    /// shell + list; Phase 2 wires real account persistence and switching through the backend.
    /// </summary>
    public partial class AccountsViewModel : ViewModelBase
    {
        [ObservableProperty] private string _title = "Accounts";

        public ObservableCollection<AccountItemViewModel> Accounts { get; } = new();

        [ObservableProperty] private AccountItemViewModel? _selectedAccount;

        [RelayCommand]
        private void AddAccount()
        {
            // Phase 1 placeholder — Phase 2 opens an edit dialog and persists.
            Accounts.Add(new AccountItemViewModel { Name = "New account", Village = "Main", Enabled = true });
        }

        [RelayCommand]
        private void EditAccount()
        {
            // Phase 1 placeholder — Phase 2 opens the editor for SelectedAccount.
        }

        [RelayCommand]
        private void DeleteAccount()
        {
            if (SelectedAccount is not null)
            {
                Accounts.Remove(SelectedAccount);
            }
        }
    }

    /// <summary>One row in the accounts list.</summary>
    public sealed partial class AccountItemViewModel : ObservableObject
    {
        [ObservableProperty] private string _name = string.Empty;
        [ObservableProperty] private string _village = "Main";
        [ObservableProperty] private bool _enabled = true;
    }
}
