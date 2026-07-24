using System.Collections.Generic;
using System.Linq;
using CvAut;
using OpenCvSharp;
using Xunit;

namespace CvAut.Backend.Tests
{
    public class BuilderBaseRegressionTests
    {
        [Fact]
        public void OrderSlots_UsesConfiguredCustomOrderAfterHero()
        {
            var slots = new List<BuilderBaseTroopSlot>
            {
                Slot("PowerPekka", 0),
                Slot("BattleMachine", 1),
                Slot("BetaMinion", 2)
            };
            var options = new BuilderBaseBattleOptions("BetaMinion|PowerPekka", true, 600, 180, true);

            string[] ordered = BuilderBaseAttacks.OrderSlots(slots, options).Select(slot => slot.Name).ToArray();

            Assert.Equal(new[] { "BattleMachine", "BetaMinion", "PowerPekka" }, ordered);
        }

        [Theory]
        [InlineData(612, 6, 12)]
        [InlineData(1012, 10, 12)]
        [InlineData(1212, 12, 12)]
        [InlineData(66, 6, 6)]
        [InlineData(36, 3, 6)]
        [InlineData(712, 7, 12)]
        [InlineData(1010, 10, 10)]
        [InlineData(1112, 11, 12)]
        [InlineData(106, 0, 0)]
        [InlineData(126, 0, 0)]
        [InlineData(1, 0, 0)]
        [InlineData(6, 0, 0)]
        [InlineData(12, 0, 0)]
        [InlineData(0, 0, 0)]
        [InlineData(-1, 0, 0)]
        public void ParseStarPair_SupportsTwoDigitMaximum(int raw, int expectedRemaining, int expectedMax)
        {
            (int remaining, int max) = BuilderBaseReport.ParseStarPair(raw);

            Assert.Equal(expectedRemaining, remaining);
            Assert.Equal(expectedMax, max);
        }

        [Theory]
        [InlineData(1, false, false, true)]
        [InlineData(1, false, true, false)]
        [InlineData(1, true, true, true)]
        [InlineData(0, true, false, false)]
        public void EvaluateArmyReadiness_HonorsRequireHeroFlag(int visibleTroops, bool heroReady, bool requireHero, bool expectedReady)
        {
            bool ready = BuilderBaseArmyManager.EvaluateArmyReadiness(visibleTroops, heroReady, requireHero);
            Assert.Equal(expectedReady, ready);
        }

        [Fact]
        public void ScaleMbrPoint_ScalesGoldEndFrom860x732To1600x900()
        {
            Point point = BuilderBaseAttacks.ScaleMbrPoint(632, 406, 1600, 900);

            Assert.Equal(new Point(1176, 499), point);
        }

        [Fact]
        public void ShouldStopBuilderBaseAttacks_DropTrophyIgnoresExhaustedLoot()
        {
            var report = new BuilderBaseReportSnapshot(100, 100, 1500, 1, 2, 9, false, true, false, false, 0, 0, false, false, true);
            bool stop = CVAutomationFramework.ShouldStopBuilderBaseAttacks("drop_trophy", report, false, 1000, 5000, false, false, out string reason);

            Assert.False(stop);
            Assert.Equal("none", reason);
        }

        [Fact]
        public void ShouldStopBuilderBaseAttacks_DropTrophyStopsWhenMinTrophyReached()
        {
            var report = new BuilderBaseReportSnapshot(100, 100, 950, 1, 2, 9, true, true, false, false, 0, 0, false, false, true);
            bool stop = CVAutomationFramework.ShouldStopBuilderBaseAttacks("drop_trophy", report, true, 1000, 5000, false, false, out string reason);

            Assert.True(stop);
            Assert.Equal("trophy_reached_min", reason);
        }

        [Fact]
        public void ShouldStopBuilderBaseAttacks_TrophyModeIgnoresExhaustedLoot()
        {
            var report = new BuilderBaseReportSnapshot(100, 100, 2500, 1, 2, 9, false, true, true, false, 0, 12, false, false, true);
            bool stop = CVAutomationFramework.ShouldStopBuilderBaseAttacks("trophy", report, true, 1000, 3000, false, false, out string reason);

            Assert.False(stop);
            Assert.Equal("none", reason);
        }

        [Fact]
        public void ShouldStopBuilderBaseAttacks_StarBonusStopsOnCompletedStarsBeforeLootExhausted()
        {
            var report = new BuilderBaseReportSnapshot(100, 100, 1500, 1, 2, 9, false, true, true, false, 0, 12, false, false, true);
            bool stop = CVAutomationFramework.ShouldStopBuilderBaseAttacks("star_bonus", report, false, 1000, 5000, false, false, out string reason);

            Assert.True(stop);
            Assert.Equal("star_bonus_completed", reason);
        }

        [Fact]
        public void ShouldStopBuilderBaseAttacks_UnreliableReportDoesNotStopAsLootExhausted()
        {
            var report = new BuilderBaseReportSnapshot(0, 0, 0, 0, 0, 0, false, false, false, false, 0, 0, false, false, false);
            bool stop = CVAutomationFramework.ShouldStopBuilderBaseAttacks("star_bonus", report, false, 1000, 5000, false, false, out string reason);

            Assert.False(stop);
            Assert.Equal("none", reason);
        }

        [Fact]
        public void ShouldStopBuilderBaseAttacks_StopsWhenTrophyReachesMax()
        {
            var report = new BuilderBaseReportSnapshot(100, 100, 3100, 1, 2, 9, true, true, true, true, 6, 12, false, false, true);
            bool stop = CVAutomationFramework.ShouldStopBuilderBaseAttacks("trophy", report, true, 1000, 3000, false, false, out string reason);

            Assert.True(stop);
            Assert.Equal("trophy_reached_max", reason);
        }

        [Fact]
        public void ReadDebouncedReport_ExhaustedThenAvailable_DoesNotStop()
        {
            var exhausted = new BuilderBaseReportSnapshot(100, 100, 2000, 1, 2, 9, false, true, false, false, 0, 0, false, false, true);
            var available = new BuilderBaseReportSnapshot(100, 100, 2000, 1, 2, 9, true, true, false, false, 0, 0, false, false, true);

            int readCount = 0;
            int sleepCount = 0;

            BuilderBaseReportSnapshot result = CVAutomationFramework.ReadDebouncedReport(
                () => ++readCount == 1 ? exhausted : available,
                "gold",
                false, 1000, 5000, false, false,
                System.Threading.CancellationToken.None,
                (ms, t) => { sleepCount++; return false; },
                out bool shouldStop,
                out string reason);

            Assert.Equal(2, readCount);
            Assert.Equal(1, sleepCount);
            Assert.False(shouldStop);
            Assert.Equal("none", reason);
            Assert.True(result.AttackAvailable);
        }

        [Fact]
        public void ReadDebouncedReport_TwoConsecutiveExhausted_StopsWithReason()
        {
            var exhausted = new BuilderBaseReportSnapshot(100, 100, 2000, 1, 2, 9, false, true, false, false, 0, 0, false, false, true);

            int readCount = 0;
            int sleepCount = 0;

            BuilderBaseReportSnapshot result = CVAutomationFramework.ReadDebouncedReport(
                () => { readCount++; return exhausted; },
                "gold",
                false, 1000, 5000, false, false,
                System.Threading.CancellationToken.None,
                (ms, t) => { sleepCount++; return false; },
                out bool shouldStop,
                out string reason);

            Assert.Equal(2, readCount);
            Assert.Equal(1, sleepCount);
            Assert.True(shouldStop);
            Assert.Equal("loot_exhausted", reason);
        }

        [Fact]
        public void ReadDebouncedReport_StorageFull_StopsOnFirstReadWithoutDelay()
        {
            var fullStorage = new BuilderBaseReportSnapshot(100, 100, 2000, 1, 2, 9, true, true, false, false, 0, 0, true, false, true);

            int readCount = 0;
            int sleepCount = 0;

            BuilderBaseReportSnapshot result = CVAutomationFramework.ReadDebouncedReport(
                () => { readCount++; return fullStorage; },
                "gold",
                false, 1000, 5000, true, false,
                System.Threading.CancellationToken.None,
                (ms, t) => { sleepCount++; return false; },
                out bool shouldStop,
                out string reason);

            Assert.Equal(1, readCount);
            Assert.Equal(0, sleepCount);
            Assert.True(shouldStop);
            Assert.Equal("storage_full", reason);
        }

        [Fact]
        public void ReadDebouncedReport_UnreliableReport_DoesNotStop()
        {
            var unreliable = new BuilderBaseReportSnapshot(0, 0, 0, 0, 0, 0, false, false, false, false, 0, 0, false, false, false);

            int readCount = 0;
            int sleepCount = 0;

            BuilderBaseReportSnapshot result = CVAutomationFramework.ReadDebouncedReport(
                () => { readCount++; return unreliable; },
                "gold",
                false, 1000, 5000, false, false,
                System.Threading.CancellationToken.None,
                (ms, t) => { sleepCount++; return false; },
                out bool shouldStop,
                out string reason);

            Assert.Equal(1, readCount);
            Assert.Equal(0, sleepCount);
            Assert.False(shouldStop);
            Assert.Equal("none", reason);
        }

        [Fact]
        public void ReadDebouncedReport_CancellationDuringSleep_AbortsWithoutStopping()
        {
            var exhausted = new BuilderBaseReportSnapshot(100, 100, 2000, 1, 2, 9, false, true, false, false, 0, 0, false, false, true);

            int readCount = 0;

            BuilderBaseReportSnapshot result = CVAutomationFramework.ReadDebouncedReport(
                () => { readCount++; return exhausted; },
                "gold",
                false, 1000, 5000, false, false,
                System.Threading.CancellationToken.None,
                (ms, t) => true,
                out bool shouldStop,
                out string reason);

            Assert.Equal(1, readCount);
            Assert.False(shouldStop);
        }

        private static BuilderBaseTroopSlot Slot(string name, int index)
            => new(name, new Point(100 + index * 80, 600), index, 1, 1.0);
    }
}
