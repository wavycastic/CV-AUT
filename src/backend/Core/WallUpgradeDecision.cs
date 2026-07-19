namespace CvAut
{
    internal enum WallUpgradeResource
    {
        None,
        Gold,
        Elixir
    }

    internal sealed record WallUpgradeDecisionInput(
        int WallLevel,
        int? WallCost,
        int Gold,
        int Elixir,
        int GoldStartThreshold,
        int ElixirStartThreshold,
        int GoldReserve,
        int ElixirReserve,
        int BatchLimit = 10);

    internal sealed record WallUpgradeDecision(
        WallUpgradeResource Resource,
        int RequestedCount,
        string SkipReason,
        int AffordableGold,
        int AffordableElixir);

    internal static class WallUpgradeDecider
    {
        internal const int MinSupportedWallLevel = 1;
        internal const int MaxSupportedWallLevel = 18;

        internal static WallUpgradeDecision Decide(WallUpgradeDecisionInput input)
        {
            if (input.WallLevel < MinSupportedWallLevel || input.WallLevel > MaxSupportedWallLevel)
            {
                return Skip("unsupported_wall_level", 0, 0);
            }

            if (input.WallCost is not { } cost || cost <= 0)
            {
                return Skip("missing_wall_cost", 0, 0);
            }

            int affordableGold = input.Gold >= input.GoldStartThreshold
                ? Math.Max(0, input.Gold - Math.Max(0, input.GoldReserve)) / cost
                : 0;
            int affordableElixir = input.WallLevel >= 4 && input.Elixir >= input.ElixirStartThreshold
                ? Math.Max(0, input.Elixir - Math.Max(0, input.ElixirReserve)) / cost
                : 0;

            if (affordableGold == 0 && affordableElixir == 0)
            {
                return Skip("cannot_afford_or_below_threshold", affordableGold, affordableElixir);
            }

            int batchLimit = Math.Max(0, input.BatchLimit);
            return affordableGold >= affordableElixir
                ? new WallUpgradeDecision(WallUpgradeResource.Gold, Math.Min(affordableGold, batchLimit), string.Empty, affordableGold, affordableElixir)
                : new WallUpgradeDecision(WallUpgradeResource.Elixir, Math.Min(affordableElixir, batchLimit), string.Empty, affordableGold, affordableElixir);
        }

        private static WallUpgradeDecision Skip(string reason, int affordableGold, int affordableElixir)
        {
            return new WallUpgradeDecision(WallUpgradeResource.None, 0, reason, affordableGold, affordableElixir);
        }
    }
}
