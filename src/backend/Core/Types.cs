using System.Text.Json.Nodes;

namespace CvAut;

internal enum AttackMode
{
    Attack,
    DonateOnly
}

internal enum TargetSelectionLogic
{
    Or,
    And,
    Total
}

internal sealed record FarmingTargetConfig(
    int GoldThreshold,
    int ElixirThreshold,
    int DarkElixirThreshold,
    int TotalResourceThreshold,
    TargetSelectionLogic Logic);

internal sealed record SmartSurrenderConfig(
    bool Enabled,
    bool AfterSecondsEnabled,
    int AfterSeconds,
    bool LowResourcesEnabled,
    int LowResourcesThreshold);

internal sealed record MainVillageConfig(
    AttackMode AttackMode,
    FarmingTargetConfig Target,
    bool RequestTroops,
    bool UseEventTroops,
    bool UseCake,
    SmartSurrenderConfig SmartSurrender);

internal sealed record WallUpgradeConfig(
    bool Enabled,
    int WallLevel,
    int GoldThreshold,
    int ElixirThreshold,
    int GoldReserve,
    int ElixirReserve,
    bool DebugScreenshots);

internal sealed record TrainingConfig(
    string Mode,
    int QuickSlot,
    string AttackStrategy);

internal sealed record ScoutedResources(int Gold, int Elixir, int DarkElixir);
