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
        [InlineData(126, 0, 0)]
        public void ParseStarPair_SupportsTwoDigitMaximum(int raw, int expectedRemaining, int expectedMax)
        {
            (int remaining, int max) = BuilderBaseReport.ParseStarPair(raw);

            Assert.Equal(expectedRemaining, remaining);
            Assert.Equal(expectedMax, max);
        }

        [Fact]
        public void ScaleMbrPoint_ScalesGoldEndFrom860x732To1600x900()
        {
            Point point = BuilderBaseAttacks.ScaleMbrPoint(632, 406, 1600, 900);

            Assert.Equal(new Point(1176, 499), point);
        }

        private static BuilderBaseTroopSlot Slot(string name, int index)
            => new(name, new Point(100 + index * 80, 600), index, 1, 1.0);
    }
}
