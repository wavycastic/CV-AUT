using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace CvAut
{
    internal sealed class StarLaboratoryStateStore
    {
        public DateTime? StarLabUpgradeFinishUtc { get; set; }

        public StarLabState LoadStarLabRuntime(int villageIdx)
        {
            try
            {
                string path = GetVillageProfilePath(villageIdx);
                if (!File.Exists(path)) return new StarLabState(-1, -1, 0, null, null);
                JsonNode? root = JsonNode.Parse(File.ReadAllText(path));
                JsonObject? star = root?["builder_base"]?["star_laboratory_state"] as JsonObject;
                if (star == null) return new StarLabState(-1, -1, 0, null, null);
                DateTime? finish = DateTime.TryParse((string?)star["upgrade_finish_utc"], out DateTime parsedFinish) ? parsedFinish.ToUniversalTime() : null;
                DateTime? checkedUtc = DateTime.TryParse((string?)star["last_checked_utc"], out DateTime parsedChecked) ? parsedChecked.ToUniversalTime() : null;
                StarLabUpgradeFinishUtc = finish;
                return new StarLabState((int?)star["x"] ?? -1, (int?)star["y"] ?? -1, (int?)star["level"] ?? 0, finish, checkedUtc);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[BB-MAINT] phase=star_laboratory_state status=fail action=load reason=\"{ex.Message}\"");
                return new StarLabState(-1, -1, 0, null, null);
            }
        }

        public void RecordStarLabFinish(int villageIdx, DateTime finishUtc, string reason)
        {
            StarLabUpgradeFinishUtc = finishUtc.ToUniversalTime();
            StarLabState state = LoadStarLabRuntime(villageIdx) with { UpgradeFinishUtc = StarLabUpgradeFinishUtc, LastCheckedUtc = DateTime.UtcNow };
            SaveStarLabRuntime(villageIdx, state);
            Console.WriteLine($"[BB-MAINT] phase=star_laboratory_state status=saved reason={reason} finish_utc=\"{StarLabUpgradeFinishUtc:O}\"");
        }

        public static string GetVillageProfilePath(int villageIdx)
        {
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SimpliMixi", "profiles", $"Village_{Math.Clamp(villageIdx, 1, 5)}.json");
        }

        public static void SaveStarLabRuntime(int villageIdx, StarLabState state)
        {
            string path = GetVillageProfilePath(villageIdx);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            JsonObject root = File.Exists(path) ? JsonNode.Parse(File.ReadAllText(path)) as JsonObject ?? new JsonObject() : new JsonObject();
            JsonObject builderBase = root["builder_base"] as JsonObject ?? new JsonObject();
            root["builder_base"] = builderBase;
            builderBase["star_laboratory_state"] = new JsonObject
            {
                ["x"] = state.X,
                ["y"] = state.Y,
                ["level"] = state.Level,
                ["upgrade_finish_utc"] = state.UpgradeFinishUtc?.ToUniversalTime().ToString("O") ?? string.Empty,
                ["last_checked_utc"] = state.LastCheckedUtc?.ToUniversalTime().ToString("O") ?? string.Empty
            };
            File.WriteAllText(path, root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
        }
    }

    internal sealed record StarLabState(int X, int Y, int Level, DateTime? UpgradeFinishUtc, DateTime? LastCheckedUtc);
}
