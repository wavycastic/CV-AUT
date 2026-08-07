using System;
using System.Text.Json;
using CvAut.Configuration;
using TypedTrainingConfig = CvAut.Configuration.TrainingConfig;
using TypedWallConfig = CvAut.Configuration.WallUpgradeConfig;

namespace CvAut;

internal sealed class ConfigService : IConfigService
{
    private readonly JsonConfigPersistence _persistence;

    public ConfigService(string configPath)
    {
        _persistence = new JsonConfigPersistence(configPath);
        Current = AutomationConfigSnapshotReader.Read(_persistence.Root);
    }

    public AutomationConfigSnapshot Current { get; private set; }

    MainVillageConfig IConfigService.GetMainVillageConfig(int villageIndex)
        => GetMainVillageConfig(_persistence.Root, villageIndex);

    CvAut.TrainingConfig IConfigService.GetTrainingConfig(int villageIndex)
        => GetTrainingConfig(_persistence.Root, villageIndex);

    string IConfigService.GetAttackStrategy(int villageIndex)
        => GetAttackStrategy(_persistence.Root, villageIndex);

    CvAut.WallUpgradeConfig IConfigService.GetWallUpgradeConfig(int villageIndex)
        => GetWallUpgradeConfig(_persistence.Root, villageIndex);

    public void Reload()
    {
        _persistence.Reload();
        Current = AutomationConfigSnapshotReader.Read(_persistence.Root);
    }

    public static MainVillageConfig GetMainVillageConfig(JsonElement root, int villageIndex)
    {
        AutomationConfigSnapshot snapshot = AutomationConfigSnapshotReader.Read(root);
        JsonConfigReader rootReader = new(root);
        JsonConfigReader profile = new(JsonConfigPersistence.LoadVillageProfile(villageIndex));
        FarmingConfig farming = snapshot.Farming;

        JsonConfigReader profileFarming = profile.Section("farming_thresholds");
        bool profileHasFarmingSection = profile.HasProperty("farming_thresholds");
        bool rootHasFarmingSection = rootReader.HasProperty("farming_thresholds");

        int gold = profileHasFarmingSection
            ? profileFarming.Int("gold_threshold", farming.GoldThreshold, 0)
            : (rootHasFarmingSection
                ? farming.GoldThreshold
                : profile.Int("gold_threshold", farming.GoldThreshold, 0));

        int elixir = profileHasFarmingSection
            ? profileFarming.Int("elixir_threshold", farming.ElixirThreshold, 0)
            : (rootHasFarmingSection
                ? farming.ElixirThreshold
                : profile.Int("elixir_threshold", farming.ElixirThreshold, 0));

        int darkElixir = profileHasFarmingSection
            ? profileFarming.Int("dark_elixir_threshold", farming.DarkElixirThreshold, 0)
            : (rootHasFarmingSection
                ? farming.DarkElixirThreshold
                : profile.Int("dark_elixir_threshold", farming.DarkElixirThreshold, 0));

        int total = profileHasFarmingSection
            ? profileFarming.Int("total_resource_threshold", farming.TotalResourceThreshold, 0)
            : (rootHasFarmingSection
                ? farming.TotalResourceThreshold
                : profile.Int("total_resource_threshold", farming.TotalResourceThreshold, 0));

        TargetSelectionLogic logic = profileHasFarmingSection
            ? (profileFarming.String("target_logic", farming.TargetLogic.ToString()).ToLowerInvariant() switch
            {
                "and" => TargetSelectionLogic.And,
                "or" => TargetSelectionLogic.Or,
                _ => TargetSelectionLogic.Total
            })
            : (rootHasFarmingSection
                ? (farming.TargetLogic switch
                {
                    TargetLogic.And => TargetSelectionLogic.And,
                    TargetLogic.Or => TargetSelectionLogic.Or,
                    _ => TargetSelectionLogic.Total
                })
                : (profile.String("target_logic", farming.TargetLogic.ToString()).ToLowerInvariant() switch
                {
                    "and" => TargetSelectionLogic.And,
                    "or" => TargetSelectionLogic.Or,
                    _ => TargetSelectionLogic.Total
                }));

        AttackConfig attack = snapshot.Attack;
        SmartSurrenderSettings surrender = attack.SmartSurrender;
        return new MainVillageConfig(
            profile.String("attack_mode", attack.Mode).Equals("donate_only", StringComparison.OrdinalIgnoreCase)
                ? AttackMode.DonateOnly
                : AttackMode.Attack,
            new FarmingTargetConfig(gold, elixir, darkElixir, total <= 0 ? gold + elixir : total, logic),
            profile.Bool("request_troops", attack.RequestTroops),
            profile.Bool("use_event_troops", attack.UseEventTroops),
            profile.Bool("use_cake", attack.UseCake),
            new SmartSurrenderConfig(
                profile.Bool("smart_surrender_enabled", surrender.Enabled),
                profile.Bool("surrender_after_seconds_enabled", surrender.AfterSecondsEnabled),
                profile.Int("surrender_after_seconds", surrender.AfterSeconds, 0),
                profile.Bool("surrender_low_resources_enabled", surrender.LowResourcesEnabled),
                profile.Int("surrender_low_resources_threshold", surrender.LowResourcesThreshold, 0)));
    }

    public static CvAut.TrainingConfig GetTrainingConfig(JsonElement root, int villageIndex)
    {
        AutomationConfigSnapshot snapshot = AutomationConfigSnapshotReader.Read(root);
        TypedTrainingConfig training = snapshot.Training;
        JsonConfigReader profile = new(JsonConfigPersistence.LoadVillageProfile(villageIndex));
        return new CvAut.TrainingConfig(
            profile.String("train_mode", training.Mode),
            profile.Int("quick_slot", training.QuickSlot, 1, 2),
            profile.String("attack", snapshot.Attack.Strategy));
    }

    public static string GetAttackStrategy(JsonElement root, int villageIndex)
    {
        AutomationConfigSnapshot snapshot = AutomationConfigSnapshotReader.Read(root);
        JsonConfigReader profile = new(JsonConfigPersistence.LoadVillageProfile(villageIndex));
        return profile.String("attack", snapshot.Attack.Strategy);
    }

    public static CvAut.WallUpgradeConfig GetWallUpgradeConfig(JsonElement root, int villageIndex)
    {
        TypedWallConfig wall = AutomationConfigSnapshotReader.Read(root).WallUpgrade;
        JsonConfigReader profile = new(JsonConfigPersistence.LoadVillageProfile(villageIndex));
        return new CvAut.WallUpgradeConfig(
            profile.Bool("upgrade_wall", wall.Enabled),
            profile.Int("wall_gold_threshold", wall.GoldThreshold, 0),
            profile.Int("wall_elixir_threshold", wall.ElixirThreshold, 0),
            profile.Int("wall_gold_reserve", wall.GoldReserve, 0),
            profile.Int("wall_elixir_reserve", wall.ElixirReserve, 0),
            profile.Int("wall_batch_limit", wall.BatchLimit, 1, 10),
            profile.Bool("wall_debug_screenshots", wall.DebugScreenshots));
    }

    public static int ReadClanGamesPoints(int villageIndex)
        => JsonConfigPersistence.ReadClanGamesPoints(villageIndex);

    public static JsonElement LoadVillageProfile(int villageIndex)
        => JsonConfigPersistence.LoadVillageProfile(villageIndex);
}
