using OpenCvSharp;
using System.IO;
using Xunit;

namespace CvAut.Backend.Tests
{
    public static class FixtureLoader
    {
        public static Mat LoadMandatory(string path)
        {
            if (!File.Exists(path))
            {
                Assert.Fail($"Mandatory fixture missing: {path}");
            }
            var img = Cv2.ImRead(path, ImreadModes.Color);
            if (img.Empty())
            {
                Assert.Fail($"Fixture image is empty or invalid: {path}");
            }
            return img;
        }

        public static Mat? LoadOptional(string path)
        {
            if (!File.Exists(path))
            {
                Assert.Skip($"Optional fixture missing: {path}");
                return null;
            }
            var img = Cv2.ImRead(path, ImreadModes.Color);
            if (img.Empty())
            {
                Assert.Skip($"Optional fixture empty: {path}");
                return null;
            }
            return img;
        }
    }
}
