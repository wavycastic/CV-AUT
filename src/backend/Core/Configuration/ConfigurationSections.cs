using System.Collections.Generic;

namespace CvAut.Configuration;

public enum TargetLogic
{
    Total,
    And,
    Or
}

public sealed record FarmingConfig(
    int GoldThreshold,
    int ElixirThreshold,
    int DarkElixirThreshold,
    int TotalResourceThreshold,
    TargetLogic TargetLogic);

public sealed record SmartSurrenderSettings(
    bool Enabled,
    bool AfterSecondsEnabled,
    int AfterSeconds,
    bool LowResourcesEnabled,
    int LowResourcesThreshold);

public sealed record AttackConfig(
    string Strategy,
    string Mode,
    bool RequestTroops,
    bool UseEventTroops,
    bool UseCake,
    SmartSurrenderSettings SmartSurrender);

public sealed record TrainingConfig(
    string Mode,
    int QuickSlot,
    bool WaitForHeroes,
    int HeroWaitSeconds);

public sealed record WallUpgradeConfig(
    bool Enabled,
    int Level,
    int GoldThreshold,
    int ElixirThreshold,
    int GoldReserve,
    int ElixirReserve,
    int BatchLimit,
    bool DebugScreenshots);

public sealed record AccountConfig(
    string Id,
    string Name,
    int ProfileVillage,
    VillagePlayMode TargetVillage,
    string TemplatePath,
    bool Enabled);

public sealed record MultiAccountConfig(
    bool Enabled,
    int IntervalMinutes,
    bool SwitchAfterBattlesEnabled,
    int SwitchAfterBattles,
    bool SwitchAfterMinutesEnabled,
    int SwitchAfterMinutes,
    bool SwitchAfterClanPointsEnabled,
    int SwitchAfterClanPoints,
    IReadOnlyList<int> SelectedVillages,
    IReadOnlyList<AccountConfig> Accounts);

public sealed record NightVillageConfig(
    string FarmMode,
    int MinCups,
    int MaxCups,
    bool EnableAttack,
    bool BoostClockTower,
    bool UpgradeWall,
    bool ArmyManagement,
    bool FillArmy,
    string ArmyFormation,
    bool WaitForHeroes,
    int HeroWaitSeconds,
    bool CustomDropOrderEnabled,
    string DropOrder,
    int NextTroopDelayMs,
    int SameTroopDelayMs,
    bool HandleBomber,
    int MaxAttacksPerCycle);

public sealed record AdvancedConfig(
    bool UseDefaults,
    int SearchDelayMs,
    int DeployDelayMs,
    int ReturnHomeDelayMs,
    int TroopDeployDelayMs,
    int RageSpellDelayMs,
    int FreezeSpellDelayMs,
    int GrandWardenAbilityDelayMs);

public sealed record NotificationConfig(
    bool Enabled,
    string WebhookUrl,
    bool NotifyOnError,
    bool NotifyOnStopped,
    bool NotifyOnStarted)
{
    public bool IsActionable
        => Enabled && WebhookUrl.StartsWith("https://", System.StringComparison.OrdinalIgnoreCase);
}
