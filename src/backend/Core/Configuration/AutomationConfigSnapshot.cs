using System;
using System.Collections.Generic;
using System.Text.Json;

namespace CvAut.Configuration;

public enum VillagePlayMode
{
    MainVillage,
    NightVillage,
    ClanGames,
    ClanCapital
}

public sealed record RunSessionConfig(
    VillagePlayMode PlayMode,
    bool StopAfterBattlesEnabled,
    int StopAfterBattles,
    bool StopAfterMinutesEnabled,
    int StopAfterMinutes);

public sealed record AutomationConfigSnapshot(
    DeviceConnectionConfig DeviceConnection,
    RunSessionConfig RunSession,
    FarmingConfig Farming,
    AttackConfig Attack,
    TrainingConfig Training,
    WallUpgradeConfig WallUpgrade,
    MultiAccountConfig MultiAccount,
    NightVillageConfig NightVillage,
    AdvancedConfig Advanced,
    NotificationConfig Notifications,
    bool EnableStats);

internal static class AutomationConfigSnapshotReader
{
    public static AutomationConfigSnapshot Read(JsonElement root)
    {
        var config = new JsonConfigReader(root);
        return new AutomationConfigSnapshot(
            DeviceConnectionConfigReader.Read(root),
            ReadRunSession(config),
            ReadFarming(config),
            ReadAttack(config),
            ReadTraining(config),
            ReadWall(config),
            ReadMultiAccount(config),
            ReadNightVillage(config),
            ReadAdvanced(config),
            ReadNotifications(config),
            config.Bool("enable_stats", true));
    }

    private static RunSessionConfig ReadRunSession(JsonConfigReader root)
    {
        JsonConfigReader session = root.Section("run_session");
        string mode = session.String("play_mode", root.String("play_mode", "main_village"));
        return new RunSessionConfig(
            ParsePlayMode(mode),
            session.Bool("stop_after_battles_enabled", false),
            session.Int("stop_after_battles", 0, 0),
            session.Bool("stop_after_minutes_enabled", false),
            session.Int("stop_after_minutes", 0, 0));
    }

    private static FarmingConfig ReadFarming(JsonConfigReader root)
    {
        JsonConfigReader farming = root.Section("farming_thresholds");
        int gold = farming.Int("gold_threshold", 650000, 0);
        int elixir = farming.Int("elixir_threshold", 650000, 0);
        int total = farming.Int("total_resource_threshold", gold + elixir, 0);
        return new FarmingConfig(
            gold,
            elixir,
            farming.Int("dark_elixir_threshold", 1000, 0),
            total <= 0 ? gold + elixir : total,
            farming.String("target_logic", "total").ToLowerInvariant() switch
            {
                "and" => TargetLogic.And,
                "or" => TargetLogic.Or,
                _ => TargetLogic.Total
            });
    }

    private static AttackConfig ReadAttack(JsonConfigReader root)
    {
        JsonConfigReader surrender = root.Section("smart_surrender");
        return new AttackConfig(
            root.String("attack", "Dragon_Attack"),
            root.String("attack_mode", "attack"),
            root.Bool("request_troops", false),
            root.Bool("use_event_troops", false),
            root.Bool("use_cake", false),
            new SmartSurrenderSettings(
                surrender.Bool("enabled", root.Bool("smart_surrender_enabled", false)),
                surrender.Bool("after_seconds_enabled", root.Bool("surrender_after_seconds_enabled", false)),
                surrender.Int("after_seconds", root.Int("surrender_after_seconds", 0), 0),
                surrender.Bool("low_resources_enabled", root.Bool("surrender_low_resources_enabled", false)),
                surrender.Int("low_resources_threshold", root.Int("surrender_low_resources_threshold", 0), 0)));
    }

    private static TrainingConfig ReadTraining(JsonConfigReader root)
        => new(
            root.String("train_mode", "smart"),
            root.Int("quick_slot", 1, 1, 2),
            root.Bool("wait_for_heroes", true),
            root.Int("hero_wait_seconds", 90, 0));

    private static WallUpgradeConfig ReadWall(JsonConfigReader root)
    {
        JsonConfigReader legacy = root.Section("element_state_automation");
        return new WallUpgradeConfig(
            root.Bool("upgrade_wall", legacy.Bool("upgrade_enabled", false)),
            root.Int("wall_level", 14, 1),
            root.Int("wall_gold_threshold", legacy.Int("wall_gold_threshold", root.Int("wall_upgrade_threshold", 5000000), 0), 0),
            root.Int("wall_elixir_threshold", legacy.Int("wall_elixir_threshold", root.Int("wall_upgrade_threshold", 5000000), 0), 0),
            root.Int("wall_gold_reserve", legacy.Int("wall_gold_reserve", root.Int("wall_reserve_threshold", 100000), 0), 0),
            root.Int("wall_elixir_reserve", legacy.Int("wall_elixir_reserve", 0, 0), 0),
            root.Int("wall_batch_limit", root.Int("wall_batch_limit", legacy.Int("wall_batch_limit", 1, 1, 10), 1, 10), 1, 10),
            root.Bool("wall_debug_screenshots", false));
    }

    private static MultiAccountConfig ReadMultiAccount(JsonConfigReader root)
    {
        JsonConfigReader multi = root.Section("multi_account");
        var accounts = new List<AccountConfig>();
        foreach (JsonConfigReader account in multi.ObjectArray("accounts"))
        {
            accounts.Add(new AccountConfig(
                account.String("id", string.Empty),
                account.String("name", "Account"),
                account.Int("profileVillage", 1, 1),
                ParsePlayMode(account.String("targetVillage", "main_village")),
                account.String("templatePath", string.Empty),
                account.Bool("enabled", true)));
        }
        return new MultiAccountConfig(
            multi.Bool("enable_multi_account", false),
            multi.Int("multi_interval_mins", 60, 1),
            multi.Bool("switch_after_battles_enabled", false),
            multi.Int("switch_after_battles", 0, 0),
            multi.Bool("switch_after_minutes_enabled", true),
            multi.Int("switch_after_minutes", 0, 0),
            multi.Bool("switch_after_clan_points_enabled", false),
            multi.Int("switch_after_clan_points", 0, 0),
            multi.IntArray("selected_villages", 1),
            accounts);
    }

    private static NightVillageConfig ReadNightVillage(JsonConfigReader root)
    {
        JsonConfigReader night = root.Section("night_village");
        return new NightVillageConfig(
            night.String("farm_mode", "auto"),
            night.Int("min_cups", 0, 0, 10000),
            night.Int("max_cups", 5000, 0, 10000),
            night.Bool("trophy_range_enabled", true),
            night.Bool("halt_on_gold_full", false),
            night.Bool("halt_on_elixir_full", false),
            night.Bool("force_attack_for_clan_games", false),
            night.Bool("enable_attack", true),
            night.Bool("boost_clock_tower", false),
            night.Bool("upgrade_wall", false),
            night.Bool("army_management", true),
            night.Bool("fill_army", true),
            night.String("army_formation", "auto"),
            night.Bool("wait_for_heroes", true),
            night.Int("hero_wait_seconds", 90, 0),
            night.Bool("custom_drop_order_enabled", false),
            night.String("drop_order", string.Empty),
            night.Int("next_troop_delay_ms", 600, 0),
            night.Int("same_troop_delay_ms", 180, 0),
            night.Bool("handle_bomber", true),
            night.Int("max_attacks_per_cycle", 20, 1, 100));
    }

    private static AdvancedConfig ReadAdvanced(JsonConfigReader root)
    {
        JsonConfigReader advanced = root.Section("advanced");
        JsonConfigReader legacy = root.Section("advanced_config");
        JsonConfigReader delays = legacy.Section("attack_delays");
        return new AdvancedConfig(
            legacy.Bool("use_default_config", true),
            advanced.Int("search_delay_ms", 800, 0),
            advanced.Int("deploy_delay_ms", 120, 0),
            advanced.Int("return_home_delay_ms", 1500, 0),
            delays.Int("troop_deploy_delay_ms", 35, 20, 500),
            delays.Int("rage_spell_delay_ms", 350, 100, 5000),
            delays.Int("freeze_spell_delay_ms", 450, 100, 5000),
            delays.Int("grand_warden_ability_delay_ms", 2500, 500, 15000));
    }

    private static NotificationConfig ReadNotifications(JsonConfigReader root)
    {
        JsonConfigReader notifications = root.Section("notifications");
        return new NotificationConfig(
            notifications.Bool("enabled", false),
            notifications.String("webhook_url", string.Empty),
            notifications.Bool("notify_on_error", true),
            notifications.Bool("notify_on_stopped", false),
            notifications.Bool("notify_on_started", false));
    }

    internal static VillagePlayMode ParsePlayMode(string value)
        => value.Trim().ToLowerInvariant() switch
        {
            "night_village" => VillagePlayMode.NightVillage,
            "clan_games" => VillagePlayMode.ClanGames,
            "clan_capital" => VillagePlayMode.ClanCapital,
            _ => VillagePlayMode.MainVillage
        };
}
