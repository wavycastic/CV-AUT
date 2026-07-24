using System.Text.Json;
using CvAut;
using OpenCvSharp;
using Xunit;

namespace CvAut.Backend.Tests
{
    public class WallUpdaterVerificationTests
    {
        [Fact]
        public void WallUpgradeDecider_PicksElixirWhenElixirAffordabilityIsHigher()
        {
            // Gold: 10M, threshold: 0, reserve: 9M -> afford 1M / 1M = 1 wall
            // Elixir: 15M, threshold: 0, reserve: 0 -> afford 15M / 1M = 15 walls -> capped by batchLimit 5
            var input = new WallUpgradeDecisionInput(
                WallCost: 1_000_000,
                Gold: 10_000_000,
                Elixir: 15_000_000,
                GoldStartThreshold: 0,
                ElixirStartThreshold: 0,
                GoldReserve: 9_000_000,
                ElixirReserve: 0,
                BatchLimit: 5);

            WallUpgradeDecision decision = WallUpgradeDecider.Decide(input);

            Assert.Equal(WallUpgradeResource.Elixir, decision.Resource);
            Assert.Equal(5, decision.RequestedCount);
        }

        [Fact]
        public void WallUpgradeDecider_RespectsReserves()
        {
            // Gold: 10M, Reserve: 9M, WallCost: 2M -> spendable 1M -> cannot afford 2M wall
            var input = new WallUpgradeDecisionInput(
                WallCost: 2_000_000,
                Gold: 10_000_000,
                Elixir: 0,
                GoldStartThreshold: 5_000_000,
                ElixirStartThreshold: 0,
                GoldReserve: 9_000_000,
                ElixirReserve: 0,
                BatchLimit: 1);

            WallUpgradeDecision decision = WallUpgradeDecider.Decide(input);

            Assert.Equal(WallUpgradeResource.None, decision.Resource);
            Assert.Equal(0, decision.RequestedCount);
        }

        [Theory]
        [InlineData(0, 1)]
        [InlineData(1, 1)]
        [InlineData(5, 5)]
        [InlineData(10, 10)]
        [InlineData(50, 10)]
        public void CreateWallUpgradeConfig_ClampsBatchLimitBetween1And10(int rawBatch, int expectedBatch)
        {
            string json = $$"""
            {
                "upgrade_wall": true,
                "wall_batch_limit": {{rawBatch}}
            }
            """;
            using var doc = JsonDocument.Parse(json);

            WallUpgradeConfig config = ConfigService.GetWallUpgradeConfig(doc.RootElement, 1);

            Assert.Equal(expectedBatch, config.BatchLimit);
        }

        [Fact]
        public void GetWallUpgradeConfig_FallbackToElementStateAutomation_WhenUpgradeWallPropertyIsMissing()
        {
            string legacyJson = """
            {
                "element_state_automation": {
                    "upgrade_enabled": true,
                    "wall_gold_threshold": 6000000,
                    "wall_elixir_threshold": 6000000,
                    "wall_gold_reserve": 200000,
                    "wall_elixir_reserve": 100000,
                    "wall_batch_limit": 3
                }
            }
            """;
            using var doc = JsonDocument.Parse(legacyJson);

            WallUpgradeConfig config = ConfigService.GetWallUpgradeConfig(doc.RootElement, 99);

            Assert.True(config.Enabled);
            Assert.Equal(6_000_000, config.GoldThreshold);
            Assert.Equal(6_000_000, config.ElixirThreshold);
            Assert.Equal(200_000, config.GoldReserve);
            Assert.Equal(100_000, config.ElixirReserve);
            Assert.Equal(3, config.BatchLimit);
        }

        [Fact]
        public void ScanWallLocations_HandlesEmptyScreenshotGracefully()
        {
            var adb = new ADBHelper("127.0.0.1", 5556);
            var vision = new VisionEngine(System.IO.Path.Combine(System.AppContext.BaseDirectory, "assets", "Templates"));
            var updater = new WallUpdater(adb, vision, System.IO.Path.Combine(System.AppContext.BaseDirectory, "assets", "Templates"));

            using Mat screenshot = new Mat();
            var locations = updater.ScanWallLocations(screenshot);

            Assert.Empty(locations);
        }
    }
}
