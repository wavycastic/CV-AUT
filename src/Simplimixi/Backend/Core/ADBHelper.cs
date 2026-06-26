using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
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

        public Func<bool>? BeforeInputAction { get; set; }

        public string DeviceAddress => _deviceAddress;

        /// <summary>
        /// Khởi tạo đối tượng kết nối ADB tới giả lập Android.
        /// Tự động bật ADB Server và kết nối tới thiết bị mong muốn.
        /// </summary>
        /// <param name="host">Địa chỉ IP giả lập (Thường là localhost hoặc 127.0.0.1).</param>
        /// <param name="port">Cổng ADB (ví dụ: 5556 cho BlueStacks, 5555 cho MEmu).</param>
        public ADBHelper(string host = "127.0.0.1", int port = 5556, string? preferredSerial = null)
        {
            // Khởi chạy ADB Server cục bộ
            AdbServer server = new AdbServer();
            try
            {
                server.StartServer(_adbExePath, restartServerIfNewer: false);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ADB WARNING] phase=init status=fail action=start_server reason=\"{ex.Message}\"");
            }

            string activeHost = host;
            int activePort = port;
            string activeAddress = string.IsNullOrWhiteSpace(preferredSerial) ? $"{host}:{port}" : preferredSerial.Trim();
            DeviceData activeDevice;

            // Lấy thêm các cổng tự động từ bluestacks.conf nếu có
            var dynamicPorts = GetBlueStacksPortsFromConfig();
            var fallbackPorts = new System.Collections.Generic.List<int>();
            foreach (int dp in dynamicPorts)
            {
                if (dp != port && !fallbackPorts.Contains(dp))
                {
                    fallbackPorts.Add(dp);
                }
            }
            int[] defaultFallbacks = { 5555, 5556, 5557, 5554, 5565 };
            foreach (int df in defaultFallbacks)
            {
                if (df != port && !fallbackPorts.Contains(df))
                {
                    fallbackPorts.Add(df);
                }
            }

            if (!string.IsNullOrWhiteSpace(preferredSerial) && TrySelectExistingDevice(preferredSerial.Trim(), out activeDevice))
            {
                _host = activeHost;
                _port = activePort;
                _deviceAddress = activeAddress;
                _device = activeDevice;
                Console.WriteLine("[ADB] phase=connect status=success details=\"preferred_device_selected\"");
                return;
            }

            // 1. Ưu tiên đúng địa chỉ cổng cấu hình cụ thể để tránh điều khiển nhầm giả lập khác đang mở.
            if (TryConnectAndSelectDevice(activeHost, activePort, out activeDevice))
            {
                _host = activeHost;
                _port = activePort;
                _deviceAddress = activeDevice.Serial;
                _device = activeDevice;
                Console.WriteLine("[ADB] phase=connect status=success details=\"device_connected\"");
                return;
            }

            // 2. Thử kết nối dự phòng sang các cổng phổ biến khác của BlueStacks, MEmu, LDPlayer, v.v.
            foreach (int p in fallbackPorts)
            {
                if (TryConnectAndSelectDevice(activeHost, p, out activeDevice))
                {
                    activePort = p;
                    _host = activeHost;
                    _port = activePort;
                    _deviceAddress = $"{activeHost}:{p}";
                    _device = activeDevice;
                    Console.WriteLine("[ADB] phase=connect status=success details=\"device_connected_fallback\"");
                    return;
                }
            }

            // 3. Cuối cùng mới lấy thiết bị đã được kích hoạt sẵn đầu tiên trong ADB Server (nếu có).
            try
            {
                var connectedDevices = AdbClient.Instance.GetDevices();
                if (connectedDevices != null && connectedDevices.Count > 0)
                {
                    activeDevice = connectedDevices[0];
                    activeAddress = activeDevice.Serial;
                    if (TryParseEndpointSerial(activeAddress, out string parsedHost, out int parsedPort))
                    {
                        activeHost = parsedHost;
                        activePort = parsedPort;
                    }
                    _host = activeHost;
                    _port = activePort;
                    _deviceAddress = activeAddress;
                    _device = activeDevice;
                    Console.WriteLine("[ADB] phase=connect status=success details=\"active_device_detected\"");
                    return;
                }
            }
            catch (Exception)
            {
                Console.WriteLine("[ADB WARNING] phase=connect status=pending action=get_devices reason=\"read_failed\"");
            }

            // Mặc định tạo dữ liệu thiết bị trống nếu hoàn toàn không kết nối được (để tránh NullReference)
            activeDevice = new DeviceData { Serial = activeAddress };
            _host = activeHost;
            _port = activePort;
            _deviceAddress = activeAddress;
            _device = activeDevice;
            Console.WriteLine("[ADB WARNING] phase=connect status=pending reason=\"no_device_detected\"");
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

        private bool TrySelectExistingDevice(string serial, out DeviceData device)
        {
            try
            {
                foreach (var connectedDevice in AdbClient.Instance.GetDevices())
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
                // Ignore and fall back to host/port probing.
            }

            device = new DeviceData { Serial = serial };
            return false;
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
                    if (IPAddress.TryParse(_host, out IPAddress? ipAddress))
                    {
                        AdbClient.Instance.Connect(new IPEndPoint(ipAddress, _port));
                    }
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
            if (BeforeInputAction?.Invoke() == true)
            {
                return;
            }

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

            if (BeforeInputAction?.Invoke() == true)
            {
                return;
            }

            // Gộp tất cả các lệnh tap lại và gửi đi trong 1 phiên làm việc
            ExecuteShell(string.Join("; ", commands));
        }

        /// <summary>
        /// Rải tap nhanh theo từng batch nhỏ để giảm miss input khi game hoặc giả lập phản hồi chậm.
        /// </summary>
        public void TapSequenceSafeFast(IEnumerable<Point> points, int batchSize = 4, int batchDelayMs = 90)
        {
            TapSequenceSafeFast(points, batchSize, batchDelayMs, CancellationToken.None);
        }

        public void TapSequenceSafeFast(IEnumerable<Point> points, int batchSize, int batchDelayMs, CancellationToken token)
        {
            var batch = new List<Point>(Math.Max(1, batchSize));
            foreach (Point point in points)
            {
                if (token.IsCancellationRequested)
                {
                    return;
                }

                batch.Add(point);
                if (batch.Count >= batchSize)
                {
                    TapSequence(batch);
                    batch.Clear();
                    if (token.WaitHandle.WaitOne(batchDelayMs))
                    {
                        return;
                    }
                }
            }

            if (batch.Count > 0 && !token.IsCancellationRequested)
            {
                TapSequence(batch);
            }
        }

        /// <summary>
        /// Thực hiện thao tác vuốt (Swipe) từ tọa độ nguồn đến tọa độ đích với thời gian thực hiện chỉ định.
        /// </summary>
        public void Swipe(int x1, int y1, int x2, int y2, int durationMs = 300)
        {
            if (BeforeInputAction?.Invoke() == true)
            {
                return;
            }

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
            if (BeforeInputAction?.Invoke() == true)
            {
                return false;
            }

            // 1. Thử dùng cơ chế UIAutomator2 pinchIn
            if (TryUiAutomatorPinchIn(count, percent: 100, steps: 20, intervalMs))
            {
                return true;
            }

            Console.WriteLine("[ADB WARNING] phase=pinch status=retry action=pinch reason=\"pinch_unsupported\" details=\"swipe_fallback\"");
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
            // Thiết lập chuyển tiếp cổng mạng local 9008 tới cổng 9008 trên giả lập Android
            RunAdb($"-s {_deviceAddress} forward tcp:9008 tcp:9008", waitForExit: true);
            if (PingUiAutomator2Server())
            {
                return true;
            }

            string? jarPath = FindUiAutomatorJar();
            if (jarPath == null)
            {
                Console.WriteLine("[ADB WARNING] phase=uia2 status=fail action=find_package reason=\"not_found\"");
                return false;
            }

            // Cache file jar ngoài thư mục cài đặt để chạy được khi app nằm trong Program Files.
            string appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            string destJar = Path.Combine(appData, "SimpliMixi", "adb", "u2.jar");
            if (!File.Exists(destJar))
            {
                try
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(destJar)!);
                    File.Copy(jarPath, destJar, overwrite: true);
                    Console.WriteLine("[ADB] phase=uia2 status=pending action=cache_package");
                }
                catch { }
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
                // AOT-safe: JsonNode (DOM) avoids reflection-based JsonSerializer.Serialize
                // that breaks under Native AOT trimming.
                var payload = new JsonObject
                {
                    ["jsonrpc"] = "2.0",
                    ["id"] = 1,
                    ["method"] = method,
                    ["params"] = JsonArrayFromParameters(parameters)
                };

                string json = payload.ToJsonString();
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
                    Console.WriteLine($"[ADB WARNING] phase=uia2 status=fail action=rpc code={(int)response.StatusCode} details=\"{body}\"");
                    return false;
                }

                using JsonDocument doc = JsonDocument.Parse(body);
                if (doc.RootElement.TryGetProperty("error", out JsonElement error))
                {
                    Console.WriteLine($"[ADB WARNING] phase=uia2 status=fail action=rpc reason=\"error\" details=\"{error}\"");
                    return false;
                }

                return doc.RootElement.TryGetProperty("result", out _);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ADB WARNING] phase=uia2 status=fail action=rpc reason=\"exception\" details=\"{ex.Message}\"");
                return false;
            }
        }

        /// <summary>
        /// Converts a heterogeneous object[] (strings, ints, nested dictionaries, arrays)
        /// into an AOT-safe JsonArray without reflection-based serialization.
        /// </summary>
        private static JsonArray JsonArrayFromParameters(object[] parameters)
        {
            var array = new JsonArray();
            foreach (object? p in parameters)
            {
                array.Add(ToJsonNode(p));
            }
            return array;
        }

        private static JsonNode? ToJsonNode(object? value)
        {
            switch (value)
            {
                case null:
                    return null;
                case JsonNode node:
                    return node.DeepClone();
                case string s:
                    return JsonValue.Create(s);
                case int i:
                    return JsonValue.Create(i);
                case long l:
                    return JsonValue.Create(l);
                case double d:
                    return JsonValue.Create(d);
                case bool b:
                    return JsonValue.Create(b);
                case IDictionary<string, object> dict:
                    {
                        var obj = new JsonObject();
                        foreach (KeyValuePair<string, object> kv in dict)
                        {
                            obj[kv.Key] = ToJsonNode(kv.Value);
                        }
                        return obj;
                    }
                case IEnumerable<object> seq:
                    {
                        var arr = new JsonArray();
                        foreach (object? item in seq)
                        {
                            arr.Add(ToJsonNode(item));
                        }
                        return arr;
                    }
                default:
                    // Fallback for unexpected types; ToString is safe (no reflection emit).
                    return JsonValue.Create(value.ToString());
            }
        }

        /// <summary>
        /// Tìm tệp tin u2.jar của dịch vụ UIAutomator2 bằng cách tìm kiếm trong các thư mục Downloads thường thấy.
        /// </summary>
        private string? FindUiAutomatorJar()
        {
            string appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            string localJar = Path.Combine(appData, "SimpliMixi", "adb", "u2.jar");
            if (File.Exists(localJar)) return localJar;

            localJar = Path.Combine(AppContext.BaseDirectory, "adb", "u2.jar");
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
                Console.WriteLine($"[ADB WARNING] phase=command status=fail command=\"adb\" reason=\"{ex.Message}\"");
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
                        Console.WriteLine($"[ADB WARNING] phase=screenshot status=retry attempt={attempt}");
                        Thread.Sleep(1000);
                        continue;
                    }

                    byte[] imageBytes = ms.ToArray();
                    if (imageBytes.Length == 0)
                    {
                        Console.WriteLine($"[ADB WARNING] phase=screenshot status=retry reason=\"empty\" attempt={attempt}");
                        Thread.Sleep(1000);
                        continue;
                    }

                    // Giải mã bytes nhị phân PNG thành đối tượng Mat màu
                    using Mat decoded = Cv2.ImDecode(imageBytes, ImreadModes.Color);
                    if (decoded.Empty())
                    {
                        Console.WriteLine($"[ADB WARNING] phase=screenshot status=retry reason=\"decode_fail\" attempt={attempt}");
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
                        Console.WriteLine($"[ADB WARNING] phase=screenshot status=retry reason=\"blank\" attempt={attempt}");
                        Thread.Sleep(1000);
                        continue;
                    }

                    return decoded.Clone();
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[ADB ERROR] phase=screenshot status=fail reason=\"{ex.Message}\" attempt={attempt}");
                    Thread.Sleep(1000);
                }
            }

            return null;
        }

        private static System.Collections.Generic.List<int> GetBlueStacksPortsFromConfig()
        {
            var ports = new System.Collections.Generic.List<int>();
            try
            {
                string confPath = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                    @"BlueStacks_nxt\bluestacks.conf");
                if (File.Exists(confPath))
                {
                    foreach (var line in File.ReadLines(confPath))
                    {
                        if (line.Contains(".status.adb_port="))
                        {
                            int eqIdx = line.IndexOf('=');
                            if (eqIdx > 0)
                            {
                                string val = line.Substring(eqIdx + 1).Trim(' ', '"', '\'', ';');
                                if (int.TryParse(val, out int port))
                                {
                                    ports.Add(port);
                                }
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ADB WARNING] Failed to read BlueStacks ports from config: {ex.Message}");
            }
            return ports;
        }

        private static bool TryParseEndpointSerial(string serial, out string host, out int port)
        {
            host = "127.0.0.1";
            port = 5556;
            if (string.IsNullOrWhiteSpace(serial)) return false;

            int colonIndex = serial.LastIndexOf(':');
            if (colonIndex <= 0 || colonIndex >= serial.Length - 1) return false;
            if (!int.TryParse(serial[(colonIndex + 1)..], out int parsedPort)) return false;

            host = serial[..colonIndex];
            port = Math.Clamp(parsedPort, 1, 65535);
            return true;
        }
    }
}
