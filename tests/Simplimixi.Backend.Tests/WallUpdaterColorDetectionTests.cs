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
            // Draw pure red pixels inside the calibrated Gold cost ROI.
            Cv2.Rectangle(screenshot, new Rect(920, 640, 30, 10), new Scalar(30, 30, 255), -1);

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

    }
}
