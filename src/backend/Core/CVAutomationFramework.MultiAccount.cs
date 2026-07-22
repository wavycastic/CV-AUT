using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using OpenCvSharp;

namespace CvAut
{
    internal partial class CVAutomationFramework
    {
        private bool ShouldSwitchAccount(
            DateTime slotStart,
            int slotBattleStart,
            int slotClanPointStart,
            int villageIdx,
            bool switchByMinutes,
            int intervalSecs,
            bool switchByBattles,
            int battleLimit,
            bool switchByClanPoints,
            int clanPointLimit,
            out string reason)
        {
            if (switchByBattles && battleLimit > 0 && _sessionBattlesCompleted - slotBattleStart >= battleLimit)
            {
                reason = "battle_limit";
                return true;
            }

            if (switchByClanPoints && clanPointLimit > 0 && ReadClanGamesPoints(villageIdx) - slotClanPointStart >= clanPointLimit)
            {
                reason = "clan_games_points";
                return true;
            }

            if (switchByMinutes && intervalSecs > 0 && (DateTime.Now - slotStart).TotalSeconds >= intervalSecs)
            {
                reason = "minute_limit";
                return true;
            }

            reason = "none";
            return false;
        }

        private bool ShouldSwitchAccount(DateTime sessionStartedAt, int battlesCompleted, JsonElement multiConfig)
        {
            if (multiConfig.ValueKind != JsonValueKind.Object) return false;

            bool enableMulti = GetBoolOrDefault(multiConfig, "enable_multi_account", false);
            if (!enableMulti) return false;

            if (GetBoolOrDefault(multiConfig, "switch_after_battles_enabled", false))
            {
                int limit = GetIntOrDefault(multiConfig, "switch_after_battles", 0);
                if (limit > 0 && battlesCompleted >= limit)
                {
                    Console.WriteLine($"[MULTI-ACC] phase=switch status=trigger reason=battles_limit count={battlesCompleted}");
                    return true;
                }
            }

            if (GetBoolOrDefault(multiConfig, "switch_after_minutes_enabled", false))
            {
                int mins = GetIntOrDefault(multiConfig, "multi_interval_mins", 60);
                if (mins > 0 && (DateTime.Now - sessionStartedAt).TotalMinutes >= mins)
                {
                    Console.WriteLine($"[MULTI-ACC] phase=switch status=trigger reason=time_limit mins={(DateTime.Now - sessionStartedAt).TotalMinutes:F1}");
                    return true;
                }
            }

            return false;
        }

        private static AccountConfig[] GetConfiguredAccounts(JsonElement multiConfig)
        {
            var manager = new AccountManager();
            return manager.GetConfiguredAccounts(multiConfig);
        }

        private static int[] GetSelectedVillages(JsonElement multiConfig)
        {
            return AccountManager.GetSelectedVillages(multiConfig);
        }
    }
}
