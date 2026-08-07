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

    public int TryUpgradeWallsFromHome(
        int villageIdx,
        int cycleCount,
        Func<int, bool> ensureHomeBaseFunc,
        CancellationToken token,
        string phase,
        int batchBudget,
        string? runId = null)
    {
        var timer = WallLogger.StartTimer();
        runId ??= WallLogger.GenerateRunId();
        WallUpgradeConfig wallConfig = _configService.GetWallUpgradeConfig(villageIdx);

        WallLogger.LogInfo("home_wall_runner", "start", village: villageIdx, cycle: cycleCount, trigger: phase, batchBudget: batchBudget, batchLimit: wallConfig.BatchLimit, runId: runId, extra: $"enabled={wallConfig.Enabled} gold_threshold={wallConfig.GoldThreshold:N0} elixir_threshold={wallConfig.ElixirThreshold:N0} gold_reserve={wallConfig.GoldReserve:N0} elixir_reserve={wallConfig.ElixirReserve:N0} debug_screenshots={wallConfig.DebugScreenshots}");

        Console.WriteLine($"[WALL DECISION] phase={phase} cycle={cycleCount} enabled={wallConfig.Enabled} home=true gold_start={wallConfig.GoldThreshold:N0} elixir_start={wallConfig.ElixirThreshold:N0} gold_reserve={wallConfig.GoldReserve:N0} elixir_reserve={wallConfig.ElixirReserve:N0} batch_limit={wallConfig.BatchLimit} wall_debug_screenshots={wallConfig.DebugScreenshots} status=check");

        if (!wallConfig.Enabled || batchBudget <= 0)
        {
            string skipReason = batchBudget <= 0 ? "budget_exhausted" : "disabled";
            WallLogger.LogInfo("home_wall_runner", "skip", reason: skipReason, village: villageIdx, cycle: cycleCount, trigger: phase, batchBudget: batchBudget, batchLimit: wallConfig.BatchLimit, runId: runId, elapsedMs: timer.ElapsedMilliseconds);
            Console.WriteLine($"[WALL RESULT] phase={phase} status=skip reason={skipReason}");
            return 0;
        }

        try
        {
            bool homeConfirmed = ensureHomeBaseFunc(20);
            WallLogger.LogInfo("preflight", homeConfirmed ? "ok" : "fail", reason: homeConfirmed ? "home_confirmed" : "home_not_confirmed", village: villageIdx, cycle: cycleCount, trigger: phase, runId: runId, extra: $"home_confirmation={homeConfirmed}");

            if (!homeConfirmed)
            {
                WallLogger.LogInfo("home_wall_runner", "skip", reason: "home_not_confirmed", village: villageIdx, cycle: cycleCount, trigger: phase, runId: runId, elapsedMs: timer.ElapsedMilliseconds);
                Console.WriteLine($"[WALL RESULT] phase={phase} status=skip reason=home_not_confirmed");
                return 0;
            }

            int safeBatchLimit = Math.Min(wallConfig.BatchLimit, batchBudget);
            int upgradedWalls = _wallUpdater.HandleHomeResources(
                wallConfig.GoldThreshold,
                wallConfig.ElixirThreshold,
                wallConfig.GoldReserve,
                wallConfig.ElixirReserve,
                safeBatchLimit,
                wallConfig.DebugScreenshots,
                cycleCount,
                token,
                trigger: phase,
                runId: runId);

            if (upgradedWalls > 0 && _configService.Current.EnableStats)
            {
                _stats.UpdateWallStats(villageIdx, upgradedWalls);
            }

            WallLogger.LogInfo("home_wall_runner", "success", village: villageIdx, cycle: cycleCount, trigger: phase, batchBudget: batchBudget, batchLimit: wallConfig.BatchLimit, runId: runId, elapsedMs: timer.ElapsedMilliseconds, extra: $"upgraded_walls={upgradedWalls}");
            return upgradedWalls;
        }
        catch (Exception ex)
        {
            WallLogger.LogInfo("home_wall_runner", "fail", reason: "exception", village: villageIdx, cycle: cycleCount, trigger: phase, runId: runId, elapsedMs: timer.ElapsedMilliseconds, extra: $"ex_type=\"{ex.GetType().Name}\" ex_msg=\"{ex.Message}\"");
            throw;
        }
    }
}
