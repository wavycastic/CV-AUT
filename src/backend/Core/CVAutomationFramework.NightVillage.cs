using System.Text.Json;
using System.Threading;
using CvAut.Automation;

namespace CvAut
{
    internal partial class CVAutomationFramework
    {
        private BuilderBaseEntryCoordinator? _builderBaseEntryCoordinator;

        private BuilderBaseEntryCoordinator BuilderBaseEntry
            => _builderBaseEntryCoordinator ??= new BuilderBaseEntryCoordinator(
                _adb,
                _vision,
                _builderBaseNavigator);

        private bool IsNightVillageMode(int villageIdx)
            => VillageModeResolver.IsNightVillage(
                _configService.Current.MultiAccount,
                _configService.RunSession,
                villageIdx);

        private void DismissBuilderBasePopups(CancellationToken token)
            => BuilderBaseEntry.DismissPopups(
                token,
                CheckStop,
                InterruptibleSleep);

        private bool EnsureBuilderBaseEntry(CancellationToken token)
            => BuilderBaseEntry.EnsureEntry(
                token,
                CheckStop,
                InterruptibleSleep,
                BootRecovery);
    }
}
