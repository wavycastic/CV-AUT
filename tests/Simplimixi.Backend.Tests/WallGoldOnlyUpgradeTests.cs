using CvAut;
using OpenCvSharp;
using Xunit;

namespace CvAut.Backend.Tests;

public sealed class WallGoldOnlyUpgradeTests
{
    public static IEnumerable<object[]> GoldOnlyCases()
    {
        yield return new object[] { "wall_gold_only_level1_noring_1000.png", 1_000 };
        yield return new object[] { "wall_gold_only_level1_ring_1000.png", 1_000 };
        yield return new object[] { "wall_gold_only_level1_upgrade_noring_1000.png", 1_000 };
        yield return new object[] { "wall_gold_only_level2_noring_5000.png", 5_000 };
        yield return new object[] { "wall_gold_only_level2_ring_5000.png", 5_000 };
    }

    [Theory]
    [MemberData(nameof(GoldOnlyCases))]
    public void GoldOnlyFixtures_LocalizeReadAndAuthorizeGoldOnly(string fixtureName, int expectedCost)
    {
        using Mat screenshot = LoadFixture(fixtureName);
        using var vision = CreateVision();

        WallPanelLocalizationResult panel = WallDynamicLocalizer.LocalizePanelAndButtons(vision, screenshot);

        Assert.Equal(WallUpgradeResourceMode.GoldOnly, panel.ResourceMode);
        Assert.True(panel.GoldInfo.Found, panel.GoldInfo.SkipReason);
        Assert.True(panel.GoldInfo.CostRoiVerified);
        Assert.False(panel.ElixirInfo.Found);
        Assert.Equal("elixir_upgrade_not_available", panel.ElixirInfo.SkipReason);

        Assert.True(WallUpdater.TryReadWallUpgradeCost(
            vision, screenshot, panel.GoldInfo.CostRoi, out int goldCost, out double confidence));
        Assert.Equal(expectedCost, goldCost);
        Assert.True(confidence >= 0.80, $"confidence={confidence:F2}");

        WallCostValidationResult validation = WallUpdater.ValidateGoldOnlyWallCost(goldCost);
        Assert.True(validation.IsValid, validation.Reason);
        Assert.Equal(expectedCost, validation.Cost);

        WallUpgradeDecision decision = WallUpgradeDecider.Decide(new WallUpgradeDecisionInput(
            WallCost: validation.Cost,
            Gold: 1_000_000,
            Elixir: 20_000_000,
            GoldStartThreshold: 0,
            ElixirStartThreshold: 0,
            GoldReserve: 0,
            ElixirReserve: 0,
            BatchLimit: 1,
            ResourceMode: panel.ResourceMode));

        Assert.Equal(WallUpgradeResource.Gold, decision.Resource);
        Assert.Equal(0, decision.AffordableElixir);
        Assert.Equal(1, decision.RequestedCount);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(999)]
    [InlineData(1_234)]
    [InlineData(20_001_000)]
    public void GoldOnlyPolicy_RejectsInvalidCosts(int cost)
    {
        WallCostValidationResult result = WallUpdater.ValidateGoldOnlyWallCost(cost);
        Assert.False(result.IsValid);
    }

    [Fact]
    public void GoldOnlyDecider_NeverChoosesElixir()
    {
        WallUpgradeDecision decision = WallUpgradeDecider.Decide(new WallUpgradeDecisionInput(
            WallCost: 1_000,
            Gold: 50_000,
            Elixir: 20_000_000,
            GoldStartThreshold: 0,
            ElixirStartThreshold: 0,
            GoldReserve: 0,
            ElixirReserve: 0,
            BatchLimit: 10,
            ResourceMode: WallUpgradeResourceMode.GoldOnly));

        Assert.Equal(WallUpgradeResource.Gold, decision.Resource);
        Assert.Equal(0, decision.AffordableElixir);
    }

    private static VisionEngine CreateVision()
        => new(Path.Combine(AppContext.BaseDirectory, "assets", "Templates"));

    private static Mat LoadFixture(string fileName)
        => FixtureLoader.LoadMandatory(Path.Combine(AppContext.BaseDirectory, "Fixtures", "Wall", fileName));
}
