using System.Text.Json;
using System.Threading;

namespace CvAut;

internal interface IAccountSwitcher
{
    string ActiveAccountName { get; }
    AccountConfig[] GetConfiguredAccounts(JsonElement multiConfig);
    int[] GetSelectedVillages(JsonElement multiConfig);
    bool SwitchToAccount(AccountConfig account, CancellationToken token);
}
