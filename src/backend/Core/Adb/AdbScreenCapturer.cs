using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using OpenCvSharp;

namespace CvAut.Adb
{
    /// <summary>
    /// Captures Android frames through adb exec-out and rejects empty, invalid, or blank frames.
    /// </summary>
    internal sealed class AdbScreenCapturer : IAdbScreenCapturer
    {
        private const int MaxRetries = 3;
        private const int CopyTimeoutMs = 10000;
        private const int ExitTimeoutMs = 5000;
        private const int RetryDelayMs = 1000;
        private const double BlankFrameStdDevThreshold = 3.0;

        private readonly string _adbExePath;

        public AdbScreenCapturer()
            : this(Path.Combine(AppContext.BaseDirectory, "adb", "adb.exe"))
        {
        }

        internal AdbScreenCapturer(string adbExePath)
        {
            _adbExePath = string.IsNullOrWhiteSpace(adbExePath)
                ? throw new ArgumentException("ADB executable path is required.", nameof(adbExePath))
                : adbExePath;
        }

        public Mat? TakeScreenshot(string deviceAddress)
            => Capture(deviceAddress, new FramePacer());

        public Mat? Capture(string deviceAddress, FramePacer framePacer, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(framePacer);

            for (int attempt = 1; attempt <= MaxRetries && !token.IsCancellationRequested; attempt++)
            {
                Stopwatch captureStopwatch = FramePacer.StartCaptureMeasurement();
                try
                {
                    var startInfo = new ProcessStartInfo
                    {
                        FileName = _adbExePath,
                        Arguments = string.IsNullOrWhiteSpace(deviceAddress)
                            ? "exec-out screencap"
                            : $"-s {deviceAddress} exec-out screencap",
                        UseShellExecute = false,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        CreateNoWindow = true
                    };

                    using Process? process = Process.Start(startInfo);
                    if (process is null)
                    {
                        LogRetry(attempt, "start_failed");
                        if (WaitBeforeRetry(token)) return null;
                        continue;
                    }

                    using var memoryStream = new MemoryStream();
                    Task copyTask = process.StandardOutput.BaseStream.CopyToAsync(memoryStream, token);
                    Task<string> errorTask = process.StandardError.ReadToEndAsync(token);

                    if (!copyTask.Wait(CopyTimeoutMs) || !process.WaitForExit(ExitTimeoutMs))
                    {
                        TryKill(process);
                        LogRetry(attempt, "timeout");
                        if (WaitBeforeRetry(token)) return null;
                        continue;
                    }

                    if (process.ExitCode != 0)
                    {
                        string error = errorTask.IsCompletedSuccessfully
                            ? errorTask.Result.Trim()
                            : string.Empty;
                        LogRetry(attempt, "process_failed", error);
                        if (WaitBeforeRetry(token)) return null;
                        continue;
                    }

                    byte[] imageBytes = memoryStream.ToArray();
                    if (imageBytes.Length == 0)
                    {
                        LogRetry(attempt, "empty");
                        if (WaitBeforeRetry(token)) return null;
                        continue;
                    }

                    using Mat? decoded = DecodeImageBytes(imageBytes);
                    if (decoded is null || decoded.Empty())
                    {
                        LogRetry(attempt, "decode_fail");
                        if (WaitBeforeRetry(token)) return null;
                        continue;
                    }

                    if (IsBlankFrame(decoded))
                    {
                        LogRetry(attempt, "blank");
                        if (WaitBeforeRetry(token)) return null;
                        continue;
                    }

                    captureStopwatch.Stop();
                    framePacer.RecordCapture(captureStopwatch.ElapsedMilliseconds);
                    return decoded.Clone();
                }
                catch (OperationCanceledException) when (token.IsCancellationRequested)
                {
                    return null;
                }
                catch (Exception ex)
                {
                    LogRetry(attempt, "exception", ex.Message);
                    if (WaitBeforeRetry(token)) return null;
                }
            }

            return null;
        }

        internal static bool IsBlankFrame(Mat frame)
        {
            ArgumentNullException.ThrowIfNull(frame);
            if (frame.Empty()) return true;

            using var gray = new Mat();
            if (frame.Channels() == 1)
            {
                frame.CopyTo(gray);
            }
            else
            {
                Cv2.CvtColor(frame, gray, ColorConversionCodes.BGR2GRAY);
            }

            Cv2.MeanStdDev(gray, out _, out Scalar standardDeviation);
            return standardDeviation.Val0 < BlankFrameStdDevThreshold;
        }

        internal static Mat? DecodeImageBytes(byte[] imageBytes)
        {
            if (imageBytes is null || imageBytes.Length == 0) return null;

            if (imageBytes.Length >= 8 &&
                imageBytes[0] == 0x89 && imageBytes[1] == 0x50 &&
                imageBytes[2] == 0x4E && imageBytes[3] == 0x47 &&
                imageBytes[4] == 0x0D && imageBytes[5] == 0x0A &&
                imageBytes[6] == 0x1A && imageBytes[7] == 0x0A)
            {
                Mat decodedPng = Cv2.ImDecode(imageBytes, ImreadModes.Color);
                return !decodedPng.Empty() ? decodedPng : null;
            }

            if (imageBytes.Length >= 12)
            {
                int width = BitConverter.ToInt32(imageBytes, 0);
                int height = BitConverter.ToInt32(imageBytes, 4);

                if (width is >= 1 and <= 4000 && height is >= 1 and <= 4000)
                {
                    int expectedPixelBytes = width * height * 4;
                    if (imageBytes.Length >= 12 + expectedPixelBytes)
                    {
                        var handle = System.Runtime.InteropServices.GCHandle.Alloc(
                            imageBytes,
                            System.Runtime.InteropServices.GCHandleType.Pinned);
                        try
                        {
                            IntPtr pointer = IntPtr.Add(handle.AddrOfPinnedObject(), 12);
                            using var rawRgba = new Mat(height, width, MatType.CV_8UC4, pointer);
                            var bgr = new Mat();
                            Cv2.CvtColor(rawRgba, bgr, ColorConversionCodes.RGBA2BGR);
                            return bgr;
                        }
                        finally
                        {
                            handle.Free();
                        }
                    }
                }
            }

            Mat fallback = Cv2.ImDecode(imageBytes, ImreadModes.Color);
            return !fallback.Empty() ? fallback : null;
        }

        private static bool WaitBeforeRetry(CancellationToken token)
            => token.WaitHandle.WaitOne(RetryDelayMs);

        private static void TryKill(Process process)
        {
            try
            {
                process.Kill(entireProcessTree: true);
                process.WaitForExit(1000);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ADB WARNING] phase=screenshot status=pending action=kill_timeout_process reason=\"{ex.Message}\"");
            }
        }

        private static void LogRetry(int attempt, string reason, string details = "")
        {
            string suffix = string.IsNullOrWhiteSpace(details)
                ? string.Empty
                : $" details=\"{details}\"";
            Console.WriteLine($"[ADB WARNING] phase=screenshot status=retry reason=\"{reason}\" attempt={attempt}{suffix}");
        }
    }
}
