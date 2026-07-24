using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace CvAut;

internal sealed class ConfigService : IConfigService
{
    private readonly string _configPath;
    private static bool s_loggedLegacyWallConfigMigration;

    public JsonElement Config { get; private set; }

    public ConfigService(string configPath)
    {
        _configPath = configPath;
        Reload();
    }

    public void Reload()
    {
        LoadConfig(_configPath);
    }

    private void LoadConfig(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                string json = File.ReadAllText(path);
                using var doc = JsonDocument.Parse(json);
                Config = doc.RootElement.Clone();
                return;
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[CONFIG-CS WARNING] phase=init status=fail action=load_config reason=\"{ex.Message}\" details=\"using_defaults\"");
        }

        string defaultJson = @"{
            ""device_connection"": {""host"": ""127.0.0.1"", ""port"": 5556},
            ""farming_thresholds"": {""gold_threshold"": 650000, ""elixir_threshold"": 650000, ""dark_elixir_threshold"": 1000, ""total_resource_threshold"": 1300000, ""target_logic"": ""total""},
            ""upgrade_wall"": false, ""wall_level"": 14, ""wall_gold_threshold"": 5000000, ""wall_elixir_threshold"": 5000000,
            ""wall_gold_reserve"": 100000, ""wall_elixir_reserve"": 0, ""enable_stats"": true,
            ""night_village"": {""farm_mode"": ""auto"", ""min_cups"": 0, ""max_cups"": 5000, ""enable_attack"": true, ""boost_clock_tower"": false, ""upgrade_wall"": false, ""army_management"": true, ""fill_army"": true, ""army_formation"": ""auto"", ""wait_for_heroes"": true, ""hero_wait_seconds"": 90, ""custom_drop_order_enabled"": false, ""drop_order"": ""BattleMachine|BattleCopter|BoxerGiant|DropShip|HogGlider|Bomber|SuperPekka|PowerPekka|BabyDragon|CannonCart|ElectrofireWizard|NightWitch|RagedBarbarian|BetaMinion|SneakyArcher"", ""next_troop_delay_ms"": 600, ""same_troop_delay_ms"": 180, ""handle_bomber"": true},
            ""run_session"": {""play_mode"": ""main_village"", ""stop_after_battles_enabled"": false, ""stop_after_battles"": 0, ""stop_after_minutes_enabled"": false, ""stop_after_minutes"": 0},
            ""multi_account"": {""enable_multi_account"": false, ""multi_interval_mins"": 60, ""switch_after_battles_enabled"": false, ""switch_after_battles"": 0, ""switch_after_minutes_enabled"": true, ""switch_after_clan_points_enabled"": false, ""switch_after_clan_points"": 0, ""selected_villages"": [1], ""accounts"": [{""id"": ""acc_1"", ""name"": ""Account 1"", ""profileVillage"": 1, ""targetVillage"": ""main_village"", ""templatePath"": """", ""enabled"": true}]}
        }";
        using var defaultDoc = JsonDocument.Parse(defaultJson);
        Config = defaultDoc.RootElement.Clone();
    }

    public static MainVillageConfig GetMainVillageConfig(JsonElement cfg, int villageIdx)
    {
        JsonElement profile = LoadVillageProfile(villageIdx);
        JsonElement farming = ConfigManager.GetObjectOrDefault(cfg, "farming_thresholds");
        JsonElement target = ConfigManager.GetObjectOrDefault(cfg, "target_data_threshold");

        var targetConfig = new FarmingTargetConfig(
            GoldThreshold: ConfigManager.GetThresholdOrDefault(profile, farming, target, "gold_threshold", "gold", 0),
            ElixirThreshold: ConfigManager.GetThresholdOrDefault(profile, farming, target, "elixir_threshold", "elixir", 0),
            DarkElixirThreshold: ConfigManager.GetThresholdOrDefault(profile, farming, target, "dark_elixir_threshold", "dark_elixir", 0),
            TotalResourceThreshold: ConfigManager.GetThresholdOrDefault(profile, farming, target, "total_resource_threshold", "total", 0),
            Logic: ParseTargetSelectionLogic(ConfigManager.GetStringOrDefault(profile, "target_logic", ConfigManager.GetStringOrDefault(farming, "target_logic", "total"))));

        int defaultTotal = targetConfig.GoldThreshold + targetConfig.ElixirThreshold;
        if (targetConfig.TotalResourceThreshold <= 0)
            targetConfig = targetConfig with { TotalResourceThreshold = defaultTotal };

        string attackModeText = ConfigManager.GetStringOrDefault(profile, "attack_mode", ConfigManager.GetStringOrDefault(cfg, "attack_mode", "attack"));
        AttackMode attackMode = string.Equals(attackModeText, "donate_only", StringComparison.OrdinalIgnoreCase)
            ? AttackMode.DonateOnly : AttackMode.Attack;

        var surrender = new SmartSurrenderConfig(
            Enabled: ConfigManager.GetBoolOrDefault(profile, "smart_surrender_enabled", ConfigManager.GetBoolOrDefault(cfg, "smart_surrender_enabled", false)),
            AfterSecondsEnabled: ConfigManager.GetBoolOrDefault(profile, "surrender_after_seconds_enabled", ConfigManager.GetBoolOrDefault(cfg, "surrender_after_seconds_enabled", false)),
            AfterSeconds: ConfigManager.GetIntOrDefault(profile, "surrender_after_seconds", ConfigManager.GetIntOrDefault(cfg, "surrender_after_seconds", 0)),
            LowResourcesEnabled: ConfigManager.GetBoolOrDefault(profile, "surrender_low_resources_enabled", ConfigManager.GetBoolOrDefault(cfg, "surrender_low_resources_enabled", false)),
            LowResourcesThreshold: ConfigManager.GetIntOrDefault(profile, "surrender_low_resources_threshold", ConfigManager.GetIntOrDefault(cfg, "surrender_low_resources_threshold", 0)));

        return new MainVillageConfig(
            AttackMode: attackMode, Target: targetConfig,
            RequestTroops: ConfigManager.GetBoolOrDefault(profile, "request_troops", ConfigManager.GetBoolOrDefault(cfg, "request_troops", false)),
            UseEventTroops: ConfigManager.GetBoolOrDefault(profile, "use_event_troops", ConfigManager.GetBoolOrDefault(cfg, "use_event_troops", false)),
            UseCake: ConfigManager.GetBoolOrDefault(profile, "use_cake", ConfigManager.GetBoolOrDefault(cfg, "use_cake", false)),
            SmartSurrender: surrender);
    }

    public static TrainingConfig GetTrainingConfig(JsonElement cfg, int villageIdx)
    {
        JsonElement profile = LoadVillageProfile(villageIdx);
        return new TrainingConfig(
            Mode: ConfigManager.GetStringOrDefault(profile, "train_mode", ConfigManager.GetStringOrDefault(cfg, "train_mode", "smart")),
            QuickSlot: ConfigManager.GetIntOrDefault(profile, "quick_slot", ConfigManager.GetIntOrDefault(cfg, "quick_slot", 1)),
            AttackStrategy: GetAttackStrategy(cfg, villageIdx));
    }

    public static string GetAttackStrategy(JsonElement cfg, int villageIdx)
    {
        JsonElement profile = LoadVillageProfile(villageIdx);
        return ConfigManager.GetStringOrDefault(profile, "attack", ConfigManager.GetStringOrDefault(cfg, "attack", "Dragon_Attack"));
    }

    public static WallUpgradeConfig GetWallUpgradeConfig(JsonElement cfg, int villageIdx)
    {
        JsonElement profile = LoadVillageProfile(villageIdx);
        bool hasProfileKey = profile.ValueKind == JsonValueKind.Object && profile.TryGetProperty("upgrade_wall", out _);
        bool hasRootKey = cfg.ValueKind == JsonValueKind.Object && cfg.TryGetProperty("upgrade_wall", out _);

        if (hasProfileKey || hasRootKey)
        {
            JsonElement primary = hasProfileKey ? profile : cfg;
            bool enabled = ConfigManager.GetBoolOrDefault(primary, "upgrade_wall", false);
            int goldThreshold = GetWallThreshold(primary, cfg, "wall_gold_threshold");
            int elixirThreshold = GetWallThreshold(primary, cfg, "wall_elixir_threshold");
            int goldReserve = GetWallReserve(primary, cfg, "wall_gold_reserve", 100_000);
            int elixirReserve = GetWallReserve(primary, cfg, "wall_elixir_reserve", 0);
            int batchLimit = ConfigManager.GetIntOrDefault(primary, "wall_batch_limit", ConfigManager.GetIntOrDefault(cfg, "wall_batch_limit", 1));
            bool debugScreenshots = ConfigManager.GetBoolOrDefault(primary, "wall_debug_screenshots", ConfigManager.GetBoolOrDefault(cfg, "wall_debug_screenshots", false));

            return CreateWallUpgradeConfig(enabled, goldThreshold, elixirThreshold, goldReserve, elixirReserve, batchLimit, debugScreenshots);
        }

        JsonElement wall = ConfigManager.GetObjectOrDefault(cfg, "element_state_automation");
        if (wall.ValueKind == JsonValueKind.Object && ConfigManager.GetBoolOrDefault(wall, "upgrade_enabled", false))
        {
            return CreateWallUpgradeConfig(
                true,
                ConfigManager.GetIntOrDefault(wall, "wall_gold_threshold", ConfigManager.GetIntOrDefault(wall, "min_retained_gold", 5_000_000)),
                ConfigManager.GetIntOrDefault(wall, "wall_elixir_threshold", ConfigManager.GetIntOrDefault(wall, "min_retained_elixir", 5_000_000)),
                ConfigManager.GetIntOrDefault(wall, "wall_gold_reserve", 100_000),
                ConfigManager.GetIntOrDefault(wall, "wall_elixir_reserve", 0),
                ConfigManager.GetIntOrDefault(wall, "wall_batch_limit", 1),
                ConfigManager.GetBoolOrDefault(wall, "wall_debug_screenshots", false));
        }

        return CreateWallUpgradeConfig(false, 5_000_000, 5_000_000, 100_000, 0, 1, false);
    }

    public static int ReadClanGamesPoints(int villageIdx)
    {
        string path = StatsFilePath(villageIdx);
        JsonObject stats = LoadStatsFromDisk(path);
        return GetJsonInt(stats, "clan_games_points");
    }

    public static JsonElement LoadVillageProfile(int villageIdx)
    {
        string fileName = $"Village_{villageIdx}.json";
        string userData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        string[] candidates =
        {
            Path.Combine(userData, "SimpliMixi", "profiles", fileName),
            Path.Combine(Directory.GetCurrentDirectory(), "profiles", fileName),
            Path.Combine(AppContext.BaseDirectory, "profiles", fileName)
        };

        foreach (string path in candidates.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (!File.Exists(path)) continue;
            try
            {
                using JsonDocument doc = JsonDocument.Parse(File.ReadAllText(path));
                return doc.RootElement.Clone();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[CONFIG-CS WARNING] phase=init status=fail action=load_profile path=\"{path}\" reason=\"{ex.Message}\"");
                return default;
            }
        }
        return default;
    }

    private static int GetWallThreshold(JsonElement primary, JsonElement root, string key)
    {
        if (ConfigManager.TryReadInt(primary, key, out int value) || ConfigManager.TryReadInt(root, key, out value))
            return value;
        if (ConfigManager.TryReadInt(primary, "wall_upgrade_threshold", out value) || ConfigManager.TryReadInt(root, "wall_upgrade_threshold", out value))
        {
            LogLegacyWallConfigMigrated();
            return value;
        }
        return 5_000_000;
    }

    private static int GetWallReserve(JsonElement primary, JsonElement root, string key, int fallback)
    {
        if (ConfigManager.TryReadInt(primary, key, out int value) || ConfigManager.TryReadInt(root, key, out value))
            return value;
        if (ConfigManager.TryReadInt(primary, "wall_reserve_threshold", out value) || ConfigManager.TryReadInt(root, "wall_reserve_threshold", out value))
        {
            LogLegacyWallConfigMigrated();
            return value;
        }
        return fallback;
    }

    private static WallUpgradeConfig CreateWallUpgradeConfig(bool enabled, int goldThreshold, int elixirThreshold, int goldReserve, int elixirReserve, int batchLimit, bool debugScreenshots)
    {
        int safeBatchLimit = Math.Clamp(batchLimit, 1, 10);
        return new WallUpgradeConfig(enabled, goldThreshold, elixirThreshold, goldReserve, elixirReserve, safeBatchLimit, debugScreenshots);
    }

    private static TargetSelectionLogic ParseTargetSelectionLogic(string logic)
    {
        return logic.Trim().ToLowerInvariant() switch
        {
            "and" => TargetSelectionLogic.And,
            "or" => TargetSelectionLogic.Or,
            _ => TargetSelectionLogic.Total
        };
    }

    private static void LogLegacyWallConfigMigrated()
    {
        if (s_loggedLegacyWallConfigMigration) return;
        Console.WriteLine("[CONFIG] event=legacy_config_migrated scope=wall");
        s_loggedLegacyWallConfigMigration = true;
    }

    // --- stats helpers for config (minimal, reused by config methods) ---
    private static string StatsFilePath(int villageIdx)
    {
        string appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        return Path.Combine(appData, "SimpliMixi", "profiles", $"Stats_{villageIdx}.json");
    }

    private static JsonObject LoadStatsFromDisk(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                JsonNode? node = JsonNode.Parse(File.ReadAllText(path));
                if (node is JsonObject obj) return obj;
            }
        }
        catch { }
        return new JsonObject { ["clan_games_points"] = 0 };
    }

    private static int GetJsonInt(JsonObject obj, string key)
    {
        if (!obj.TryGetPropertyValue(key, out JsonNode? node) || node == null) return 0;
        return node.GetValueKind() == JsonValueKind.Number && node.GetValue<int>() is int value ? value : 0;
    }
}
