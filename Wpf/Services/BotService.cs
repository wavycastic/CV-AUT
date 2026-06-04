using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows.Threading;
using CvAut.WpfApp.Services.Logging;

namespace CvAut.WpfApp.Services
{
    public class BotService : IBotService
    {
        private const string ConfigPath = "CV-AUT-PY/test_config.json";
        private CVAutomationFramework? _framework;
        private TextWriter? _originalOut;
        private UiLogTextWriter? _uiWriter;
        private readonly DispatcherTimer _statsTimer;
        private DateTime? _runStartTime;
        private bool _isPaused;
        private int _currentVillage = 1;
        private string _statusText = "IDLE";

        // Properties
        public bool IsRunning => _framework != null;
        public bool IsPaused => _isPaused;
        public string StatusText => _statusText;

        public int CurrentVillage
        {
            get => _currentVillage;
            set
            {
                if (_currentVillage != value)
                {
                    _currentVillage = value;
                    OnStatusChanged();
                    RefreshStats();
                }
            }
        }

        // Stats
        public string UptimeText { get; private set; } = "00:00:00";
        public string MemoryUsageText { get; private set; } = "0.0 MB";
        public string SuccessRateText { get; private set; } = "100%";
        public int AttacksCount { get; private set; }
        public long GoldGained { get; private set; }
        public long ElixirGained { get; private set; }
        public long DarkElixirGained { get; private set; }
        public long AvgGoldPerHour { get; private set; }
        public long AvgElixirPerHour { get; private set; }
        public long AvgDarkElixirPerHour { get; private set; }
        public int Star0Count { get; private set; }
        public int Star1Count { get; private set; }
        public int Star2Count { get; private set; }
        public int Star3Count { get; private set; }

        public event Action<string>? LogReceived;
        public event Action? StatusChanged;
        public event Action? StatsUpdated;

        public BotService()
        {
            _statsTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(5)
            };
            _statsTimer.Tick += (s, e) =>
            {
                UpdateUptime();
                RefreshStats();
            };
            _statsTimer.Start();
            RefreshStats();
        }

        public void StartBot()
        {
            if (_framework != null) return;

            _originalOut = Console.Out;
            _uiWriter = new UiLogTextWriter(_originalOut, AppendLog, ShouldIgnoreLog, TranslateLogToEnglish);
            Console.SetOut(_uiWriter);

            try
            {
                _framework = new CVAutomationFramework(ConfigPath);
                _framework.Start();
                _runStartTime = DateTime.Now;
                _isPaused = false;
                _statusText = "RUNNING";
                OnStatusChanged();
                AppendLog($"[{DateTime.Now:HH:mm:ss}] [GUI] Bot started");
            }
            catch (Exception ex)
            {
                AppendLog($"[{DateTime.Now:HH:mm:ss}] [GUI ERROR] Failed to start framework: {ex.Message}");
                RestoreConsole();
                _framework = null;
                _statusText = "IDLE";
                OnStatusChanged();
            }
        }

        public void StopBot()
        {
            if (_framework == null) return;

            try
            {
                _framework.Stop();
                _runStartTime = null;
                UptimeText = "00:00:00";
                AppendLog($"[{DateTime.Now:HH:mm:ss}] [GUI] Stop requested");
            }
            catch (Exception ex)
            {
                AppendLog($"[{DateTime.Now:HH:mm:ss}] [GUI ERROR] Error stopping framework: {ex.Message}");
            }
            finally
            {
                _framework = null;
                _isPaused = false;
                _statusText = "IDLE";
                RestoreConsole();
                OnStatusChanged();
            }
        }

        public void TogglePause()
        {
            if (_framework == null) return;

            if (_isPaused)
            {
                _framework.Resume();
                _isPaused = false;
                _statusText = "RUNNING";
                AppendLog($"[{DateTime.Now:HH:mm:ss}] [GUI] Resume requested");
            }
            else
            {
                _framework.Pause();
                _isPaused = true;
                _statusText = "PAUSED";
                AppendLog($"[{DateTime.Now:HH:mm:ss}] [GUI] Pause requested");
            }
            OnStatusChanged();
        }

        public JsonObject LoadMainConfig()
        {
            return ReadJsonObject(ConfigPath);
        }

        public void SaveMainConfig(JsonObject root)
        {
            Directory.CreateDirectory("profiles");
            WriteJson(ConfigPath, root);
        }

        public JsonObject LoadProfile(int villageId)
        {
            return ReadJsonObject(ProfilePath(villageId));
        }

        public void SaveProfile(int villageId, JsonObject profile)
        {
            Directory.CreateDirectory("profiles");
            WriteJson(ProfilePath(villageId), profile);
        }

        private void AppendLog(string message)
        {
            // Invoke LogReceived on GUI thread if needed, or bubble it up
            LogReceived?.Invoke(message);
        }

        private void RestoreConsole()
        {
            if (_originalOut != null)
            {
                Console.SetOut(_originalOut);
                _originalOut = null;
            }
            _uiWriter?.Dispose();
            _uiWriter = null;
        }

        private void UpdateUptime()
        {
            if (_runStartTime != null)
            {
                UptimeText = (DateTime.Now - _runStartTime.Value).ToString(@"hh\:mm\:ss");
            }
            else
            {
                UptimeText = "00:00:00";
            }
        }

        private void RefreshStats()
        {
            try
            {
                // Dynamic memory usage
                try
                {
                    long memBytes = Process.GetCurrentProcess().WorkingSet64;
                    MemoryUsageText = (memBytes / 1024.0 / 1024.0).ToString("F1") + " MB";
                }
                catch
                {
                    MemoryUsageText = "0.0 MB";
                }

                string statsFile = Path.Combine("profiles", $"Stats_{_currentVillage}.json");
                JsonObject stats = ReadJsonObject(statsFile);

                int gold = GetInt(stats, "gold", 0);
                int elixir = GetInt(stats, "elixir", 0);
                int de = GetInt(stats, "de", 0);
                int attacks = GetInt(stats, "attacks", 0);

                GoldGained = gold;
                ElixirGained = elixir;
                DarkElixirGained = de;
                AttacksCount = attacks;

                // Success rate calculation from stars
                JsonObject starsObj = GetObject(stats, "stars");
                int s0 = GetInt(starsObj, "0", 0);
                int s1 = GetInt(starsObj, "1", 0);
                int s2 = GetInt(starsObj, "2", 0);
                int s3 = GetInt(starsObj, "3", 0);

                Star0Count = s0;
                Star1Count = s1;
                Star2Count = s2;
                Star3Count = s3;

                int totalAttacks = attacks;
                int successful = s1 + s2 + s3;
                if (totalAttacks > 0)
                {
                    double rate = (double)successful / totalAttacks * 100.0;
                    SuccessRateText = rate.ToString("F0") + "%";
                }
                else
                {
                    SuccessRateText = "100%";
                }

                // Average calculation
                long lastUpdateTs = GetInt(stats, "last_update_ts", 0);
                double hours = Math.Max(1.0 / 60.0, (DateTimeOffset.UtcNow.ToUnixTimeSeconds() - lastUpdateTs) / 3600.0);
                if (lastUpdateTs <= 0)
                {
                    hours = 1.0;
                }

                AvgGoldPerHour = (long)Math.Round(gold / hours);
                AvgElixirPerHour = (long)Math.Round(elixir / hours);
                AvgDarkElixirPerHour = (long)Math.Round(de / hours);

                StatsUpdated?.Invoke();
            }
            catch
            {
                // Ignore stats file read collisions
            }
        }

        private void OnStatusChanged()
        {
            StatusChanged?.Invoke();
        }

        // Configuration utility methods
        private static string ProfilePath(int village) => Path.Combine("profiles", $"Village_{village}.json");

        private static JsonObject ReadJsonObject(string path)
        {
            if (!File.Exists(path))
            {
                return new JsonObject();
            }

            try
            {
                string json = File.ReadAllText(path);
                var node = JsonNode.Parse(json);
                return node as JsonObject ?? new JsonObject();
            }
            catch
            {
                return new JsonObject();
            }
        }

        private static void WriteJson(string path, JsonObject obj)
        {
            try
            {
                var options = new JsonSerializerOptions { WriteIndented = true };
                string json = obj.ToJsonString(options);
                File.WriteAllText(path, json, Encoding.UTF8);
            }
            catch
            {
                // Fail silently
            }
        }

        private static int GetInt(JsonObject obj, string key, int defaultValue)
        {
            if (obj.TryGetPropertyValue(key, out var val) && val != null)
            {
                try { return val.GetValue<int>(); } catch { }
            }
            return defaultValue;
        }

        private static JsonObject GetObject(JsonObject obj, string key)
        {
            if (obj.TryGetPropertyValue(key, out var val) && val is JsonObject o)
            {
                return o;
            }
            return new JsonObject();
        }

        // CAPTURED TRANSLATIONS
        private bool ShouldIgnoreLog(string line)
        {
            if (string.IsNullOrWhiteSpace(line)) return true;

            string[] noiseKeywords = new[]
            {
                "[WINDOW CHECK]",
                "[TEMPLATE]",
                "[SPACE secondary]",
                "[DIAG]",
                "[DIAG COUNT]",
                "[DIAG COUNT SCAN]",
                "[ATTACK-CS DEBUG]",
                "match=army_space",
                "best match =",
                "[DEBUG]",
                "match score =",
                "[COUNT OCR]",
                "[TPL]",
                "Checking if we're on the home base screen",
                "Checking image at path",
                "Confidence:",
                "match score = ",
                "max match =",
                "deploy elapsed=",
                "spell elapsed=",
                "Heroes deploy elapsed=",
                "not found (",
                "Bỏ qua: Thẻ '",
                "Bỏ qua: Không có tọa độ thả lính",
                "Không kiểm tra quân còn lại: Thẻ '",
                "Không có điểm rải bổ sung",
                "Không đọc được số quân còn lại",
                "đã rải hết.",
                "confidence 0.",
                "Bỏ qua "
            };

            foreach (var keyword in noiseKeywords)
            {
                if (line.Contains(keyword, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        private string TranslateLogToEnglish(string line)
        {
            if (string.IsNullOrWhiteSpace(line)) return "";

            string timestamp = $"[{DateTime.Now:HH:mm:ss}]";
            var timeMatch = Regex.Match(line, @"^\[\d{2}:\d{2}:\d{2}\]\s*");
            if (timeMatch.Success)
            {
                timestamp = timeMatch.Value.Trim();
                line = line.Substring(timeMatch.Length);
            }

            string cleanLine = line.Trim();
            string tag = "[BOT]";

            if (cleanLine.StartsWith("[FSM-CS]", StringComparison.OrdinalIgnoreCase))
            {
                cleanLine = cleanLine.Substring(8).Trim();
                tag = "[BOT]";
            }
            else if (cleanLine.StartsWith("[ADB]", StringComparison.OrdinalIgnoreCase))
            {
                cleanLine = cleanLine.Substring(5).Trim();
                tag = "[ADB]";
            }
            else if (cleanLine.StartsWith("[ADB WARNING]", StringComparison.OrdinalIgnoreCase))
            {
                cleanLine = cleanLine.Substring(13).Trim();
                tag = "[ADB WARNING]";
            }
            else if (cleanLine.StartsWith("[ADB ERROR]", StringComparison.OrdinalIgnoreCase))
            {
                cleanLine = cleanLine.Substring(11).Trim();
                tag = "[ADB ERROR]";
            }
            else if (cleanLine.StartsWith("[ATTACK-CS]", StringComparison.OrdinalIgnoreCase))
            {
                cleanLine = cleanLine.Substring(11).Trim();
                tag = "[ATTACK]";
            }
            else if (cleanLine.StartsWith("[ATTACK-CS WARNING]", StringComparison.OrdinalIgnoreCase))
            {
                cleanLine = cleanLine.Substring(19).Trim();
                tag = "[ATTACK WARNING]";
            }
            else if (cleanLine.StartsWith("[ATTACK-CS ERROR]", StringComparison.OrdinalIgnoreCase))
            {
                cleanLine = cleanLine.Substring(17).Trim();
                tag = "[ATTACK ERROR]";
            }
            else if (cleanLine.StartsWith("[VISION]", StringComparison.OrdinalIgnoreCase))
            {
                cleanLine = cleanLine.Substring(8).Trim();
                tag = "[VISION]";
            }
            else if (cleanLine.StartsWith("[GUI]", StringComparison.OrdinalIgnoreCase))
            {
                cleanLine = cleanLine.Substring(5).Trim();
                tag = "[GUI]";
            }
            else if (cleanLine.StartsWith("[GUI ERROR]", StringComparison.OrdinalIgnoreCase))
            {
                cleanLine = cleanLine.Substring(11).Trim();
                tag = "[GUI ERROR]";
            }
            else if (cleanLine.StartsWith("[WALL]", StringComparison.OrdinalIgnoreCase))
            {
                cleanLine = cleanLine.Substring(6).Trim();
                tag = "[WALL]";
            }
            else if (cleanLine.StartsWith("[TRAIN]", StringComparison.OrdinalIgnoreCase))
            {
                cleanLine = cleanLine.Substring(7).Trim();
                tag = "[TRAIN]";
            }
            else if (cleanLine.StartsWith("[SCOUT-CS]", StringComparison.OrdinalIgnoreCase))
            {
                cleanLine = cleanLine.Substring(10).Trim();
                tag = "[MATCH]";
            }
            else if (cleanLine.StartsWith("[SCOUT-CS ERROR]", StringComparison.OrdinalIgnoreCase))
            {
                cleanLine = cleanLine.Substring(16).Trim();
                tag = "[MATCH ERROR]";
            }

            string translated = TranslateText(cleanLine, ref tag);
            if (string.IsNullOrWhiteSpace(translated)) return "";

            translated = translated
                .Replace("Vàng:", "Gold:", StringComparison.OrdinalIgnoreCase)
                .Replace("Dầu hồng:", "Elixir:", StringComparison.OrdinalIgnoreCase)
                .Replace("Dầu đen:", "Dark Elixir:", StringComparison.OrdinalIgnoreCase)
                .Replace("Vàng", "Gold", StringComparison.OrdinalIgnoreCase)
                .Replace("Dầu hồng", "Elixir", StringComparison.OrdinalIgnoreCase)
                .Replace("Dầu đen", "Dark Elixir", StringComparison.OrdinalIgnoreCase);

            return $"{timestamp} {tag} {translated}";
        }

        private string TranslateText(string text, ref string tag)
        {
            text = text.Trim();

            if (text.Contains("Bắt đầu Chu kỳ đơn", StringComparison.OrdinalIgnoreCase))
            {
                tag = "[BOT]";
                var m = Regex.Match(text, @"Làng_(\d+)");
                return m.Success ? $"--- Starting Cycle (Village {m.Groups[1].Value}) ---" : "--- Starting Cycle ---";
            }
            if (text.Contains("Kết thúc Chu kỳ đơn", StringComparison.OrdinalIgnoreCase))
            {
                tag = "[BOT]";
                var m = Regex.Match(text, @"Làng_(\d+)");
                return m.Success ? $"--- Finished Cycle (Village {m.Groups[1].Value}) ---" : "--- Finished Cycle ---";
            }
            if (text.Contains("TÌM TRẬN ĐẤU", StringComparison.OrdinalIgnoreCase))
            {
                tag = "[MATCH]";
                var goldMatch = Regex.Match(text, @"Vàng\s*>=?\s*([\d,]+)");
                var elixirMatch = Regex.Match(text, @"Dầu hồng\s*>=?\s*([\d,]+)");
                string gold = goldMatch.Success ? goldMatch.Groups[1].Value : "N/A";
                string elixir = elixirMatch.Success ? elixirMatch.Groups[1].Value : "N/A";
                return $"Scouting targets (Gold >= {gold} | Elixir >= {elixir})...";
            }
            if (text.Contains("Phân tích nhà đối thủ", StringComparison.OrdinalIgnoreCase))
            {
                tag = "[MATCH]";
                var m = Regex.Match(text, @"đối thủ\s*(\d+/\d+)");
                return m.Success ? $"Scouting opponent target {m.Groups[1].Value}..." : "Scouting opponent target...";
            }
            if (text.Contains("ĐÃ ĐẠT TIÊU CHÍ!", StringComparison.OrdinalIgnoreCase))
            {
                tag = "[MATCH]";
                var goldMatch = Regex.Match(text, @"Gold=([\d,]+)");
                var elixirMatch = Regex.Match(text, @"Elixir=([\d,]+)");
                string g = goldMatch.Success ? goldMatch.Groups[1].Value : "Target";
                string e = elixirMatch.Success ? elixirMatch.Groups[1].Value : "Target";
                return $"TARGET FOUND! Loot: Gold={g}, Elixir={e}";
            }
            if (text.Contains("Thực thi:", StringComparison.OrdinalIgnoreCase))
            {
                tag = "[ATTACK]";
                var stratMatch = Regex.Match(text, @"Thực thi:\s*([a-zA-Z0-9_]+)");
                var sideMatch = Regex.Match(text, @"Tấn công cánh:\s*([a-zA-Z0-9_]+)");
                string strat = stratMatch.Success ? stratMatch.Groups[1].Value : "Attack Strategy";
                string side = sideMatch.Success ? sideMatch.Groups[1].Value : "N/A";
                return $"Executing strategy: {strat} | Side: {side.ToUpper()}";
            }
            if (text.Contains("Đã phát hiện thẻ '", StringComparison.OrdinalIgnoreCase))
            {
                tag = "[ATTACK]";
                var m = Regex.Match(text, @"thẻ\s*'([^']+)'");
                return m.Success ? $"Card detected: '{m.Groups[1].Value}'" : "Card detected.";
            }
            if (text.Contains("Chọn thẻ '", StringComparison.OrdinalIgnoreCase))
            {
                tag = "[ATTACK]";
                var cardMatch = Regex.Match(text, @"thẻ\s*'([^']+)'");
                var tapsMatch = Regex.Match(text, @"\((\d+)\s*taps?\)");
                string card = cardMatch.Success ? cardMatch.Groups[1].Value : "unknown";
                string taps = tapsMatch.Success ? tapsMatch.Groups[1].Value : "some";
                return $"Deploying '{card}' ({taps} taps)";
            }
            if (text.Contains("Thu hoạch", StringComparison.OrdinalIgnoreCase))
            {
                tag = "[BOT]";
                var m = Regex.Match(text, @"Thu hoạch\s+([a-zA-Z0-9_]+)");
                if (m.Success)
                {
                    string name = m.Groups[1].Value.Replace("_", " ");
                    name = System.Globalization.CultureInfo.CurrentCulture.TextInfo.ToTitleCase(name);
                    return $"Harvested {name}.";
                }
                return "Harvesting resources...";
            }
            if (text.Contains("Luyện quân nhanh (Quick Train Slot", StringComparison.OrdinalIgnoreCase))
            {
                tag = "[TRAIN]";
                var m = Regex.Match(text, @"Slot\s*(\d+)");
                return $"Quick Train started (Slot {(m.Success ? m.Groups[1].Value : "1")})...";
            }
            if (text.Contains("Smart Train theo cấu hình", StringComparison.OrdinalIgnoreCase))
            {
                tag = "[TRAIN]";
                var m = Regex.Match(text, @"attack='([^']+)'");
                return $"Smart Train started (Strategy: {(m.Success ? m.Groups[1].Value : "Default")})...";
            }
            if (text.Contains("Wall Updater - nâng tường level", StringComparison.OrdinalIgnoreCase))
            {
                tag = "[WALL]";
                var m = Regex.Match(text, @"level\s*(\d+)");
                return $"Scanning wall upgrades (Target level: {(m.Success ? m.Groups[1].Value : "N/A")})...";
            }
            if (text.Contains("Đang thực hiện chuyển sang Làng_", StringComparison.OrdinalIgnoreCase))
            {
                tag = "[BOT]";
                var m = Regex.Match(text, @"Làng_(\d+)");
                return m.Success ? $"Switching to Village {m.Groups[1].Value}..." : "Switching Village...";
            }
            if (text.Contains("Hoàn tất thời gian chơi của Làng_", StringComparison.OrdinalIgnoreCase))
            {
                tag = "[BOT]";
                var m = Regex.Match(text, @"Làng_(\d+)");
                return m.Success ? $"Finished session for Village {m.Groups[1].Value}." : "Finished village session.";
            }
            if (text.Contains("Đang chụp màn hình giả lập để quét tài nguyên LÀNG CHÍNH...", StringComparison.OrdinalIgnoreCase))
            {
                tag = "[BOT]";
                return "Capturing screen for home base resources check...";
            }
            if (text.Contains("Đang chụp màn hình giả lập để quét tài nguyên...", StringComparison.OrdinalIgnoreCase))
            {
                tag = "[MATCH]";
                return "Capturing screen for scouting loot...";
            }
            if (text.Contains("Kết quả quét Làng chính -> Vàng:", StringComparison.OrdinalIgnoreCase))
            {
                tag = "[BOT]";
                return text.Substring(text.IndexOf("Vàng:", StringComparison.OrdinalIgnoreCase));
            }
            if (text.Contains("Kết quả quét -> Vàng:", StringComparison.OrdinalIgnoreCase))
            {
                tag = "[MATCH]";
                return text.Substring(text.IndexOf("Vàng:", StringComparison.OrdinalIgnoreCase));
            }
            if (text.Contains("Không thể chụp ảnh màn hình hoặc ảnh trống.", StringComparison.OrdinalIgnoreCase))
            {
                tag = "[ERROR]";
                return "Failed to capture screenshot or image is blank.";
            }

            var dict = new Dictionary<string, (string newText, string newTag)>(StringComparer.OrdinalIgnoreCase)
            {
                { "Phân hệ lõi Máy trạng thái đã khởi tạo thành công.", ("State machine initialized successfully.", "[BOT]") },
                { "Vòng lặp tự động hóa đã bắt đầu chạy ngầm...", ("Automation loop started in background.", "[BOT]") },
                { "Đã gửi lệnh dừng khẩn cấp Máy trạng thái.", ("Emergency stop command sent.", "[BOT]") },
                { "Đã tạm dừng luồng chạy bot.", ("Bot execution paused.", "[BOT]") },
                { "Tiếp tục chạy luồng bot.", ("Bot execution resumed.", "[BOT]") },
                { "Bước 1: Xác thực tiêu điểm Làng chính...", ("Verifying Home Base focus...", "[BOT]") },
                { "Bước 2: Kéo camera góc rộng (Zoom Out)...", ("Zooming out camera view...", "[BOT]") },
                { "Đang thực hiện thu nhỏ góc nhìn bản đồ (Zoom Out)...", ("Zooming out camera view...", "[BOT]") },
                { "Quay trở về làng chính...", ("Returning to Home Base...", "[BOT]") },
                { "Vòng lặp bất đồng bộ bắt đầu xử lý...", ("Asynchronous processing active.", "[BOT]") },
                { "Chế độ chạy đơn tài khoản (Single Account Mode).", ("Running in Single Account Mode.", "[BOT]") },
                { "Vòng lặp chạy ngầm đã dừng hoàn toàn.", ("Automation loop stopped completely.", "[BOT]") },
                { "Bước 5: Tự động thu hoạch tài nguyên tại các mỏ sản xuất...", ("Harvesting resources from collectors...", "[BOT]") },
                { "Still not on home base after Treasure handling. Sending one BACK.", ("Home base check failed. Sending BACK command.", "[BOT]") },
                { "Chờ 1.5s để trang trí/base overlay của đối thủ ẩn hết trước khi đánh...", ("Waiting for base overlay to settle...", "[MATCH]") },
                { "Đang triển khai kịch bản thả quân", ("Executing army deployment...", "[ATTACK]") },
                { "Đang triển khai quân tướng...", ("Deploying Heroes...", "[ATTACK]") },
                { "Đang kích hoạt kỹ năng đặc biệt của Tướng...", ("Activating Hero special abilities...", "[ATTACK]") },
                { "Chờ thả phép đóng băng (Freeze)...", ("Deploying Freeze spell...", "[ATTACK]") },
                { "Kịch bản cướp trận hoàn tất.", ("Attack sequence completed.", "[ATTACK]") },
                { "Gửi lệnh Zoom Out ngầm tới MEmu hoàn tất.", ("MEmu background zoom-out complete.", "[ADB]") },
                { "Gửi lệnh Zoom Out BlueStacks qua ADB hoàn tất.", ("BlueStacks ADB zoom-out complete.", "[ADB]") },
                { "Tự động dò tìm và kết nối thành công tới cổng dự phòng:", ("Auto-detected and connected to backup port:", "[ADB]") },
                { "Tự động phát hiện thiết bị đang hoạt động:", ("Auto-detected active device:", "[ADB]") },
                { "Không thể lấy danh sách thiết bị từ AdbClient.", ("Could not retrieve device list from AdbClient.", "[ADB WARNING]") },
                { "Không có thiết bị nào kết nối. Đã mặc định serial:", ("No connected devices found. Setting default serial:", "[ADB WARNING]") },
                { "UIAutomator2 pinch-in không chạy được. Thử fallback ADB swipe đồng thời...", ("UIAutomator2 pinch-in failed. Falling back to simultaneous ADB swipes...", "[ADB WARNING]") },
                { "Không tìm thấy u2.jar của UIAutomator2 trong repo hoặc thư mục Simplicity.", ("u2.jar not found in repository or Simplicity folder.", "[ADB WARNING]") },
                { "Đã kết nối đến thiết bị cấu hình:", ("Connected to configured device:", "[ADB]") },
                { "Đã cache u2.jar vào thư mục build:", ("Cached u2.jar to build directory:", "[ADB]") },
                { "Bot started", ("Bot started.", "[GUI]") },
                { "Stop requested", ("Stop requested.", "[GUI]") },
                { "Resume requested", ("Resume requested.", "[GUI]") },
                { "Pause requested", ("Pause requested.", "[GUI]") },
                { "Loaded Village_", ("Loaded profile for Village", "[GUI]") },
                { "Saved Village_", ("Saved profile for Village", "[GUI]") },
                { "Saved config and Village_", ("Saved configuration and profile for Village", "[GUI]") },
                { "Không thể khởi động server ADB:", ("Cannot start ADB server:", "[ADB ERROR]") }
            };

            foreach (var kvp in dict)
            {
                if (text.Contains(kvp.Key, StringComparison.OrdinalIgnoreCase))
                {
                    tag = kvp.Value.newTag;
                    return text.Replace(kvp.Key, kvp.Value.newText, StringComparison.OrdinalIgnoreCase);
                }
            }

            return text;
        }
    }
}
