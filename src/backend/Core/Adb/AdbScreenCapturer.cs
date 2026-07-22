using System;
using System.Diagnostics;
using System.IO;
using OpenCvSharp;

namespace CvAut.Adb
{
    /// <summary>
    /// Chuyên thực hiện chụp màn hình giả lập Android qua ADB (exec-out screencap -p) và giải mã thành ma trận OpenCV Mat.
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
                        Arguments = string.IsNullOrWhiteSpace(deviceAddress) ? "exec-out screencap -p" : $"-s {deviceAddress} exec-out screencap -p",
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

                    Mat mat = Cv2.ImDecode(imageBytes, ImreadModes.Color);
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
    }
}
