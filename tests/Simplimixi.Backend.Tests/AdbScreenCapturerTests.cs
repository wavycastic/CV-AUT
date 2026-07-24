using System;
using CvAut.Adb;
using OpenCvSharp;
using Xunit;

namespace CvAut.Backend.Tests
{
    public class AdbScreenCapturerTests
    {
        [Fact]
        public void DecodeImageBytes_NullOrEmpty_ReturnsNull()
        {
            Assert.Null(AdbScreenCapturer.DecodeImageBytes(null!));
            Assert.Null(AdbScreenCapturer.DecodeImageBytes(Array.Empty<byte>()));
        }

        [Fact]
        public void DecodeImageBytes_ValidRawRgba_DecodesToBgrMat()
        {
            int width = 10;
            int height = 10;
            int pixelFormat = 1; // RGBA_8888
            byte[] bytes = new byte[12 + width * height * 4];

            BitConverter.GetBytes(width).CopyTo(bytes, 0);
            BitConverter.GetBytes(height).CopyTo(bytes, 4);
            BitConverter.GetBytes(pixelFormat).CopyTo(bytes, 8);

            // Fill pixels with Red (R=255, G=0, B=0, A=255)
            for (int i = 12; i < bytes.Length; i += 4)
            {
                bytes[i] = 255;     // R
                bytes[i + 1] = 0;   // G
                bytes[i + 2] = 0;   // B
                bytes[i + 3] = 255; // A
            }

            using Mat? mat = AdbScreenCapturer.DecodeImageBytes(bytes);

            Assert.NotNull(mat);
            Assert.False(mat!.Empty());
            Assert.Equal(width, mat.Width);
            Assert.Equal(height, mat.Height);
            Assert.Equal(MatType.CV_8UC3, mat.Type());

            // Check first pixel in BGR (B=0, G=0, R=255)
            Vec3b pixel = mat.At<Vec3b>(0, 0);
            Assert.Equal(0, pixel.Item0);   // B
            Assert.Equal(0, pixel.Item1);   // G
            Assert.Equal(255, pixel.Item2); // R
        }

        [Fact]
        public void DecodeImageBytes_ValidPngHeader_DecodesPngMat()
        {
            // Create a small Mat and encode to PNG bytes
            using Mat originalMat = new Mat(10, 10, MatType.CV_8UC3, new Scalar(0, 255, 0));
            Cv2.ImEncode(".png", originalMat, out byte[] pngBytes);

            using Mat? mat = AdbScreenCapturer.DecodeImageBytes(pngBytes);

            Assert.NotNull(mat);
            Assert.False(mat!.Empty());
            Assert.Equal(10, mat.Width);
            Assert.Equal(10, mat.Height);
        }
    }
}
