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
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using CvAut.WpfApp.Services.Logging;

namespace CvAut.WpfApp.Services
{
    /// <summary>
    /// Lớp dịch vụ quản lý trạng thái hoạt động của bot, chuyển đổi/lưu cấu hình,
    /// định tuyến và lọc các bản ghi nhật ký (log) hiển thị lên giao diện WPF,
    /// đồng thời tính toán các thông số thống kê hiệu suất (Uptime, Tài nguyên thu được, Tỷ lệ thắng, v.v.).
    /// </summary>
    public class BotService : IBotService
    {
        // UserData giữ cấu hình người dùng ngoài Program Files để cài lại/mở lại không mất chỉnh sửa.
        private static readonly string UserDataDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "SimpliMixi");
        private static readonly string ConfigPath = Path.Combine(UserDataDirectory, "Config", "test_config.json");

        // Boundary tối giản để UI điều khiển backend mà không phụ thuộc implementation.
        private IAutomationRunner? _runner;

        // Lưu trữ luồng xuất Standard Output gốc của Console trước khi chuyển hướng
        private TextWriter? _originalOut;

        // Bộ ghi log tùy biến giúp định tuyến Console.WriteLine lên giao diện WPF
        private UiLogTextWriter? _uiWriter;

        // Bộ hẹn giờ DispatcherTimer định kỳ cập nhật thời gian chạy và thống kê lên UI
        private readonly DispatcherTimer _statsTimer;

        // Mốc thời gian bắt đầu chạy bot
        private DateTime? _runStartTime;

        private readonly Dictionary<int, StatsSnapshot> _sessionBaselines = new();
        private readonly List<BattleLootPoint> _sessionBattleHistory = new();
        private StatsSnapshot? _lastObservedSessionStats;

        // Trạng thái tạm dừng của bot
        private bool _isPaused;
        private bool _isStopping;
        private bool _hasSessionStats;

        // ID làng hiện tại đang được chọn điều khiển (mặc định là Làng 1)
        private int _currentVillage = 1;

        // Chuỗi văn bản trạng thái hiển thị trên giao diện (ví dụ: RUNNING, PAUSED, IDLE)
        private string _statusText = "IDLE";

        // Properties
        /// <summary>
        /// Xác định xem luồng xử lý bot có đang chạy hay không.
        /// </summary>
        public bool IsRunning => _runner != null || _isStopping;

        /// <summary>
        /// Xác định xem bot có đang ở trạng thái tạm dừng hay không.
        /// </summary>
        public bool IsPaused => _isPaused;

        /// <summary>
        /// Trạng thái hoạt động dưới dạng chuỗi văn bản của bot (IDLE, RUNNING, PAUSED).
        /// </summary>
        public string StatusText => _statusText;

        /// <summary>
        /// Làng hiện tại bot đang xử lý hoặc cấu hình hiển thị.
        /// Khi thay đổi sẽ tự động làm mới số liệu thống kê tương ứng.
        /// </summary>
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
        /// <summary>
        /// Thời gian hoạt động liên tục của bot dưới dạng HH:mm:ss.
        /// </summary>
        public string UptimeText { get; private set; } = "00:00:00";

        /// <summary>
        /// Dung lượng bộ nhớ RAM (Working Set) đang sử dụng bởi ứng dụng (MB).
        /// </summary>
        public string MemoryUsageText { get; private set; } = "0.0 MB";

        /// <summary>
        /// Tỷ lệ tấn công thành công (tấn công được từ 1 sao trở lên).
        /// </summary>
        public string SuccessRateText { get; private set; } = "100%";

        /// <summary>
        /// Tổng số trận tấn công đã thực hiện trong phiên hiện tại.
        /// </summary>
        public int AttacksCount { get; private set; }

        /// <summary>
        /// Tổng lượng Vàng (Gold) cướp được.
        /// </summary>
        public long GoldGained { get; private set; }

        /// <summary>
        /// Tổng lượng Dầu hồng (Elixir) cướp được.
        /// </summary>
        public long ElixirGained { get; private set; }

        /// <summary>
        /// Tổng lượng Dầu đen (Dark Elixir) cướp được.
        /// </summary>
        public long DarkElixirGained { get; private set; }

        /// <summary>
        /// Tốc độ cướp Vàng trung bình mỗi giờ.
        /// </summary>
        public long AvgGoldPerHour { get; private set; }

        /// <summary>
        /// Tốc độ cướp Dầu hồng trung bình mỗi giờ.
        /// </summary>
        public long AvgElixirPerHour { get; private set; }

        /// <summary>
        /// Tốc độ cướp Dầu đen trung bình mỗi giờ.
        /// </summary>
        public long AvgDarkElixirPerHour { get; private set; }

        /// <summary>
        /// Số trận kết thúc với 0 Sao (Thất bại).
        /// </summary>
        public int Star0Count { get; private set; }

        /// <summary>
        /// Số trận kết thúc với 1 Sao.
        /// </summary>
        public int Star1Count { get; private set; }

        /// <summary>
        /// Số trận kết thúc với 2 Sao.
        /// </summary>
        public int Star2Count { get; private set; }

        /// <summary>
        /// Số trận kết thúc với 3 Sao (Thắng tuyệt đối).
        /// </summary>
        public int Star3Count { get; private set; }

        public IReadOnlyList<BattleLootPoint> SessionBattleHistory => _sessionBattleHistory;

        /// <summary>
        /// Sự kiện xảy ra khi nhận được một dòng nhật ký mới (được định dạng lại).
        /// </summary>
        public event Action<string>? LogReceived;

        /// <summary>
        /// Sự kiện xảy ra khi trạng thái của bot thay đổi (Start/Stop/Pause).
        /// </summary>
        public event Action? StatusChanged;

        /// <summary>
        /// Sự kiện xảy ra khi các chỉ số thống kê hiệu suất được cập nhật.
        /// </summary>
        public event Action? StatsUpdated;

        /// <summary>
        /// Khởi tạo dịch vụ BotService, thiết lập timer chạy chu kỳ 1 giây để cập nhật Uptime và chỉ số thống kê.
        /// </summary>
        public BotService()
        {
            _statsTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(1)
            };
            _statsTimer.Tick += (s, e) =>
            {
                UpdateUptime();
                RefreshStats();
            };
            _statsTimer.Start();
            RefreshStats();
        }

        /// <summary>
        /// Bắt đầu chạy bot: chuyển hướng đầu ra Console, khởi tạo và kích hoạt backend automation runner.
        /// </summary>
        public void StartBot()
        {
            if (_runner != null || _isStopping) return;

            // Lưu trữ TextWriter Console gốc
            _originalOut = Console.Out;
            // Chuyển hướng Console qua bộ ghi nhận diện và lọc tiếng Anh
            _uiWriter = new UiLogTextWriter(_originalOut, AppendLog, ShouldIgnoreLog, TranslateLogToEnglish);
            Console.SetOut(_uiWriter);

            try
            {
                // Khởi tạo backend automation runner với đường dẫn cấu hình
                _sessionBaselines.Clear();
                _sessionBattleHistory.Clear();
                _lastObservedSessionStats = StatsSnapshot.Empty;
                _hasSessionStats = false;
                UptimeText = "00:00:00";
                CaptureSessionBaseline(_currentVillage);

                _runner = new AutomationRunner(ConfigPath);
                _runner.Start();

                _runStartTime = DateTime.Now;
                _isPaused = false;
                _statusText = "RUNNING";
                OnStatusChanged();
                AppendLog($"[{DateTime.Now:HH:mm:ss}] [GUI] Bot started");
            }
            catch (Exception ex)
            {
                AppendLog($"[{DateTime.Now:HH:mm:ss}] [GUI ERROR] Failed to start runner: {ex.Message}");
                RestoreConsole();
                _runner = null;
                _statusText = "IDLE";
                OnStatusChanged();
            }
        }

        /// <summary>
        /// Dừng bot: kết thúc luồng backend automation, phục hồi lại Console tiêu chuẩn và khôi phục các biến trạng thái.
        /// </summary>
        public void StopBot()
        {
            if (_runner == null || _isStopping) return;

            IAutomationRunner runner = _runner;
            _isStopping = true;
            _isPaused = false;
            _statusText = "STOPPING";
            UpdateUptime();
            AppendLog($"[{DateTime.Now:HH:mm:ss}] [GUI] Hard stop requested");
            OnStatusChanged();

            try
            {
                runner.Stop();
            }
            catch (Exception ex)
            {
                AppendLog($"[{DateTime.Now:HH:mm:ss}] [GUI ERROR] Error stopping runner: {ex.Message}");
            }

            Task.Run(async () =>
            {
                try
                {
                    await runner.Completion.ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    AppendLog($"[{DateTime.Now:HH:mm:ss}] [GUI ERROR] Worker stop failed: {ex.Message}");
                }

                Application.Current.Dispatcher.Invoke(() => CompleteStop(runner));
            });
        }

        private void CompleteStop(IAutomationRunner runner)
        {
            if (!ReferenceEquals(_runner, runner)) return;

            runner.Dispose();
            _runner = null;
            _isStopping = false;
            _isPaused = false;
            _statusText = "IDLE";
            UpdateUptime();
            _runStartTime = null;
            _hasSessionStats = _sessionBaselines.Count > 0;
            RestoreConsole();
            RefreshStats();
            OnStatusChanged();
            AppendLog($"[{DateTime.Now:HH:mm:ss}] [GUI] Bot stopped");
        }

        /// <summary>
        /// Chuyển đổi trạng thái Tạm dừng (Pause) hoặc Tiếp tục chạy (Resume) đối với bot.
        /// </summary>
        public void TogglePause()
        {
            if (_runner == null) return;

            if (_isPaused)
            {
                _runner.Resume();
                _isPaused = false;
                _statusText = "RUNNING";
                AppendLog($"[{DateTime.Now:HH:mm:ss}] [GUI] Resume requested");
            }
            else
            {
                _runner.Pause();
                _isPaused = true;
                _statusText = "PAUSED";
                AppendLog($"[{DateTime.Now:HH:mm:ss}] [GUI] Pause requested");
            }
            OnStatusChanged();
        }

        /// <summary>
        /// Tải nội dung tệp cấu hình kiểm thử chính của bot dưới dạng JsonObject.
        /// </summary>
        public JsonObject LoadMainConfig()
        {
            EnsureUserConfigSeeded();
            return ReadJsonObject(ConfigPath);
        }

        /// <summary>
        /// Lưu cấu hình chính của bot xuống tệp cấu hình kiểm thử.
        /// </summary>
        public void SaveMainConfig(JsonObject root)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(ConfigPath)!);
            WriteJson(ConfigPath, root);
        }

        /// <summary>
        /// Tải tệp cấu hình cấu hình riêng biệt của một làng cụ thể theo ID làng.
        /// </summary>
        public JsonObject LoadProfile(int villageId)
        {
            string path = ProfilePath(villageId);
            if (!File.Exists(path))
            {
                SaveProfile(villageId, CreateDefaultProfile());
            }

            return ReadJsonObject(path);
        }

        /// <summary>
        /// Lưu tệp cấu hình của một làng cụ thể xuống tệp tin tương ứng.
        /// </summary>
        public void SaveProfile(int villageId, JsonObject profile)
        {
            Directory.CreateDirectory(Path.Combine(UserDataDirectory, "profiles"));
            WriteJson(ProfilePath(villageId), profile);
        }

        /// <summary>
        /// Đẩy thông tin log ra ngoài cho các bên đăng ký sự kiện LogReceived (thường là để hiển thị trên UI RichTextBox).
        /// </summary>
        private void AppendLog(string message)
        {
            LogReceived?.Invoke(message);
        }

        /// <summary>
        /// Khôi phục lại Standard Output cho Console hệ thống và hủy bỏ bộ chuyển hướng log.
        /// </summary>
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

        /// <summary>
        /// Cập nhật chuỗi hiển thị thời gian hoạt động của bot kể từ lúc bắt đầu chạy.
        /// </summary>
        private void UpdateUptime()
        {
            if (_runStartTime != null)
            {
                UptimeText = (DateTime.Now - _runStartTime.Value).ToString(@"hh\:mm\:ss");
            }
            else if (!_hasSessionStats)
            {
                UptimeText = "00:00:00";
            }
        }

        /// <summary>
        /// Đọc tệp thống kê của làng hiện tại (`profiles/Stats_{villageId}.json`),
        /// thực hiện tính toán tài nguyên thu được, tỷ lệ thắng (từ số Sao đạt được),
        /// tốc độ cướp trung bình mỗi giờ và dung lượng bộ nhớ RAM ứng dụng đang tiêu thụ.
        /// </summary>
        private void RefreshStats()
        {
            try
            {
                // Đo lường động dung lượng RAM đang chiếm dụng của tiến trình hiện tại
                try
                {
                    long memBytes = Process.GetCurrentProcess().WorkingSet64;
                    MemoryUsageText = (memBytes / 1024.0 / 1024.0).ToString("F1") + " MB";
                }
                catch
                {
                    MemoryUsageText = "0.0 MB";
                }

                StatsSnapshot current = ReadStatsSnapshot(_currentVillage);
                bool showSessionStats = IsRunning || _hasSessionStats;
                StatsSnapshot baseline = showSessionStats ? GetSessionBaseline(_currentVillage) : current;
                StatsSnapshot session = current.Subtract(baseline);
                TrackBattleHistory(session);

                GoldGained = session.Gold;
                ElixirGained = session.Elixir;
                DarkElixirGained = session.DarkElixir;
                AttacksCount = session.Attacks;

                Star0Count = session.Star0;
                Star1Count = session.Star1;
                Star2Count = session.Star2;
                Star3Count = session.Star3;

                int totalAttacks = session.Attacks;
                int successful = session.Star1 + session.Star2 + session.Star3;
                if (totalAttacks > 0)
                {
                    double rate = (double)successful / totalAttacks * 100.0;
                    SuccessRateText = rate.ToString("F0") + "%";
                }
                else
                {
                    SuccessRateText = "100%";
                }

                double hours = GetSessionHours();

                AvgGoldPerHour = (long)Math.Round(session.Gold / hours);
                AvgElixirPerHour = (long)Math.Round(session.Elixir / hours);
                AvgDarkElixirPerHour = (long)Math.Round(session.DarkElixir / hours);

                // Kích hoạt sự kiện thông báo cập nhật số liệu
                StatsUpdated?.Invoke();
            }
            catch
            {
                // Bỏ qua các lỗi xung đột truy cập tệp khi tệp đang bị ghi đồng thời bởi luồng FSM
            }
        }

        private void CaptureSessionBaseline(int village)
        {
            _sessionBaselines[village] = ReadStatsSnapshot(village);
            _lastObservedSessionStats = StatsSnapshot.Empty;
        }

        private void TrackBattleHistory(StatsSnapshot session)
        {
            if (!IsRunning && !_hasSessionStats) return;

            StatsSnapshot previous = _lastObservedSessionStats ?? StatsSnapshot.Empty;
            if (session.Attacks <= previous.Attacks)
            {
                _lastObservedSessionStats = session;
                return;
            }

            int attackDelta = session.Attacks - previous.Attacks;
            long goldDelta = Math.Max(0, session.Gold - previous.Gold);
            long elixirDelta = Math.Max(0, session.Elixir - previous.Elixir);
            long darkElixirDelta = Math.Max(0, session.DarkElixir - previous.DarkElixir);
            int stars = EstimateLatestStars(session, previous);

            if (attackDelta <= 1)
            {
                _sessionBattleHistory.Add(new BattleLootPoint(DateTime.Now, goldDelta, elixirDelta, darkElixirDelta, stars));
            }
            else
            {
                // If the UI missed multiple file updates, split the delta evenly so the chart stays truthful enough.
                for (int i = 0; i < attackDelta; i++)
                {
                    _sessionBattleHistory.Add(new BattleLootPoint(
                        DateTime.Now,
                        goldDelta / attackDelta,
                        elixirDelta / attackDelta,
                        darkElixirDelta / attackDelta,
                        stars));
                }
            }

            const int maxHistoryPoints = 30;
            if (_sessionBattleHistory.Count > maxHistoryPoints)
            {
                _sessionBattleHistory.RemoveRange(0, _sessionBattleHistory.Count - maxHistoryPoints);
            }

            _lastObservedSessionStats = session;
        }

        private static int EstimateLatestStars(StatsSnapshot current, StatsSnapshot previous)
        {
            if (current.Star3 > previous.Star3) return 3;
            if (current.Star2 > previous.Star2) return 2;
            if (current.Star1 > previous.Star1) return 1;
            if (current.Star0 > previous.Star0) return 0;
            return 0;
        }

        private StatsSnapshot GetSessionBaseline(int village)
        {
            if (!_sessionBaselines.TryGetValue(village, out StatsSnapshot? baseline))
            {
                baseline = ReadStatsSnapshot(village);
                _sessionBaselines[village] = baseline;
            }

            return baseline;
        }

        private StatsSnapshot ReadStatsSnapshot(int village)
        {
            string statsFile = StatsPath(village);
            JsonObject stats = ReadJsonObject(statsFile);
            JsonObject starsObj = GetObject(stats, "stars");

            return new StatsSnapshot(
                GetInt(stats, "gold", 0),
                GetInt(stats, "elixir", 0),
                GetInt(stats, "de", 0),
                GetInt(stats, "attacks", 0),
                GetInt(starsObj, "0", 0),
                GetInt(starsObj, "1", 0),
                GetInt(starsObj, "2", 0),
                GetInt(starsObj, "3", 0));
        }

        private double GetSessionHours()
        {
            if (_runStartTime == null)
            {
                return 1.0;
            }

            return Math.Max(1.0 / 60.0, (DateTime.Now - _runStartTime.Value).TotalHours);
        }

        /// <summary>
        /// Kích hoạt sự kiện thay đổi trạng thái hoạt động của bot.
        /// </summary>
        private void OnStatusChanged()
        {
            StatusChanged?.Invoke();
        }

        // Các phương thức tiện ích hỗ trợ thao tác tệp cấu hình JSON

        /// <summary>
        /// Trả về đường dẫn tệp cấu hình lưu trữ thông tin cấu hình cho một làng cụ thể.
        /// </summary>
        private static string ProfilePath(int village) => Path.Combine(UserDataDirectory, "profiles", $"Village_{village}.json");

        private static string StatsPath(int village) => Path.Combine(UserDataDirectory, "profiles", $"Stats_{village}.json");

        private static void EnsureUserConfigSeeded()
        {
            if (File.Exists(ConfigPath))
            {
                return;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(ConfigPath)!);
            WriteJson(ConfigPath, CreateDefaultMainConfig());
        }

        private static JsonObject CreateDefaultMainConfig() => new()
        {
            ["device_connection"] = new JsonObject { ["host"] = "127.0.0.1", ["port"] = 5556 },
            ["farming_thresholds"] = new JsonObject
            {
                ["gold_threshold"] = 650000,
                ["elixir_threshold"] = 650000,
                ["dark_elixir_threshold"] = 1000
            },
            ["upgrade_wall"] = false,
            ["wall_level"] = 14,
            ["wall_gold_threshold"] = 5000000,
            ["wall_elixir_threshold"] = 5000000,
            ["enable_stats"] = true,
            ["multi_account"] = new JsonObject
            {
                ["enable_multi_account"] = false,
                ["multi_interval_mins"] = 60,
                ["selected_villages"] = new JsonArray(1)
            }
        };

        private static JsonObject CreateDefaultProfile() => new()
        {
            ["gold_threshold"] = 650000,
            ["elixir_threshold"] = 650000,
            ["dark_elixir_threshold"] = 1000,
            ["upgrade_wall"] = false,
            ["wall_level"] = 14,
            ["wall_gold_threshold"] = 5000000,
            ["wall_elixir_threshold"] = 5000000,
            ["request_troops"] = false,
            ["enable_clan_games"] = false,
            ["enable_clan_capital"] = false,
            ["enable_stats"] = true,
            ["attack"] = "Dragon_Attack",
            ["train_mode"] = "smart",
            ["quick_slot"] = 1
        };

        /// <summary>
        /// Đọc nội dung tệp tin JSON và chuyển đổi thành một đối tượng JsonObject.
        /// </summary>
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

        /// <summary>
        /// Ghi nội dung của đối tượng JsonObject xuống tệp tin dưới dạng chuỗi JSON có canh lề thụt đầu dòng (indent).
        /// </summary>
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
                // Bỏ qua lỗi âm thầm khi không ghi được tệp tin
            }
        }

        /// <summary>
        /// Lấy giá trị số nguyên nguyên từ một khóa trong đối tượng JsonObject, trả về giá trị mặc định nếu thất bại hoặc không tồn tại.
        /// </summary>
        private static int GetInt(JsonObject obj, string key, int defaultValue)
        {
            if (obj.TryGetPropertyValue(key, out var val) && val != null)
            {
                try { return val.GetValue<int>(); } catch { }
            }
            return defaultValue;
        }

        /// <summary>
        /// Lấy một đối tượng JsonObject con nằm trong đối tượng cha bằng khóa tương ứng.
        /// </summary>
        private static JsonObject GetObject(JsonObject obj, string key)
        {
            if (obj.TryGetPropertyValue(key, out var val) && val is JsonObject o)
            {
                return o;
            }
            return new JsonObject();
        }

        private sealed record StatsSnapshot(
            long Gold,
            long Elixir,
            long DarkElixir,
            int Attacks,
            int Star0,
            int Star1,
            int Star2,
            int Star3)
        {
            public static StatsSnapshot Empty { get; } = new(0, 0, 0, 0, 0, 0, 0, 0);

            public StatsSnapshot Subtract(StatsSnapshot baseline)
            {
                return new StatsSnapshot(
                    Math.Max(0, Gold - baseline.Gold),
                    Math.Max(0, Elixir - baseline.Elixir),
                    Math.Max(0, DarkElixir - baseline.DarkElixir),
                    Math.Max(0, Attacks - baseline.Attacks),
                    Math.Max(0, Star0 - baseline.Star0),
                    Math.Max(0, Star1 - baseline.Star1),
                    Math.Max(0, Star2 - baseline.Star2),
                    Math.Max(0, Star3 - baseline.Star3));
            }
        }

        // CƠ CHẾ LỌC VÀ DỊCH NHẬT KÝ LOG

        /// <summary>
        /// Xác định xem một dòng nhật ký chi tiết từ nhân (Core) có nên được ẩn đi trên giao diện UI hay không.
        /// Giúp người dùng tránh bị ngợp bởi các log kỹ thuật tần suất cao như kiểm tra tọa độ, so khớp mẫu màu, kích thước ảnh.
        /// </summary>
        /// <param name="line">Dòng thông tin log đầu vào.</param>
        /// <returns>True nếu dòng log đó là log nhiễu/kỹ thuật cần bỏ qua; ngược lại là False.</returns>
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

        /// <summary>
        /// Dịch dòng nhật ký từ tiếng Việt sang tiếng Anh và chuẩn hóa thẻ phân loại (Tag) trước khi hiển thị lên giao diện UI.
        /// </summary>
        /// <param name="line">Dòng log đầu vào gốc từ phía Core Engine.</param>
        /// <returns>Dòng log đã được chuyển ngữ kèm nhãn phân loại và mốc thời gian.</returns>
        private string TranslateLogToEnglish(string line)
        {
            if (string.IsNullOrWhiteSpace(line)) return "";

            // Trích xuất hoặc khởi tạo mốc thời gian của log
            string timestamp = $"[{DateTime.Now:HH:mm:ss}]";
            var timeMatch = Regex.Match(line, @"^\[\d{2}:\d{2}:\d{2}\]\s*");
            if (timeMatch.Success)
            {
                timestamp = timeMatch.Value.Trim();
                line = line.Substring(timeMatch.Length);
            }

            string cleanLine = line.Trim();
            string tag = "[BOT]"; // Thẻ phân loại mặc định

            // Phân loại nguồn sinh log dựa vào các thẻ đánh dấu đặc trưng
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

            // Gọi hàm dịch nội dung văn bản cụ thể
            string translated = TranslateText(cleanLine, ref tag);
            if (string.IsNullOrWhiteSpace(translated)) return "";

            // Việt-Anh hóa một số chuỗi tài nguyên chung có thể còn sót lại
            translated = translated
                .Replace("Vàng:", "Gold:", StringComparison.OrdinalIgnoreCase)
                .Replace("Dầu hồng:", "Elixir:", StringComparison.OrdinalIgnoreCase)
                .Replace("Dầu đen:", "Dark Elixir:", StringComparison.OrdinalIgnoreCase)
                .Replace("Vàng", "Gold", StringComparison.OrdinalIgnoreCase)
                .Replace("Dầu hồng", "Elixir", StringComparison.OrdinalIgnoreCase)
                .Replace("Dầu đen", "Dark Elixir", StringComparison.OrdinalIgnoreCase);

            return $"{timestamp} {tag} {translated}";
        }

        /// <summary>
        /// Thực hiện dịch các từ khóa và ngữ cảnh tiếng Việt quen thuộc từ lõi sang chuỗi tương ứng trong tiếng Anh,
        /// sử dụng biểu thức chính quy (Regex) để trích xuất các tham số linh hoạt như lượng tài nguyên, tên thẻ, số lượt bấm.
        /// </summary>
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

            // Từ điển khớp chuỗi tĩnh để dịch nhanh các thông báo cố định
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
