using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace CvAut
{
    internal sealed record AccountConfig(
        string Id,
        string Name,
        int ProfileVillage,
        string TargetVillage,
        string TemplatePath,
        bool Enabled,
        string ConfigPreset = "");

    /// <summary>
    /// Quản lý danh sách tài khoản, chuyển đổi tài khoản đa tài khoản (Multi-Account) và theo dõi trạng thái tài khoản đang hoạt động.
    /// </summary>
    internal class AccountManager
    {
        private int _currentVillageIdx = 1;
        private string _activeAccountName = "unknown";

        public int CurrentVillageIdx
        {
            get => _currentVillageIdx;
            set => _currentVillageIdx = Math.Clamp(value, 1, 5);
        }

        public string ActiveAccountName
        {
            get => _activeAccountName;
            set => _activeAccountName = value ?? "unknown";
        }

        public AccountConfig[] GetConfiguredAccounts(JsonElement multiConfig)
        {
            if (multiConfig.ValueKind == JsonValueKind.Object
                && multiConfig.TryGetProperty("accounts", out JsonElement accounts)
                && accounts.ValueKind == JsonValueKind.Array)
            {
                AccountConfig[] parsed = accounts.EnumerateArray()
                    .Select((account, index) => ParseAccountConfig(account, index + 1))
                    .Where(account => account.Enabled)
                    .ToArray();

                if (parsed.Length > 0)
                {
                    return parsed;
                }
            }

            return GetSelectedVillages(multiConfig)
                .Select(village => new AccountConfig(
                    Id: $"acc_{village}",
                    Name: $"Account {village}",
                    ProfileVillage: village,
                    TargetVillage: "main_village",
                    TemplatePath: string.Empty,
                    Enabled: true,
                    ConfigPreset: string.Empty))
                .ToArray();
        }

        public static int[] GetSelectedVillages(JsonElement multiConfig)
        {
            if (multiConfig.ValueKind == JsonValueKind.Object
                && multiConfig.TryGetProperty("selected_villages", out JsonElement selArr)
                && selArr.ValueKind == JsonValueKind.Array)
            {
                int[] selected = selArr.EnumerateArray()
                    .Where(v => v.ValueKind == JsonValueKind.Number)
                    .Select(v => v.GetInt32())
                    .Where(v => v >= 1 && v <= 5)
                    .Distinct()
                    .OrderBy(v => v)
                    .ToArray();

                if (selected.Length > 0)
                {
                    return selected;
                }
            }

            return new[] { 1 };
        }

        private static AccountConfig ParseAccountConfig(JsonElement account, int fallbackIndex)
        {
            int profileVillage = Math.Clamp(GetIntOrDefault(account, "profileVillage", fallbackIndex), 1, 5);
            string id = GetStringOrDefault(account, "id", $"acc_{profileVillage}");
            string name = GetStringOrDefault(account, "name", $"Account {profileVillage}");
            string targetVillage = GetStringOrDefault(account, "targetVillage", "main_village");
            string templatePath = GetStringOrDefault(account, "templatePath", string.Empty);
            bool enabled = GetBoolOrDefault(account, "enabled", true);
            string configPreset = GetStringOrDefault(account, "configPreset", string.Empty);

            return new AccountConfig(id, name, profileVillage, targetVillage, templatePath, enabled, configPreset);
        }

        private static int GetIntOrDefault(JsonElement element, string propName, int defaultValue)
        {
            return element.ValueKind == JsonValueKind.Object && element.TryGetProperty(propName, out JsonElement p) && p.ValueKind == JsonValueKind.Number
                ? p.GetInt32()
                : defaultValue;
        }

        private static string GetStringOrDefault(JsonElement element, string propName, string defaultValue)
        {
            return element.ValueKind == JsonValueKind.Object && element.TryGetProperty(propName, out JsonElement p) && p.ValueKind == JsonValueKind.String
                ? (p.GetString() ?? defaultValue)
                : defaultValue;
        }

        private static bool GetBoolOrDefault(JsonElement element, string propName, bool defaultValue)
        {
            if (element.ValueKind != JsonValueKind.Object || !element.TryGetProperty(propName, out JsonElement p))
                return defaultValue;

            return p.ValueKind switch
            {
                JsonValueKind.True => true,
                JsonValueKind.False => false,
                _ => defaultValue
            };
        }
    }
}
