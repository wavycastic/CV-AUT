using System;

namespace CvAut
{
    internal sealed partial class WallUpdater
    {
        public bool PerformWallUpgrade(WallUpgradeDecision decision)
        {
            if (decision == null || decision.Resource == WallUpgradeResource.None || decision.RequestedCount <= 0) return false;

            Console.WriteLine($"[WALL_UPGRADER] phase=upgrade status=executing resource={decision.Resource} count={decision.RequestedCount}");
            return true;
        }
    }
}
