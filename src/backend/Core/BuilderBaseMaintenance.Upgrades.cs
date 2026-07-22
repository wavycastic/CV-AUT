using System;
using System.Threading;

namespace CvAut
{
    internal sealed partial class BuilderBaseMaintenance
    {
        public bool PerformSuggestedUpgrades(CancellationToken token)
        {
            if (token.IsCancellationRequested) return false;
            Console.WriteLine("[BB_MAINTENANCE] phase=suggested_upgrades status=start");
            return true;
        }

        public bool UpgradeStarLaboratory(string troopName, CancellationToken token)
        {
            if (token.IsCancellationRequested) return false;
            Console.WriteLine($"[BB_MAINTENANCE] phase=star_laboratory status=start troop={troopName}");
            return true;
        }
    }
}
