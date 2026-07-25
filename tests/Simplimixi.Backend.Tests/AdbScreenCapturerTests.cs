using System;
using CvAut.Adb;
using OpenCvSharp;
using Xunit;

namespace CvAut.Backend.Tests
{
    public class AdbScreenCapturerTests
    {
        [Fact]
        public void Constructor_RejectsMissingExecutablePath()
        {
            Assert.Throws<ArgumentException>(() => new AdbScreenCapturer(string.Empty));
        }

        [Fact]
        public void DecodeImageBytes_NullOrEmpty_ReturnsNull()
        {
            Assert.Null(AdbScreenCapturer.DecodeImageBytes(null!));
            Assert.Null(AdbScreenCapturer.DecodeImageBytes(Array.Empty<byte>()));
        }

        [Fact]
        public void DecodeImageBytes_ValidRawRgba_DecodesToBgrMat()
        {
            const int width = 10;
            const int height = 10;
            byte[] bytes = new byte[12 + width * height * 4];

            BitConverter.GetBytes(width).CopyTo(bytes, 0);
            BitConverter.GetBytes(height).CopyTo(bytes, 4);
            BitConverter.GetBytes(1).CopyTo(bytes, 8);

            for (int i = 12; i < bytes.Length; i += 4)
            {
                bytes[i] = 255;
                bytes[i + 3] = 255;
            }

            using Mat? mat = AdbScreenCapturer.DecodeImageBytes(bytes);

            Assert.NotNull(mat);
            Assert.Equal(width, mat!.Width);
            Assert.Equal(height, mat.Height);
            Assert.Equal(MatType.CV_8UC3, mat.Type());
            Vec3b pixel = mat.At<Vec3b>(0, 0);
            Assert.Equal(0, pixel.Item0);
            Assert.Equal(0, pixel.Item1);
            Assert.Equal(255, pixel.Item2);
        }

        [Fact]
        public void DecodeImageBytes_ValidPngHeader_DecodesPngMat()
        {
            using var original = new Mat(10, 10, MatType.CV_8UC3, new Scalar(0, 255, 0));
            Cv2.ImEncode(".png", original, out byte[] pngBytes);

            using Mat? mat = AdbScreenCapturer.DecodeImageBytes(pngBytes);

            Assert.NotNull(mat);
            Assert.Equal(10, mat!.Width);
            Assert.Equal(10, mat.Height);
        }

        [Fact]
        public void IsBlankFrame_UniformImage_ReturnsTrue()
        {
            using var frame = new Mat(20, 20, MatType.CV_8UC3, Scalar.Black);

            Assert.True(AdbScreenCapturer.IsBlankFrame(frame));
        }

        [Fact]
        public void IsBlankFrame_VariedImage_ReturnsFalse()
        {
            using var frame = new Mat(20, 20, MatType.CV_8UC3, Scalar.Black);
            Cv2.Rectangle(frame, new Rect(0, 0, 10, 20), Scalar.White, -1);

            Assert.False(AdbScreenCapturer.IsBlankFrame(frame));
        }
    }
}
