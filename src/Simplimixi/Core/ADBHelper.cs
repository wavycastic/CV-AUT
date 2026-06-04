using System;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using OpenCvSharp;
using SharpAdbClient;
using Point = OpenCvSharp.Point;

namespace CvAut
{
    public class ADBHelper : IDisposable
    {
        private bool _disposed;
        private readonly string _deviceAddress;
        private readonly DeviceData _device;
        private readonly string _host;
        private readonly int _port;
        private readonly string _adbExePath = Path.Combine(AppContext.BaseDirectory, "adb", "adb.exe");
        private static readonly HttpClient UiAutomatorHttp = new HttpClient();
        private Process? _uiautomatorProcess;

        public ADBHelper(string host = "127.0.0.1", int port = 5556)
        {
            _host = host;
            _port = port;

            // Initialize ADB Client
            AdbServer server = new AdbServer();
            try
            {
                server.StartServer(_adbExePath, restartServerIfNewer: false);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ADB WARNING] Không thể khởi động server ADB: {ex.Message}");
            }

            _deviceAddress = $"{host}:{port}";

            // 1. Ưu tiên đúng endpoint trong config. Nếu có nhiều giả lập đang mở,
            // lấy connectedDevices[0] có thể gửi zoom sang nhầm giả lập.
            if (TryConnectAndSelectDevice(_host, _port, out _device))
            {
                Console.WriteLine($"[ADB] Đã kết nối đến thiết bị cấu hình: {_deviceAddress}");
                return;
            }

            // 2. Thử kết nối sang các cổng phổ biến khác của BlueStacks, MEmu, v.v.
            int[] fallbackPorts = { 5555, 5556, 5557, 5554, 5565 };
            foreach (int p in fallbackPorts)
            {
                if (p == port) continue;
                if (TryConnectAndSelectDevice(_host, p, out _device))
                {
                    _deviceAddress = $"{_host}:{p}";
                    Console.WriteLine($"[ADB] Tự động dò tìm và kết nối thành công tới cổng dự phòng: {_deviceAddress}");
                    return;
                }
            }

            // 3. Cuối cùng mới dùng thiết bị đã có sẵn trong ADB Server.
            try
            {
                var connectedDevices = AdbClient.Instance.GetDevices();
                if (connectedDevices != null && connectedDevices.Count > 0)
                {
                    _device = connectedDevices[0];
                    _deviceAddress = _device.Serial;
                    Console.WriteLine($"[ADB] Tự động phát hiện thiết bị đang hoạt động: {_deviceAddress}");
                    return;
                }
            }
            catch (Exception)
            {
                Console.WriteLine("[ADB WARNING] Không thể lấy danh sách thiết bị từ AdbClient.");
            }

            _device = new DeviceData { Serial = _deviceAddress };
            Console.WriteLine($"[ADB WARNING] Không có thiết bị nào kết nối. Đã mặc định serial: {_deviceAddress}");
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            if (_uiautomatorProcess != null && !_uiautomatorProcess.HasExited)
            {
                try { _uiautomatorProcess.Kill(); } catch { }
                _uiautomatorProcess.Dispose();
            }
        }

        private bool TryConnectAndSelectDevice(string host, int port, out DeviceData device)
        {
            string serial = $"{host}:{port}";
            try
            {
                AdbClient.Instance.Connect(new IPEndPoint(IPAddress.Parse(host), port));
            }
            catch
            {
                // Có thể thiết bị đã connected sẵn; vẫn thử tìm serial trong danh sách.
            }

            try
            {
                var devices = AdbClient.Instance.GetDevices();
                foreach (var connectedDevice in devices)
                {
                    if (string.Equals(connectedDevice.Serial, serial, StringComparison.OrdinalIgnoreCase))
                    {
                        device = connectedDevice;
                        return true;
                    }
                }
            }
            catch
            {
                // Fall through to false.
            }

            device = new DeviceData { Serial = serial };
            return false;
        }

        public bool EnsureConnectedOnline(int timeoutSeconds = 30)
        {
            DateTime deadline = DateTime.Now.AddSeconds(timeoutSeconds);
            while (DateTime.Now < deadline)
            {
                try
                {
                    AdbClient.Instance.Connect(new IPEndPoint(IPAddress.Parse(_host), _port));
                }
                catch
                {
                    // The device may already be connected; verify state below.
                }

                string state = GetDeviceState();
                if (string.Equals(state, "device", StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }

                Thread.Sleep(1000);
            }

            return false;
        }

        public string GetDeviceState()
        {
            try
            {
                var processInfo = new ProcessStartInfo
                {
                    FileName = _adbExePath,
                    Arguments = $"-s {_deviceAddress} get-state",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                };

                using var process = Process.Start(processInfo);
                if (process == null) return "unknown";

                string output = process.StandardOutput.ReadToEnd().Trim();
                process.WaitForExit(5000);
                return string.IsNullOrWhiteSpace(output) ? "unknown" : output;
            }
            catch
            {
                return "unknown";
            }
        }

        public string ExecuteShell(string command)
        {
            try
            {
                var receiver = new ConsoleOutputReceiver();
                AdbClient.Instance.ExecuteRemoteCommand(command, _device, receiver);
                return receiver.ToString().Trim();
            }
            catch (Exception ex)
            {
                return $"Error: {ex.Message}";
            }
        }

        public void Tap(int x, int y)
        {
            ExecuteShell($"input tap {x} {y}");
        }

        public void TapSequence(IEnumerable<Point> points)
        {
            var commands = new List<string>();
            foreach (Point point in points)
            {
                commands.Add($"input tap {point.X} {point.Y}");
            }

            if (commands.Count == 0)
            {
                return;
            }

            ExecuteShell(string.Join("; ", commands));
        }

        public void Swipe(int x1, int y1, int x2, int y2, int durationMs = 300)
        {
            ExecuteShell($"input swipe {x1} {y1} {x2} {y2} {durationMs}");
        }

        public bool PinchInZoomOut(int count = 5, int durationMs = 450, int intervalMs = 350)
        {
            if (TryUiAutomatorPinchIn(count, percent: 100, steps: 20, intervalMs))
            {
                return true;
            }

            Console.WriteLine("[ADB WARNING] UIAutomator2 pinch-in không chạy được. Thử fallback ADB swipe đồng thời...");
            bool anySuccess = false;

            for (int i = 0; i < count; i++)
            {
                // Simulate a two-finger pinch-in. Android's input tool has no direct
                // multi-touch primitive, so run two swipes concurrently in the shell.
                string result = ExecuteShell(
                    "sh -c \"input swipe 360 450 790 450 " + durationMs +
                    " & input swipe 1240 450 810 450 " + durationMs +
                    " & wait\""
                );

                if (!result.StartsWith("Error:", StringComparison.OrdinalIgnoreCase))
                {
                    anySuccess = true;
                }

                Thread.Sleep(intervalMs);
            }

            return anySuccess;
        }

        private bool TryUiAutomatorPinchIn(int count, int percent, int steps, int intervalMs)
        {
            if (!EnsureUiAutomator2Server())
            {
                return false;
            }

            bool anySuccess = false;
            for (int i = 0; i < count; i++)
            {
                bool ok = SendUiAutomatorJsonRpc("pinchIn", new object[]
                {
                    new Dictionary<string, object>
                    {
                        ["mask"] = 0,
                        ["childOrSibling"] = Array.Empty<object>(),
                        ["childOrSiblingSelector"] = Array.Empty<object>()
                    },
                    percent,
                    steps
                });

                if (ok)
                {
                    anySuccess = true;
                }
                else
                {
                    // Simplicity cũng tap nhẹ rồi retry khi UIAutomator2 gặp lỗi INJECT_EVENTS.
                    ExecuteShell("input tap 5 5");
                    Thread.Sleep(500);
                    ok = SendUiAutomatorJsonRpc("pinchIn", new object[]
                    {
                        new Dictionary<string, object>
                        {
                            ["mask"] = 0,
                            ["childOrSibling"] = Array.Empty<object>(),
                            ["childOrSiblingSelector"] = Array.Empty<object>()
                        },
                        percent,
                        steps
                    });
                    anySuccess |= ok;
                }

                if (i < count - 1)
                {
                    Thread.Sleep(intervalMs);
                }
            }

            return anySuccess;
        }

        private bool EnsureUiAutomator2Server()
        {
            RunAdb($"-s {_deviceAddress} forward tcp:9008 tcp:9008", waitForExit: true);
            if (PingUiAutomator2Server())
            {
                return true;
            }

            string? jarPath = FindUiAutomatorJar();
            if (jarPath == null)
            {
                Console.WriteLine("[ADB WARNING] Không tìm thấy u2.jar của UIAutomator2 trong repo hoặc thư mục Simplicity.");
                return false;
            }

            // Cache it locally so we don't have to scan the disk again
            string destJar = Path.Combine(AppContext.BaseDirectory, "adb", "u2.jar");
            if (!File.Exists(destJar))
            {
                try
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(destJar)!);
                    File.Copy(jarPath, destJar, overwrite: true);
                    Console.WriteLine($"[ADB] Đã cache u2.jar vào thư mục build: {destJar}");
                }
                catch { }
            }

            string rootJarDir = Path.Combine(Directory.GetCurrentDirectory(), "adb");
            string rootJar = Path.Combine(rootJarDir, "u2.jar");
            if (!File.Exists(rootJar) && Directory.Exists(rootJarDir))
            {
                try { File.Copy(jarPath, rootJar, overwrite: true); } catch { }
            }

            RunAdb($"-s {_deviceAddress} push \"{jarPath}\" /data/local/tmp/u2.jar", waitForExit: true);
            RunAdb($"-s {_deviceAddress} forward tcp:9008 tcp:9008", waitForExit: true);

            _uiautomatorProcess ??= RunAdb(
                $"-s {_deviceAddress} shell \"CLASSPATH=/data/local/tmp/u2.jar app_process / com.wetest.uia2.Main\"",
                waitForExit: false
            );

            DateTime deadline = DateTime.Now.AddSeconds(10);
            while (DateTime.Now < deadline)
            {
                if (PingUiAutomator2Server())
                {
                    return true;
                }

                Thread.Sleep(500);
            }

            return false;
        }

        private bool PingUiAutomator2Server()
        {
            try
            {
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
                string result = UiAutomatorHttp.GetStringAsync("http://127.0.0.1:9008/ping", cts.Token)
                    .GetAwaiter()
                    .GetResult();
                return result.Trim().Equals("pong", StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                return false;
            }
        }

        private bool SendUiAutomatorJsonRpc(string method, object[] parameters)
        {
            try
            {
                var payload = new
                {
                    jsonrpc = "2.0",
                    id = 1,
                    method,
                    @params = parameters
                };

                string json = JsonSerializer.Serialize(payload);
                using var content = new StringContent(json, Encoding.UTF8, "application/json");
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(8));
                using HttpResponseMessage response = UiAutomatorHttp.PostAsync(
                    "http://127.0.0.1:9008/jsonrpc/0",
                    content,
                    cts.Token
                ).GetAwaiter().GetResult();

                string body = response.Content.ReadAsStringAsync(cts.Token).GetAwaiter().GetResult();
                if (!response.IsSuccessStatusCode)
                {
                    Console.WriteLine($"[ADB WARNING] UIAutomator2 HTTP {(int)response.StatusCode}: {body}");
                    return false;
                }

                using JsonDocument doc = JsonDocument.Parse(body);
                if (doc.RootElement.TryGetProperty("error", out JsonElement error))
                {
                    Console.WriteLine($"[ADB WARNING] UIAutomator2 RPC error: {error}");
                    return false;
                }

                return doc.RootElement.TryGetProperty("result", out _);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ADB WARNING] UIAutomator2 RPC không thành công: {ex.Message}");
                return false;
            }
        }

        private string? FindUiAutomatorJar()
        {
            string localJar = Path.Combine(AppContext.BaseDirectory, "adb", "u2.jar");
            if (File.Exists(localJar)) return localJar;

            localJar = Path.Combine(Directory.GetCurrentDirectory(), "adb", "u2.jar");
            if (File.Exists(localJar)) return localJar;

            // Search E:\Download first (primary) and fall back to system Downloads folder
            string[] searchRoots = {
                @"E:\Download",
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads")
            };

            foreach (string downloadRoot in searchRoots)
            {
                if (!Directory.Exists(downloadRoot)) continue;

                try
                {
                    foreach (string file in Directory.EnumerateFiles(downloadRoot, "u2.jar", SearchOption.AllDirectories))
                    {
                        if (file.Contains("Simplicity", StringComparison.OrdinalIgnoreCase) &&
                            file.Contains("uiautomator2", StringComparison.OrdinalIgnoreCase))
                        {
                            return file;
                        }
                    }
                }
                catch
                {
                    // Ignore inaccessible folders under Download.
                }
            }

            return null;
        }

        private Process? RunAdb(string arguments, bool waitForExit)
        {
            try
            {
                var processInfo = new ProcessStartInfo
                {
                    FileName = _adbExePath,
                    Arguments = arguments,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = waitForExit,
                    RedirectStandardError = waitForExit
                };

                var process = Process.Start(processInfo);
                if (process == null) return null;

                if (waitForExit)
                {
                    process.WaitForExit(15000);
                    process.Dispose();
                    return null;
                }

                return process;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ADB WARNING] Không chạy được adb {arguments}: {ex.Message}");
                return null;
            }
        }

        public Mat? TakeScreenshot()
        {
            const int maxRetries = 3;
            for (int attempt = 1; attempt <= maxRetries; attempt++)
            {
                try
                {
                    // Port từ Simplicity screenshot_utils.py:
                    // exec-out screencap -p, retry nếu ảnh lỗi hoặc blank.
                    var processInfo = new ProcessStartInfo
                    {
                        FileName = _adbExePath,
                        Arguments = $"-s {_deviceAddress} exec-out screencap -p",
                        UseShellExecute = false,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        CreateNoWindow = true
                    };

                    using var process = Process.Start(processInfo);
                    if (process == null) return null;

                    using var ms = new MemoryStream();
                    string stderr = "";
                    process.ErrorDataReceived += (_, e) => { if (e.Data != null) stderr += e.Data; };
                    process.BeginErrorReadLine();
                    process.StandardOutput.BaseStream.CopyTo(ms);
                    process.WaitForExit();

                    if (process.ExitCode != 0)
                    {
                        Console.WriteLine($"[ADB WARNING] Lỗi chụp màn hình ADB lần {attempt}: {stderr.Trim()}");
                        Thread.Sleep(1000);
                        continue;
                    }

                    byte[] imageBytes = ms.ToArray();
                    if (imageBytes.Length == 0)
                    {
                        Console.WriteLine($"[ADB WARNING] ADB trả ảnh rỗng lần {attempt}.");
                        Thread.Sleep(1000);
                        continue;
                    }

                    using Mat decoded = Cv2.ImDecode(imageBytes, ImreadModes.Color);
                    if (decoded.Empty())
                    {
                        Console.WriteLine($"[ADB WARNING] Không decode được ảnh màn hình lần {attempt}.");
                        Thread.Sleep(1000);
                        continue;
                    }

                    using Mat gray = new Mat();
                    Cv2.CvtColor(decoded, gray, ColorConversionCodes.BGR2GRAY);
                    Cv2.MeanStdDev(gray, out _, out Scalar stddev);
                    if (stddev.Val0 < 3.0)
                    {
                        Console.WriteLine($"[ADB WARNING] Ảnh màn hình gần như blank lần {attempt} (std={stddev.Val0:F2}).");
                        Thread.Sleep(1000);
                        continue;
                    }

                    return decoded.Clone();
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[ADB ERROR] Không thể chụp màn hình giả lập lần {attempt}: {ex.Message}");
                    Thread.Sleep(1000);
                }
            }

            return null;
        }
    }
}
