using System;
using System.Threading;

namespace CvAut.Automation;

internal static class BuilderBaseStopPolicy
{
    public static BuilderBaseReportSnapshot ReadDebouncedReport(
        Func<BuilderBaseReportSnapshot> readReport,
        string farmMode,
        bool trophyRangeEnabled,
        int minTrophy,
        int maxTrophy,
        bool haltOnGoldFull,
        bool haltOnElixirFull,
        CancellationToken token,
        Func<int, CancellationToken, bool>? sleepFunc,
        out bool shouldStop,
        out string stopReason)
    {
        shouldStop = false;
        stopReason = "none";

        if (token.IsCancellationRequested)
        {
            return BuilderBaseReportSnapshot.UnknownSnapshot();
        }

        BuilderBaseReportSnapshot report = null!;

        for (int check = 1; check <= 2; check++)
        {
            report = readReport();

            if (!ShouldStopBuilderBaseAttacks(farmMode, report, trophyRangeEnabled, minTrophy, maxTrophy, haltOnGoldFull, haltOnElixirFull, out stopReason))
            {
                shouldStop = false;
                return report;
            }

            bool needsConfirmation = stopReason == "loot_exhausted" || stopReason == "star_bonus_completed";
            if (!needsConfirmation || check == 2)
            {
                shouldStop = true;
                return report;
            }

            Console.WriteLine($"[BB-CS] phase=prepare_attack status=pending reason={stopReason} debouncing={check}/2");
            if (sleepFunc != null && sleepFunc(500, token))
            {
                shouldStop = false;
                return report;
            }
        }

        return report;
    }

    public static bool ShouldStopBuilderBaseAttacks(
        string farmMode,
        BuilderBaseReportSnapshot report,
        bool trophyRangeEnabled,
        int minTrophy,
        int maxTrophy,
        bool haltOnGoldFull,
        bool haltOnElixirFull,
        out string reason)
    {
        if (trophyRangeEnabled && report.Trophy > 0)
        {
            if (farmMode.Equals("drop_trophy", StringComparison.OrdinalIgnoreCase))
            {
                if (report.Trophy <= minTrophy)
                {
                    reason = "trophy_reached_min";
                    return true;
                }
            }
            else
            {
                if (report.Trophy >= maxTrophy)
                {
                    reason = "trophy_reached_max";
                    return true;
                }
            }
        }

        if ((haltOnGoldFull && report.GoldStorageFull) || (haltOnElixirFull && report.ElixirStorageFull))
        {
            reason = "storage_full";
            return true;
        }

        bool isDropTrophy = farmMode.Equals("drop_trophy", StringComparison.OrdinalIgnoreCase);
        bool isTrophy = farmMode.Equals("trophy", StringComparison.OrdinalIgnoreCase) || farmMode.Equals("auto", StringComparison.OrdinalIgnoreCase);

        if (!isDropTrophy && !isTrophy)
        {
            if (farmMode.Equals("star_bonus", StringComparison.OrdinalIgnoreCase)
                && report.Reliable && report.StarBonusKnown && !report.StarBonusAvailable)
            {
                reason = "star_bonus_completed";
                return true;
            }

            if (report.Reliable && report.AttackAvailabilityKnown && !report.AttackAvailable)
            {
                reason = "loot_exhausted";
                return true;
            }
        }

        reason = "none";
        return false;
    }
}
