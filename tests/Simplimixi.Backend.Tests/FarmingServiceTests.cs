using CvAut;
using Xunit;

namespace CvAut.Backend.Tests
{
    public class FarmingServiceTests
    {
        [Fact]
        public void ScoutedResources_CalculatesTotalLoot()
        {
            var loot = new ScoutedResources(500000, 400000, 2000);
            Assert.Equal(500000, loot.Gold);
            Assert.Equal(400000, loot.Elixir);
            Assert.Equal(2000, loot.DarkElixir);
        }

        [Fact]
        public void MatchmakingEngine_RejectsInsufficientResources()
        {
            var engine = new MatchmakingEngine();
            var config = new FarmingTargetConfig(500000, 500000, 1000, 1000000, TargetSelectionLogic.And);
            var resources = new ScoutedResources(300000, 500000, 500);

            bool accepted = engine.ShouldAcceptTarget(resources, config, out string reason);

            Assert.False(accepted);
        }
    }
}
