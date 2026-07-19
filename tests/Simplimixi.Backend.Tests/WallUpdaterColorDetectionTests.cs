using CvAut;
using OpenCvSharp;
using System;
using System.IO;
using Xunit;

namespace CvAut.Backend.Tests
{
    public class WallUpdaterColorDetectionTests
    {
        [Fact]
        public void IsUpgradeCostRed_RedTextInGoldRoi_ReturnsTrue()
        {
            using Mat screenshot = new(new Size(1600, 900), MatType.CV_8UC3, Scalar.White);
            // Draw pure red pixels in the Gold ROI (X=860..980, Y=635..668)
            Cv2.Rectangle(screenshot, new Rect(870, 640, 30, 10), new Scalar(30, 30, 255), -1);

            bool red = WallUpdater.IsUpgradeCostRed(screenshot, "gold", out double ratio, out int redPixels);

            // We drew a 30x10 = 300 pixels rectangle, which is > 120
            Assert.True(red);
            Assert.True(redPixels >= 120);
        }

        [Fact]
        public void IsUpgradeCostRed_WhiteTextInGoldRoi_ReturnsFalse()
        {
            using Mat screenshot = new(new Size(1600, 900), MatType.CV_8UC3, Scalar.White);

            bool red = WallUpdater.IsUpgradeCostRed(screenshot, "gold", out double ratio, out int redPixels);

            Assert.False(red);
            Assert.Equal(0, redPixels);
        }

        [Fact]
        public void IsUpgradeCostRed_RealAddWallScreenshot_DetectsGoldRedOnly()
        {
            string path = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "Screenshot_2026.07.13_19.18.00.890.png"));
            if (!File.Exists(path)) return;

            using Mat screenshot = Cv2.ImRead(path, ImreadModes.Color);

            bool goldRed = WallUpdater.IsUpgradeCostRed(screenshot, "gold", out _, out _);
            bool elixirRed = WallUpdater.IsUpgradeCostRed(screenshot, "elixir", out _, out _);

            Assert.True(goldRed);
            Assert.False(elixirRed);
        }

        [Fact]
        public void IsUpgradeCostRed_RealAddWallScreenshot_DetectsGoldAndElixirRed()
        {
            string path = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "Screenshot_2026.07.13_20.10.41.762.png"));
            if (!File.Exists(path)) return;

            using Mat screenshot = Cv2.ImRead(path, ImreadModes.Color);

            bool goldRed = WallUpdater.IsUpgradeCostRed(screenshot, "gold", out _, out _);
            bool elixirRed = WallUpdater.IsUpgradeCostRed(screenshot, "elixir", out _, out _);

            Assert.True(goldRed);
            Assert.True(elixirRed);
        }

        [Fact]
        public void IsUpgradeCostRed_RealAddWallScreenshot_DoesNotTreatWhiteSixtyThousandAsRed()
        {
            string path = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "Screenshot_2026.07.13_19.17.31.823.png"));
            if (!File.Exists(path)) return;

            using Mat screenshot = Cv2.ImRead(path, ImreadModes.Color);

            bool goldRed = WallUpdater.IsUpgradeCostRed(screenshot, "gold", out _, out _);
            bool elixirRed = WallUpdater.IsUpgradeCostRed(screenshot, "elixir", out _, out _);

            Assert.False(goldRed);
            Assert.False(elixirRed);
        }

        [Fact]
        public void IsUpgradeCostRed_RealAddWallScreenshot_20260712_DetectsExpectedResources()
        {
            string path = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "Screenshot_2026.07.12_18.54.32.003.png"));
            if (!File.Exists(path)) return;

            using Mat screenshot = Cv2.ImRead(path, ImreadModes.Color);

            bool goldRed = WallUpdater.IsUpgradeCostRed(screenshot, "gold", out _, out _);
            bool elixirRed = WallUpdater.IsUpgradeCostRed(screenshot, "elixir", out _, out _);

            Assert.False(goldRed);
            Assert.True(elixirRed);
        }

        [Fact]
        public void IsUpgradeCostRed_NewScreenshots_CorrectClassifications()
        {
            string rootDir = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
            
            // 21.17.24: Both affordable
            using (Mat img = Cv2.ImRead(Path.Combine(rootDir, "Screenshot_2026.07.13_21.17.24.625.png"), ImreadModes.Color))
            {
                if (!img.Empty())
                {
                    Assert.False(WallUpdater.IsUpgradeCostRed(img, "gold", out _, out _));
                    Assert.False(WallUpdater.IsUpgradeCostRed(img, "elixir", out _, out _));
                }
            }

            // 21.17.47: Gold affordable (noise is 72 px, below threshold 120), Elixir affordable
            using (Mat img = Cv2.ImRead(Path.Combine(rootDir, "Screenshot_2026.07.13_21.17.47.158.png"), ImreadModes.Color))
            {
                if (!img.Empty())
                {
                    Assert.False(WallUpdater.IsUpgradeCostRed(img, "gold", out _, out _));
                    Assert.False(WallUpdater.IsUpgradeCostRed(img, "elixir", out _, out _));
                }
            }

            // 21.17.59: Gold red (543 px), Elixir affordable
            using (Mat img = Cv2.ImRead(Path.Combine(rootDir, "Screenshot_2026.07.13_21.17.59.275.png"), ImreadModes.Color))
            {
                if (!img.Empty())
                {
                    Assert.True(WallUpdater.IsUpgradeCostRed(img, "gold", out _, out _));
                    Assert.False(WallUpdater.IsUpgradeCostRed(img, "elixir", out _, out _));
                }
            }

            // 21.18.03: Gold red (544 px), Elixir red (468 px)
            using (Mat img = Cv2.ImRead(Path.Combine(rootDir, "Screenshot_2026.07.13_21.18.03.591.png"), ImreadModes.Color))
            {
                if (!img.Empty())
                {
                    Assert.True(WallUpdater.IsUpgradeCostRed(img, "gold", out _, out _));
                    Assert.True(WallUpdater.IsUpgradeCostRed(img, "elixir", out _, out _));
                }
            }

            // 21.18.32: Gold affordable, Elixir red (579 px)
            using (Mat img = Cv2.ImRead(Path.Combine(rootDir, "Screenshot_2026.07.13_21.18.32.108.png"), ImreadModes.Color))
            {
                if (!img.Empty())
                {
                    Assert.False(WallUpdater.IsUpgradeCostRed(img, "gold", out _, out _));
                    Assert.True(WallUpdater.IsUpgradeCostRed(img, "elixir", out _, out _));
                }
            }
        }
    }
}
