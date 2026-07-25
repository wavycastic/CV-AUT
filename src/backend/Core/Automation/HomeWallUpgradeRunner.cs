using System;
using System.Threading;

namespace CvAut.Automation;

internal sealed class HomeWallUpgradeRunner
{
    private readonly WallUpdater _wallUpdater;
    private readonly IConfigService _configService;
    private readonly StatsRepository _stats;

    public HomeWallUpgradeRunner(WallUpdater wallUpdater, IConfigService configService, StatsRepository stats)
    {
        _wallUpdater = wallUpdater;
        _configService = configService;
        _stats = stats;
    }

    public void TryUpgradeWallsFromHome(
        int villageIdx,
        int cycleCount,
        Func<int, bool> ensureHomeBaseFunc,
        CancellationToken token,
        string phase)
    {
        WallUpgradeConfig wallConfig = _configService.GetWallUpgradeConfig(villageIdx);
        Console.WriteLine($"[WALL DECISION] phase={phase} cycle={cycleCount} enabled={wallConfig.Enabled} home=true gold_start={wallConfig.GoldThreshold:N0} elixir_start={wallConfig.ElixirThreshold:N0} gold_reserve={wallConfig.GoldReserve:N0} elixir_reserve={wallConfig.ElixirReserve:N0} batch_limit={wallConfig.BatchLimit} wall_debug_screenshots={wallConfig.DebugScreenshots} status=check");

        if (!wallConfig.Enabled)
        {
            Console.WriteLine($"[WALL RESULT] phase={phase} status=skip reason=disabled");
            return;
        }

        if (!ensureHomeBaseFunc(20))
        {
            Console.WriteLine($"[WALL RESULT] phase={phase} status=skip reason=home_not_confirmed");
            return;
        }

        int upgradedWalls = _wallUpdater.HandleHomeResources(
            wallConfig.GoldThreshold,
            wallConfig.ElixirThreshold,
            wallConfig.GoldReserve,
            wallConfig.ElixirReserve,
            wallConfig.BatchLimit,
            wallConfig.DebugScreenshots,
            cycleCount,
            token);
        if (upgradedWalls > 0 && _configService.Current.EnableStats)
        {
            _stats.UpdateWallStats(villageIdx, upgradedWalls);
        }
    }
}
