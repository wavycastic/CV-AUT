using System;
using CvAut;
using Xunit;

namespace CvAut.Backend.Tests
{
    public class WallUpgradeDeciderTests
    {
        [Fact]
        public void Decide_MissingCost_Skips()
        {
            WallUpgradeDecision decision = Decide(null, gold: 10_000_000, elixir: 10_000_000);

            Assert.Equal(WallUpgradeResource.None, decision.Resource);
            Assert.Equal("missing_wall_cost", decision.SkipReason);
        }

        [Fact]
        public void Decide_BelowStartThreshold_SkipsEvenWithEnoughCost()
        {
            WallUpgradeDecision decision = Decide(1_000_000, gold: 9_000_000, elixir: 9_000_000, goldStart: 10_000_000, elixirStart: 10_000_000);

            Assert.Equal(WallUpgradeResource.None, decision.Resource);
            Assert.Equal("cannot_afford_or_below_threshold", decision.SkipReason);
        }

        [Fact]
        public void Decide_ReserveIsNotSpendable()
        {
            WallUpgradeDecision decision = Decide(1_000_000, gold: 1_999_999, elixir: 0, goldStart: 0, elixirStart: 0, goldReserve: 1_000_000);

            Assert.Equal(WallUpgradeResource.None, decision.Resource);
            Assert.Equal(0, decision.AffordableGold);
        }

        [Fact]
        public void Decide_BatchLimitIsHardCap()
        {
            WallUpgradeDecision decision = Decide(1_000_000, gold: 20_000_000, elixir: 0, goldStart: 0, elixirStart: 0, batchLimit: 3);

            Assert.Equal(WallUpgradeResource.Gold, decision.Resource);
            Assert.Equal(3, decision.RequestedCount);
        }

        [Theory]
        [InlineData(10_000_000, 6_000_000, "Gold")]
        [InlineData(6_000_000, 10_000_000, "Elixir")]
        [InlineData(10_000_000, 10_000_000, "Gold")]
        public void Decide_PicksHigherCount_AndGoldOnTie(int gold, int elixir, string expected)
        {
            WallUpgradeDecision decision = Decide(1_000_000, gold, elixir, goldStart: 0, elixirStart: 0);

            Assert.Equal(Enum.Parse<WallUpgradeResource>(expected), decision.Resource);
        }

        [Fact]
        public void Decide_ZeroAffordability_SkipsUpgradeFlow()
        {
            WallUpgradeDecision decision = Decide(1_000_000, gold: 999_999, elixir: 999_999, goldStart: 0, elixirStart: 0);

            Assert.Equal(WallUpgradeResource.None, decision.Resource);
            Assert.Equal(0, decision.RequestedCount);
        }

        [Theory]
        [InlineData(1_000_000, 0)]
        [InlineData(2_000_000, 1)]
        [InlineData(2_999_999, 1)]
        [InlineData(3_000_000, 2)]
        public void Decide_CountAtCostReserveBoundaries(int gold, int expectedCount)
        {
            WallUpgradeDecision decision = Decide(1_000_000, gold, elixir: 0, goldStart: 0, elixirStart: 0, goldReserve: 1_000_000);

            Assert.Equal(expectedCount, decision.AffordableGold);
            Assert.Equal(expectedCount, decision.RequestedCount);
        }

        private static WallUpgradeDecision Decide(
            int? cost,
            int gold,
            int elixir,
            int goldStart = 0,
            int elixirStart = 0,
            int goldReserve = 0,
            int elixirReserve = 0,
            int batchLimit = 10)
        {
            return WallUpgradeDecider.Decide(new WallUpgradeDecisionInput(
                cost,
                gold,
                elixir,
                goldStart,
                elixirStart,
                goldReserve,
                elixirReserve,
                batchLimit));
        }
    }
}
