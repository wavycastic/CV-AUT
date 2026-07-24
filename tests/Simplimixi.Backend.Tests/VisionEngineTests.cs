using System;
using System.IO;
using CvAut;
using OpenCvSharp;
using Xunit;

namespace CvAut.Backend.Tests
{
    public class VisionEngineTests
    {
        [Fact]
        public void VisionEngine_InitializesWithTemplatesDirectory()
        {
            string path = Path.Combine(AppContext.BaseDirectory, "assets", "Templates");
            using var vision = new VisionEngine(path);
            Assert.Equal(path, vision.TemplatesPath);
        }

        [Fact]
        public void VisionEngine_HandlesEmptyMatGracefully()
        {
            string path = Path.Combine(AppContext.BaseDirectory, "assets", "Templates");
            using var vision = new VisionEngine(path);
            using var emptyMat = new Mat();

            Point? result = vision.FindElement(emptyMat, "non_existent.png", 0.8, new Rect(0, 0, 100, 100), out double score);

            Assert.Null(result);
            Assert.Equal(0, score);
        }
    }
}
