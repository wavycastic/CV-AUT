using CvAut;
using OpenCvSharp;
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

        [Fact]
        public void IsActiveBattlePresent_ReturnsTrueWhenEndBattleRedButtonIsPresent()
        {
            var adb = new ADBHelper("127.0.0.1", 5555);
            var vision = new VisionEngine(System.IO.Path.Combine(System.AppContext.BaseDirectory, "assets", "Templates"));
            var farming = new FarmingService(
                adb,
                vision,
                System.IO.Path.Combine(System.AppContext.BaseDirectory, "assets", "Templates"),
                (ms, token) => false,
                () => false,
                () => true,
                token => true,
                msg => false,
                () => false);

            // Create 1600x900 image with red End Battle button at bottom-left ROI (20, 670, 180, 70)
            using Mat img = new Mat(900, 1600, MatType.CV_8UC3, new Scalar(50, 50, 50));
            Rect endBtnRoi = new Rect(30, 680, 100, 40);
            img.SubMat(endBtnRoi).SetTo(new Scalar(20, 20, 220)); // Bright Red in BGR

            bool isActive = farming.IsActiveBattlePresent(img, out double score);

            Assert.True(isActive);
            Assert.True(score > 0);
        }
    }
}
