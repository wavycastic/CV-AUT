using System;
using System.Diagnostics;
using System.IO;
using OpenCvSharp;

namespace CvAut.Adb
{
    /// <summary>
    /// Chuyên thực hiện chụp màn hình giả lập Android qua ADB (exec-out screencap) và giải mã thành ma trận OpenCV Mat.
    /// Hỗ trợ nạp mảng byte thô RGBA trực tiếp từ bộ nhớ tránh gây ép nén PNG làm sập máy ảo BlueStacks.
    /// </summary>
    internal class AdbScreenCapturer
    {
        private readonly string _adbExePath = Path.Combine(AppContext.BaseDirectory, "adb", "adb.exe");

        public Mat? TakeScreenshot(string deviceAddress)
        {
            const int maxRetries = 3;
            for (int attempt = 1; attempt <= maxRetries; attempt++)
            {
                try
                {
                    var processInfo = new ProcessStartInfo
                    {
                        FileName = _adbExePath,
                        Arguments = string.IsNullOrWhiteSpace(deviceAddress) ? "exec-out screencap" : $"-s {deviceAddress} exec-out screencap",
                        UseShellExecute = false,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        CreateNoWindow = true
                    };

                    using var process = Process.Start(processInfo);
                    if (process == null) continue;

                    using var memoryStream = new MemoryStream();
                    process.StandardOutput.BaseStream.CopyTo(memoryStream);
                    process.WaitForExit(5000);

                    byte[] imageBytes = memoryStream.ToArray();
                    if (imageBytes.Length == 0) continue;

                    Mat? mat = DecodeImageBytes(imageBytes);
                    if (mat != null && !mat.Empty())
                    {
                        return mat;
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[ADB WARNING] phase=screenshot status=retry attempt={attempt} reason=\"{ex.Message}\"");
                }
            }

            return null;
        }

        internal static Mat? DecodeImageBytes(byte[] imageBytes)
        {
            if (imageBytes == null || imageBytes.Length == 0) return null;

            // 1. Kiểm tra PNG Magic Header (0x89 'P' 'N' 'G' \r \n \x1a \n)
            if (imageBytes.Length >= 8 &&
                imageBytes[0] == 0x89 && imageBytes[1] == 0x50 && imageBytes[2] == 0x4E && imageBytes[3] == 0x47 &&
                imageBytes[4] == 0x0D && imageBytes[5] == 0x0A && imageBytes[6] == 0x1A && imageBytes[7] == 0x0A)
            {
                Mat decodedPng = Cv2.ImDecode(imageBytes, ImreadModes.Color);
                return (decodedPng != null && !decodedPng.Empty()) ? decodedPng : null;
            }

            // 2. Kiểm tra Raw RGBA Framebuffer Header (12 bytes: Width [4], Height [4], Format [4])
            if (imageBytes.Length >= 12)
            {
                int width = BitConverter.ToInt32(imageBytes, 0);
                int height = BitConverter.ToInt32(imageBytes, 4);

                if (width >= 1 && width <= 4000 && height >= 1 && height <= 4000)
                {
                    int expectedPixelBytes = width * height * 4;
                    if (imageBytes.Length >= 12 + expectedPixelBytes)
                    {
                        var handle = System.Runtime.InteropServices.GCHandle.Alloc(imageBytes, System.Runtime.InteropServices.GCHandleType.Pinned);
                        try
                        {
                            IntPtr ptr = IntPtr.Add(handle.AddrOfPinnedObject(), 12);
                            using Mat rawRgba = new Mat(height, width, MatType.CV_8UC4, ptr);
                            Mat bgrMat = new Mat();
                            Cv2.CvtColor(rawRgba, bgrMat, ColorConversionCodes.RGBA2BGR);
                            return bgrMat;
                        }
                        finally
                        {
                            handle.Free();
                        }
                    }
                }
            }

            // 3. Fallback ImDecode
            Mat fallbackDecoded = Cv2.ImDecode(imageBytes, ImreadModes.Color);
            return (fallbackDecoded != null && !fallbackDecoded.Empty()) ? fallbackDecoded : null;
        }
    }
}
