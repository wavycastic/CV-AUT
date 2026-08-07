using System;
using System.Threading;
using CvAut.Automation;
using OpenCvSharp;

namespace CvAut
{
    internal partial class CVAutomationFramework
    {
        private MainVillageInteractionService? _mainVillageInteractions;

        private MainVillageInteractionService MainVillageInteractions
            => _mainVillageInteractions ??= new MainVillageInteractionService(_adb, _vision);

        private void RunDonateOnlyCycle(MainVillageConfig config, CancellationToken token)
            => MainVillageInteractions.RunDonateOnlyCycle(
                config,
                token,
                CheckStop,
                InterruptibleSleep);

        private void TryRequestTroopsIfConfigured(MainVillageConfig config, CancellationToken token)
            => MainVillageInteractions.TryRequestTroops(
                config,
                token,
                CheckStop);

        private void TryUseCakeIfConfigured(MainVillageConfig config, CancellationToken token)
            => MainVillageInteractions.TryUseCake(
                config,
                token,
                CheckStop,
                InterruptibleSleep);

        private bool ShouldSmartSurrender(
            DateTime battleStart,
            SmartSurrenderConfig config,
            out string reason)
            => MainVillageInteractions.ShouldSmartSurrender(
                battleStart,
                config,
                out reason);

        private void ExecuteSurrender(string reason, CancellationToken token)
            => MainVillageInteractions.ExecuteSurrender(
                reason,
                token,
                InterruptibleSleep);
    }
}
