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
        public void IsActiveBattlePresent_DarkScreen_ReturnsFalse()
        {
            VisionEngine vision = CreateVisionEngine();

            using Mat img = new Mat(900, 1600, MatType.CV_8UC3, new Scalar(40, 40, 40));
            bool isActive = BattleScreenDetector.IsActiveBattlePresent(vision, img, out double score);

            Assert.False(isActive);
            Assert.Equal(0, score);
        }

        [Fact]
        public void IsActiveBattlePresent_SmallRedNoise_ReturnsFalse()
        {
            VisionEngine vision = CreateVisionEngine();

            using Mat img = new Mat(900, 1600, MatType.CV_8UC3, new Scalar(40, 40, 40));
            // Small 5x10 red patch (50 pixels) -> below 400 pixel threshold
            Rect noiseRoi = new Rect(30, 680, 5, 10);
            using (Mat noiseArea = new Mat(img, noiseRoi))
            {
                noiseArea.SetTo(new Scalar(20, 20, 220));
            }

            bool isActive = BattleScreenDetector.IsActiveBattlePresent(vision, img, out double score);

            Assert.False(isActive);
            Assert.Equal(0, score);
        }

        [Fact]
        public void IsActiveBattlePresent_RedButtonShape_ReturnsTrue()
        {
            VisionEngine vision = CreateVisionEngine();

            using Mat img = new Mat(900, 1600, MatType.CV_8UC3, new Scalar(50, 50, 50));
            Rect endBtnRoi = new Rect(30, 680, 100, 40);
            using (Mat endButton = new Mat(img, endBtnRoi))
            {
                endButton.SetTo(new Scalar(20, 20, 220)); // Bright Red in BGR
            }

            bool isActive = BattleScreenDetector.IsActiveBattlePresent(vision, img, out double score);

            Assert.True(isActive);
            Assert.True(score > 0);
        }

        [Theory]
        [InlineData(30, 30)]
        [InlineData(15, 60)]
        public void IsActiveBattlePresent_LargeRedWrongShape_ReturnsFalse(int width, int height)
        {
            VisionEngine vision = CreateVisionEngine();
            using Mat img = new Mat(900, 1600, MatType.CV_8UC3, new Scalar(40, 40, 40));
            using (Mat redArea = new Mat(img, new Rect(30, 675, width, height)))
            {
                redArea.SetTo(new Scalar(20, 20, 220));
            }

            bool isActive = BattleScreenDetector.IsActiveBattlePresent(vision, img, out double score);

            Assert.False(isActive);
            Assert.Equal(0, score);
        }

        [Fact]
        public void IsActiveBattlePresent_DisconnectedRedAreas_ReturnsFalse()
        {
            VisionEngine vision = CreateVisionEngine();
            using Mat img = new Mat(900, 1600, MatType.CV_8UC3, new Scalar(40, 40, 40));
            Rect[] areas =
            {
                new Rect(30, 680, 20, 15),
                new Rect(70, 680, 20, 15),
                new Rect(110, 680, 20, 15),
            };

            foreach (Rect area in areas)
            {
                using Mat redArea = new Mat(img, area);
                redArea.SetTo(new Scalar(20, 20, 220));
            }

            bool isActive = BattleScreenDetector.IsActiveBattlePresent(vision, img, out double score);

            Assert.False(isActive);
            Assert.Equal(0, score);
        }

        private static VisionEngine CreateVisionEngine()
        {
            return new VisionEngine(System.IO.Path.Combine(System.AppContext.BaseDirectory, "assets", "Templates"));
        }
    }
}
