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
    /// <summary>
    /// Lớp tiện ích quản lý toàn bộ các giao tiếp qua ADB (Android Debug Bridge) với giả lập:
    /// - Kết nối và quản lý vòng đời Server ADB.
    /// - Thực hiện các cử chỉ cơ bản ngẫu nhiên: Chạm (Tap), Vuốt (Swipe), Chạm chuỗi (TapSequence).
    /// - Điều khiển thu phóng nâng cao qua UIAutomator2 Server bằng giao thức JSON-RPC (PinchIn).
    /// - Chụp ảnh màn hình giả lập tốc độ cao bằng exec-out screencap, kèm cơ chế chống ảnh lỗi/trống.
    /// </summary>
    public class ADBHelper : IDisposable
    {
        private bool _disposed;
        private readonly string _deviceAddress;
        private readonly DeviceData _device;
        private readonly string _host;
        private readonly int _port;
        
        // Đường dẫn tuyệt đối tới công cụ adb.exe đi kèm trong thư mục ứng dụng
        private readonly string _adbExePath = Path.Combine(AppContext.BaseDirectory, "adb", "adb.exe");
        
        // Client HTTP dùng để gửi lệnh điều khiển JSON-RPC tới server UIAutomator2 trên thiết bị Android
        private static readonly HttpClient UiAutomatorHttp = new HttpClient();
        private Process? _uiautomatorProcess;

        /// <summary>
        /// Khởi tạo đối tượng kết nối ADB tới giả lập Android.
        /// Tự động bật ADB Server và kết nối tới thiết bị mong muốn.
        /// </summary>
        /// <param name="host">Địa chỉ IP giả lập (Thường là localhost hoặc 127.0.0.1).</param>
        /// <param name="port">Cổng ADB (ví dụ: 5556 cho BlueStacks, 5555 cho MEmu).</param>
        public ADBHelper(string host = "127.0.0.1", int port = 5556)
        {
            _host = host;
            _port = port;

            // Khởi chạy ADB Server cục bộ
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

            // 1. Ưu tiên đúng địa chỉ cổng cấu hình cụ thể để tránh điều khiển nhầm giả lập khác đang mở.
            if (TryConnectAndSelectDevice(_host, _port, out _device))
            {
                Console.WriteLine($"[ADB] Đã kết nối đến thiết bị cấu hình: {_deviceAddress}");
                return;
            }

            // 2. Thử kết nối dự phòng sang các cổng phổ biến khác của BlueStacks, MEmu, LDPlayer, v.v.
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

            // 3. Cuối cùng mới lấy thiết bị đã được kích hoạt sẵn đầu tiên trong ADB Server (nếu có).
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

            // Mặc định tạo dữ liệu thiết bị trống nếu hoàn toàn không kết nối được (để tránh NullReference)
            _device = new DeviceData { Serial = _deviceAddress };
            Console.WriteLine($"[ADB WARNING] Không có thiết bị nào kết nối. Đã mặc định serial: {_deviceAddress}");
        }

        /// <summary>
        /// Giải phóng tiến trình UIAutomator2 chạy ngầm trên Windows.
        /// </summary>
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

        /// <summary>
        /// Thử kết nối ADB tới IP/Port và kiểm tra xem thiết bị đó có trong danh sách thiết bị nhận dạng được không.
        /// </summary>
        private bool TryConnectAndSelectDevice(string host, int port, out DeviceData device)
        {
            string serial = $"{host}:{port}";
            try
            {
                AdbClient.Instance.Connect(new IPEndPoint(IPAddress.Parse(host), port));
            }
            catch
            {
                // Có thể thiết bị đã kết nối sẵn
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
                // Xử lý lỗi
            }

            device = new DeviceData { Serial = serial };
            return false;
        }

        /// <summary>
        /// Đảm bảo thiết bị đã trực tuyến (online) và trong trạng thái sẵn sàng nhận lệnh "device".
        /// Chờ đợi tối đa theo thời gian timeout.
        /// </summary>
        /// <param name="timeoutSeconds">Thời gian chờ tối đa bằng giây.</param>
        /// <returns>True nếu sẵn sàng, False nếu hết giờ.</returns>
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
                    // Thiết bị có thể đã kết nối
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

        /// <summary>
        /// Truy vấn trạng thái kết nối của thiết bị bằng lệnh "adb get-state".
        /// </summary>
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

        /// <summary>
        /// Thực thi một lệnh shell Linux bất kỳ trên thiết bị Android qua ADB và trả về kết quả dạng chuỗi.
        /// </summary>
        /// <param name="command">Lệnh shell Android (ví dụ: 'pm list packages', 'input tap 10 20').</param>
        /// <returns>Kết quả chuỗi đầu ra (stdout) của lệnh.</returns>
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

        /// <summary>
        /// Thực hiện nhấp chuột (Tap) vào tọa độ chỉ định trên màn hình giả lập.
        /// </summary>
        /// <param name="x">Tọa độ x.</param>
        /// <param name="y">Tọa độ y.</param>
        public void Tap(int x, int y)
        {
            ExecuteShell($"input tap {x} {y}");
        }

        /// <summary>
        /// Thực hiện một chuỗi nhấp chuột nhanh liên tiếp tại danh sách các tọa độ Point chỉ định.
        /// Giúp tăng tốc độ thả quân (deploy troop) nhanh trong trận đánh Clash of Clans.
        /// Gộp lệnh bằng dấu chấm phẩy ';' để giảm thiểu độ trễ giao tiếp mạng ADB.
        /// </summary>
        /// <param name="points">Danh sách các tọa độ cần chạm.</param>
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

            // Gộp tất cả các lệnh tap lại và gửi đi trong 1 phiên làm việc
            ExecuteShell(string.Join("; ", commands));
        }

        /// <summary>
        /// Thực hiện thao tác vuốt (Swipe) từ tọa độ nguồn đến tọa độ đích với thời gian thực hiện chỉ định.
        /// </summary>
        public void Swipe(int x1, int y1, int x2, int y2, int durationMs = 300)
        {
            ExecuteShell($"input swipe {x1} {y1} {x2} {y2} {durationMs}");
        }

        /// <summary>
        /// Thực hiện lệnh thu nhỏ bản đồ (Zoom Out / Pinch-In).
        /// - Ưu tiên sử dụng máy chủ UIAutomator2 RPC để gửi cử chỉ hai ngón chính xác.
        /// - Fallback sang gửi đồng thời 2 luồng vuốt ngược chiều nhau trong shell ADB nếu UIAutomator2 gặp lỗi.
        /// </summary>
        /// <param name="count">Số lần thực hiện zoom out liên tiếp.</param>
        /// <param name="durationMs">Thời gian vuốt của mỗi lần zoom (dành cho fallback swipe).</param>
        /// <param name="intervalMs">Khoảng thời gian nghỉ giữa các lần zoom.</param>
        /// <returns>True nếu thực hiện thành công ít nhất một cử chỉ zoom.</returns>
        public bool PinchInZoomOut(int count = 5, int durationMs = 450, int intervalMs = 350)
        {
            // 1. Thử dùng cơ chế UIAutomator2 pinchIn
            if (TryUiAutomatorPinchIn(count, percent: 100, steps: 20, intervalMs))
            {
                return true;
            }

            Console.WriteLine("[ADB WARNING] UIAutomator2 pinch-in không chạy được. Thử fallback ADB swipe đồng thời...");
            bool anySuccess = false;

            // 2. Chạy fallback vuốt song song ngầm bằng lệnh sh
            for (int i = 0; i < count; i++)
            {
                // Gửi đồng thời lệnh vuốt hướng tâm từ trái và phải để mô phỏng bóp 2 ngón tay thu nhỏ bản đồ
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

        /// <summary>
        /// Gửi gói JSON-RPC cử chỉ pinchIn tới máy chủ UIAutomator2 trên thiết bị.
        /// </summary>
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
                    // Giải quyết lỗi ném sự kiện INJECT_EVENTS của Android bằng cách tap nhẹ và thử lại
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

        /// <summary>
        /// Đảm bảo máy chủ UIAutomator2 đã được cài đặt và đang chạy trên thiết bị để thực hiện đa điểm.
        /// Tự động tìm kiếm file u2.jar trong thư mục hệ thống, đẩy lên thiết bị và thiết lập chuyển tiếp cổng (forward 9008).
        /// </summary>
        private bool EnsureUiAutomator2Server()
        {
            // Thiết lập chuyển tiếp cổng cổng mạng local 9008 tới cổng 9008 trên giả lập Android
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

            // Cache cục bộ file jar để các phiên khởi động sau diễn ra nhanh hơn
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

            // Đẩy tệp .jar lên thư mục tạm của hệ điều hành Android
            RunAdb($"-s {_deviceAddress} push \"{jarPath}\" /data/local/tmp/u2.jar", waitForExit: true);
            RunAdb($"-s {_deviceAddress} forward tcp:9008 tcp:9008", waitForExit: true);

            // Khởi động Main class của UIAutomator2 server ngầm trên thiết bị Android
            _uiautomatorProcess ??= RunAdb(
                $"-s {_deviceAddress} shell \"CLASSPATH=/data/local/tmp/u2.jar app_process / com.wetest.uia2.Main\"",
                waitForExit: false
            );

            // Đợi server phản hồi ping trạng thái online
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

        /// <summary>
        /// Ping thử dịch vụ HTTP của UIAutomator2 Server trên thiết bị qua endpoint /ping.
        /// </summary>
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

        /// <summary>
        /// Gửi yêu cầu JSON-RPC 2.0 tới UIAutomator2 trên thiết bị.
        /// </summary>
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

        /// <summary>
        /// Tìm tệp tin u2.jar của dịch vụ UIAutomator2 bằng cách tìm kiếm trong các thư mục Downloads thường thấy.
        /// </summary>
        private string? FindUiAutomatorJar()
        {
            string localJar = Path.Combine(AppContext.BaseDirectory, "adb", "u2.jar");
            if (File.Exists(localJar)) return localJar;

            localJar = Path.Combine(Directory.GetCurrentDirectory(), "adb", "u2.jar");
            if (File.Exists(localJar)) return localJar;

            // Tìm kiếm ưu tiên trong thư mục E:\Download hoặc thư mục Downloads hệ thống
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
                    // Bỏ qua thư mục lỗi quyền truy cập
                }
            }

            return null;
        }

        /// <summary>
        /// Khởi tạo và thực thi trực tiếp tiến trình adb.exe trên hệ điều hành Windows.
        /// </summary>
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

        /// <summary>
        /// Chụp màn hình giả lập Android ở tốc độ cao và chuyển đổi sang dạng OpenCV Mat.
        /// Sử dụng kỹ thuật chuyển hướng luồng dữ liệu thô (exec-out screencap -p) của ADB.
        /// Tự động kiểm thử ảnh rỗng/đen (chuẩn sai lệch tiêu chuẩn stddev thấp) để bắt lỗi và thử lại tối đa 3 lần.
        /// </summary>
        /// <returns>Đối tượng Mat ảnh màu (BGR) chụp được, hoặc null nếu lỗi.</returns>
        public Mat? TakeScreenshot()
        {
            const int maxRetries = 3;
            for (int attempt = 1; attempt <= maxRetries; attempt++)
            {
                try
                {
                    // Chụp định dạng PNG và gửi trực tiếp dạng nhị phân qua stdout tránh lưu tệp trung gian làm xước ổ cứng
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

                    // Giải mã bytes nhị phân PNG thành đối tượng Mat màu
                    using Mat decoded = Cv2.ImDecode(imageBytes, ImreadModes.Color);
                    if (decoded.Empty())
                    {
                        Console.WriteLine($"[ADB WARNING] Không decode được ảnh màn hình lần {attempt}.");
                        Thread.Sleep(1000);
                        continue;
                    }

                    // Kiểm tra xem ảnh chụp màn hình có bị đen hoàn toàn (blank/freeze màn hình giả lập) hay không.
                    // Tính độ lệch chuẩn stddev của màu xám, nếu stddev < 3.0 thì chứng tỏ ảnh đơn sắc (chủ yếu là đen thui).
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
