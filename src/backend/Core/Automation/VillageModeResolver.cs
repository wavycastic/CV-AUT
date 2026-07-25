using System;
using System.Text.Json;
using CvAut.Configuration;

namespace CvAut.Automation;

internal static class VillageModeResolver
{
    public static bool IsNightVillage(
        MultiAccountConfig multiAccount,
        RunSessionConfig runSession,
        int villageIndex)
    {
        if (runSession.PlayMode == VillagePlayMode.NightVillage)
            return true;

        foreach (CvAut.Configuration.AccountConfig account in multiAccount.Accounts)
        {
            if (account.ProfileVillage == villageIndex)
            {
                return account.TargetVillage == VillagePlayMode.NightVillage;
            }
        }

        return false;
    }

    public static bool IsNightVillage(
        JsonElement root,
        RunSessionConfig runSession,
        int villageIndex)
    {
        if (runSession.PlayMode == VillagePlayMode.NightVillage)
            return true;

        JsonElement multiAccount = ConfigManager.GetObjectOrDefault(root, "multi_account");
        if (multiAccount.ValueKind != JsonValueKind.Object
            || !multiAccount.TryGetProperty("accounts", out JsonElement accounts)
            || accounts.ValueKind != JsonValueKind.Array)
        {
            return false;
        }

        foreach (JsonElement account in accounts.EnumerateArray())
        {
            int profileVillage = ConfigManager.GetIntOrDefault(account, "profileVillage", 0);
            if (profileVillage != villageIndex)
                continue;

            string targetVillage = ConfigManager.GetStringOrDefault(
                account,
                "targetVillage",
                "main_village");
            return targetVillage.Equals("night_village", StringComparison.OrdinalIgnoreCase);
        }

        return false;
    }
}
