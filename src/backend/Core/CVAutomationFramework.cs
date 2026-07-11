using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using OpenCvSharp;
using Point = OpenCvSharp.Point;
using Size = OpenCvSharp.Size;

namespace CvAut
{
    /// <summary>
    /// Máy trạng thái tự động hóa CV-AUT (CVAutomationFramework):
    /// - Quản lý trung tâm toàn bộ luồng tự động hóa Clash of Clans (FSM).
    /// - Chịu trách nhiệm thực thi Chu kỳ đơn (OneCycle): Xác thực Làng chính, Luyện quân, Thu hoạch mỏ, Nâng cấp tường.
    /// - Quản lý tiến trình Tìm trận (Farming Matchmaking): Quét nhà đối thủ, Ocr đọc loot, Ra quyết định đánh, Chạy script rải quân.
    /// - Xử lý các sự kiện ngắt quãng: Mất kết nối mạng (Connection Lost), Popups sự kiện quảng cáo (Treasure Hunt).
    /// - Hỗ trợ điều khiển Zoom Out ngầm giả lập MEmu qua PostMessage Win32 API hoặc giả lập BlueStacks qua ADB Pinch-In.
    /// - Hỗ trợ cơ chế chơi luân phiên nhiều tài khoản (Multi-Account) từ cấu hình.
    /// </summary>
    internal class CVAutomationFramework : IAutomationRunner
    {
        private readonly ADBHelper _adb;
        private readonly VisionEngine _vision;
        private readonly Training _training;
        private readonly Attacks _attacks;
        private readonly WallUpdater _wallUpdater;
        private readonly string _templatesPath;

        private CancellationTokenSource? _cts;
        private Task? _workerTask;
        private readonly ManualResetEvent _pauseEvent = new(true);
        private volatile bool _isRunning;
        private int _cycleCount;
        private int _currentVillageIdx = 1;
        private volatile bool _fastAttackQueued; // Kích hoạt bỏ qua bước chuẩn bị nếu vừa đánh xong về thẳng Làng chính
        private volatile bool _disableDialogShapeFallback;
        private bool _disposed;
        private bool _handlingConnectionPopup;
        private DateTime _sessionStartedAt;
        private DateTime? _pauseStartedAt;
        private TimeSpan _pausedDuration = TimeSpan.Zero;
        private int _sessionBattlesCompleted;
        private string _activeAccountName = "unknown";
        private static bool s_loggedLegacyWallConfigMigration;

        private static readonly string WritableLogsDirectory = ResolveWritableLogsDirectory();

        // Các vùng ROI giao diện chuẩn 1600x900px phục vụ xác thực
        private static readonly Rect GameSettingHomeRoi = Rect.FromLTRB(1445, 499, 1599, 708);
        private static readonly Rect NextButtonRoi = Rect.FromLTRB(1291, 563, 1592, 721);
        private static readonly Rect ScoutUiRoi = Rect.FromLTRB(2, 612, 222, 724);
        private static readonly Rect BattleEndedRoi = Rect.FromLTRB(632, 222, 989, 841);
        private static readonly Rect ResultYouGotRoi = Rect.FromLTRB(720, 330, 910, 390);
        private static readonly Rect ResultContinueRoi = Rect.FromLTRB(590, 670, 1020, 860);
        private static readonly Rect ConnectionPopupRoi = Rect.FromLTRB(360, 180, 1240, 720);
        private static readonly Rect StarBonusPopupRoi = Rect.FromLTRB(430, 55, 1170, 145);

        // Vùng ROI cho sự kiện rương báu (Treasure Hunt) xuất hiện gây nghẽn màn hình
        private static readonly Rect TreasureHuntRoi = Rect.FromLTRB(940, 80, 1450, 830);
        private static readonly Rect TreasureHuntChestTemplateRoi = Rect.FromLTRB(105, 65, 210, 145);
        private static readonly Rect TreasureHuntTextTemplateRoi = Rect.FromLTRB(15, 210, 300, 275);
        private static readonly Point TreasureHuntOpenedChestTapPoint = new(800, 455);
        private static readonly Point TreasureHuntRewardContinueTapPoint = new(800, 750);
        private static readonly Point StarBonusOkayTapPoint = new(808, 766);

        // Ngưỡng tin cậy khớp mẫu
        private const double HomeTemplateThreshold = 0.70;
        private const double ConnectionPopupThreshold = 0.88;
        private const double LegacyConnectionPopupThreshold = 0.55;
        private const double ConnIconPopupThreshold = 0.94;
        private const double NextButtonThreshold = 0.35;
        private const double ScoutUiThreshold = 0.70;
        private const double TreasureHuntThreshold = 0.70;
        private const double TreasureHuntMarkerThreshold = 0.82;
        private const double ResultContinueThreshold = 0.38;
        private const double ResultYouGotThreshold = 0.55;
        private const double StarBonusPopupThreshold = 0.70;
        private const int ResultScreenStableMatches = 2;
        private const int MaxWaitBattleSeconds = 170; // Thời gian tối đa chờ trận đấu tự động kết thúc (hết giờ 3 phút)
        private const int NormalCycleDelayMs = 10000;
        private const int FastAttackCycleDelayMs = 500;

        // Tên các template popup lỗi mạng thường thấy cần giải tỏa
        private static readonly string[] ConnectionPopupTemplates =
        {
            @"ui\Another_device.png",
            @"ui\Connection_lost.png",
            @"ui\Client_error!.png",
            @"ui\rate_coc.png",
            @"ui\conn.png"
        };

        public JsonElement Config { get; private set; }

        /// <summary>
        /// Khởi tạo khung tự động hóa FSM với tệp cấu hình chỉ định.
        /// </summary>
        public CVAutomationFramework(string configPath = "Config/test_config.json")
        {
            LoadConfig(configPath);

            // Đọc kết nối cổng của giả lập cấu hình
            var devConfig = Config.GetProperty("device_connection");
            string host = devConfig.GetProperty("host").GetString() ?? "127.0.0.1";
            int port = devConfig.GetProperty("port").GetInt32();
            string? serial = devConfig.TryGetProperty("serial", out JsonElement serialElement) ? serialElement.GetString() : null;

            _adb = new ADBHelper(host, port, serial);

            _templatesPath = Path.Combine(AppContext.BaseDirectory, "assets", "Templates");
            _adb.BeforeInputAction = null;
            _vision = new VisionEngine(_templatesPath);
            _training = new Training(_adb, _templatesPath, _vision);
            _attacks = new Attacks(_adb, _vision, _templatesPath, ReadAttackDelayConfig(Config), ReadAttackCoordinateConfig(Config));
            _wallUpdater = new WallUpdater(_adb, _vision, _templatesPath);

            Console.WriteLine("[FSM-CS] phase=init status=success details=\"automation_core_initialized\"");
        }

        /// <summary>
        /// Đọc cấu hình JSON từ đĩa, nếu lỗi sẽ sử dụng cấu hình mặc định an toàn.
        /// </summary>
        private void LoadConfig(string path)
        {
            try
            {
                if (File.Exists(path))
                {
                    string json = File.ReadAllText(path);
                    using var doc = JsonDocument.Parse(json);
                    Config = doc.RootElement.Clone();
                    return;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[FSM-CS WARNING] phase=init status=fail action=load_config reason=\"{ex.Message}\" details=\"using_defaults\"");
            }

            // Gán cấu hình mặc định dự phòng
            string defaultJson = @"{
                ""device_connection"": {""host"": ""127.0.0.1"", ""port"": 5556},
                ""farming_thresholds"": {""gold_threshold"": 650000, ""elixir_threshold"": 650000, ""dark_elixir_threshold"": 1000, ""total_resource_threshold"": 1300000, ""target_logic"": ""total""},
                ""upgrade_wall"": false,
                ""wall_level"": 14,
                ""wall_gold_threshold"": 5000000,
                ""wall_elixir_threshold"": 5000000,
                ""wall_gold_reserve"": 100000,
                ""wall_elixir_reserve"": 0,
                ""enable_stats"": true,
                ""run_session"": {""play_mode"": ""main_village"", ""stop_after_battles_enabled"": false, ""stop_after_battles"": 0, ""stop_after_minutes_enabled"": false, ""stop_after_minutes"": 0},
                ""multi_account"": {""enable_multi_account"": false, ""multi_interval_mins"": 60, ""switch_after_battles_enabled"": false, ""switch_after_battles"": 0, ""switch_after_minutes_enabled"": true, ""switch_after_clan_points_enabled"": false, ""switch_after_clan_points"": 0, ""selected_villages"": [1], ""accounts"": [{""id"": ""acc_1"", ""name"": ""Account 1"", ""profileVillage"": 1, ""targetVillage"": ""main_village"", ""templatePath"": """", ""enabled"": true}]}
            }";
            using var defaultDoc = JsonDocument.Parse(defaultJson);
            Config = defaultDoc.RootElement.Clone();
        }

        /// <summary>
        /// Khởi chạy chu kỳ chạy bot ngầm.
        /// </summary>
        public void Start()
        {
            if (_isRunning) return;

            _isRunning = true;
            _fastAttackQueued = false;
            _sessionStartedAt = DateTime.Now;
            _pauseStartedAt = null;
            _pausedDuration = TimeSpan.Zero;
            _sessionBattlesCompleted = 0;
            _cts = new CancellationTokenSource();
            _pauseEvent.Set(); // Mở khoá luồng để bot bắt đầu chạy

            _workerTask = Task.Run(() => StartWorker(_cts.Token));
            Console.WriteLine("[FSM-CS] phase=worker status=start details=\"automation_started\"");
        }

        /// <summary>
        /// Thực hiện chuẩn bị giả lập và đi vào vòng lặp Bot chính.
        /// </summary>
        private void StartWorker(CancellationToken token)
        {
            try
            {
                var devConfig = Config.GetProperty("device_connection");
                string host = devConfig.GetProperty("host").GetString() ?? "127.0.0.1";
                int port = devConfig.GetProperty("port").GetInt32();
                string emulatorType = devConfig.TryGetProperty("emulator_type", out var typeProp) ? (typeProp.GetString() ?? "BlueStacks") : "BlueStacks";
                string emulatorPath = devConfig.TryGetProperty("emulator_path", out var pathProp) ? (pathProp.GetString() ?? string.Empty) : string.Empty;
                string emulatorInstance = devConfig.TryGetProperty("emulator_instance", out var instProp) ? (instProp.GetString() ?? string.Empty) : string.Empty;

                // Đảm bảo giả lập đã được bật và mở CoC
                if (!EmulatorBootstrapper.EnsureReady(_adb, host, port, emulatorType, emulatorPath, token, emulatorInstance))
                {
                    _isRunning = false;
                    return;
                }

                Console.WriteLine("[FSM-CS] phase=home_check status=start");
                BotLoop(token);
            }
            catch (OperationCanceledException)
            {
                Console.WriteLine("[FSM-CS] phase=worker status=cancelled");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[FSM-CS ERROR] phase=worker status=fail action=startup reason=\"{ex.Message}\"");
            }
            finally
            {
                _isRunning = false;
                _fastAttackQueued = false;
                Console.WriteLine("[FSM-CS] phase=worker status=stopped");
            }
        }

        /// <summary>
        /// Dừng bot và giải phóng Task.
        /// </summary>
        public void Stop()
        {
            _isRunning = false;
            _fastAttackQueued = false;
            _cts?.Cancel();
            _pauseEvent.Set();
            Console.WriteLine("[FSM-CS] phase=worker status=stop_requested");
        }

        public Task Completion => _workerTask ?? Task.CompletedTask;

        /// <summary>
        /// Tạm dừng tạm thời luồng chạy bot.
        /// </summary>
        public void Pause()
        {
            _pauseStartedAt = DateTime.Now;
            _pauseEvent.Reset();
            Console.WriteLine("[FSM-CS] phase=worker status=paused");
        }

        /// <summary>
        /// Tiếp tục luồng chạy bot đã tạm dừng.
        /// </summary>
        public void Resume()
        {
            if (_pauseStartedAt != null)
            {
                _pausedDuration += DateTime.Now - _pauseStartedAt.Value;
                _pauseStartedAt = null;
            }

            _pauseEvent.Set();
            Console.WriteLine("[FSM-CS] phase=worker status=resumed");
        }

        public void RunSingleCycleForTest(CancellationToken token)
        {
            RunCyclesForTest(1, token);
        }

        /// <summary>
        /// Phương thức hỗ trợ chạy thử nghiệm một số lượng chu kỳ cố định (dành cho chế độ offline mock/test).
        /// </summary>
        public void RunCyclesForTest(int cycleLimit, CancellationToken token)
        {
            bool wasRunning = _isRunning;
            _isRunning = true;
            _pauseEvent.Set();
            _currentVillageIdx = 1;
            _cycleCount = 0;

            try
            {
                for (int i = 1; i <= cycleLimit && !CheckStop(token); i++)
                {
                    Console.WriteLine($"[FSM-CS] phase=test_cycle status=pending cycle={i} max={cycleLimit}");
                    OneCycle(Config, token);
                    if (i < cycleLimit && !CheckStop(token))
                    {
                        InterruptibleSleep(_fastAttackQueued ? FastAttackCycleDelayMs : NormalCycleDelayMs, token);
                    }
                }
            }
            finally
            {
                _isRunning = wasRunning;
            }
        }

        private bool CheckStop(CancellationToken token)
        {
            return token.IsCancellationRequested || !_isRunning || CheckAutoStop();
        }

        private bool CheckAutoStop()
        {
            if (!_isRunning) return true;

            JsonElement session = GetObjectOrDefault(Config, "run_session");
            if (session.ValueKind != JsonValueKind.Object) return false;

            bool stopByBattles = GetBoolOrDefault(session, "stop_after_battles_enabled", false);
            int battleLimit = GetIntOrDefault(session, "stop_after_battles", 0);
            if (stopByBattles && battleLimit > 0 && _sessionBattlesCompleted >= battleLimit)
            {
                _isRunning = false;
                _cts?.Cancel();
                _pauseEvent.Set();
                Console.WriteLine($"[FSM-CS] phase=auto_stop status=triggered reason=battle_limit current={_sessionBattlesCompleted} limit={battleLimit}");
                return true;
            }

            bool stopByMinutes = GetBoolOrDefault(session, "stop_after_minutes_enabled", false);
            int minuteLimit = GetIntOrDefault(session, "stop_after_minutes", 0);
            if (stopByMinutes && minuteLimit > 0)
            {
                TimeSpan activeElapsed = DateTime.Now - _sessionStartedAt - _pausedDuration;
                if (_pauseStartedAt != null)
                {
                    activeElapsed -= DateTime.Now - _pauseStartedAt.Value;
                }

                if (activeElapsed.TotalMinutes >= minuteLimit)
                {
                    _isRunning = false;
                    _cts?.Cancel();
                    _pauseEvent.Set();
                    Console.WriteLine($"[FSM-CS] phase=auto_stop status=triggered reason=minute_limit elapsed_minutes={activeElapsed.TotalMinutes:F1} limit={minuteLimit}");
                    return true;
                }
            }

            return false;
        }

        private bool InterruptibleSleep(int milliseconds, CancellationToken token)
        {
            DateTime end = DateTime.Now.AddMilliseconds(milliseconds);
            while (DateTime.Now < end)
            {
                int waitMs = Math.Min(500, Math.Max(1, (int)(end - DateTime.Now).TotalMilliseconds));
                if (token.WaitHandle.WaitOne(waitMs) || !_isRunning)
                {
                    return true;
                }

                HandleBlockingConnectionPopup("[WARN] Connection popup during wait → recover");
            }

            return false;
        }

        /// <summary>
        /// Dừng luồng nếu giao diện người dùng yêu cầu Pause (tạm dừng).
        /// </summary>
        private void WaitIfPaused(CancellationToken token)
        {
            while (!_pauseEvent.WaitOne(100))
            {
                if (CheckStop(token)) break;
            }
        }

        /// <summary>
        /// Thực thi 1 chu kỳ máy trạng thái đầy đủ (One Cycle) của một tài khoản:
        /// 1. Xác thực giao diện Làng chính (Home Base). Nếu kẹt, thực hiện khôi phục (Boot Recovery).
        /// 2. Bấm giải tỏa quảng cáo, kiểm tra lỗi kết nối mạng.
        /// 3. Zoom Out rộng bản đồ để quan sát.
        /// 4. Huấn luyện quân lính (Smart Train / Quick Train) tùy cài đặt.
        /// 5. Nâng cấp tường (Wall Update) bằng lượng tài nguyên dư thừa.
        /// 6. Thu hoạch tài nguyên mỏ.
        /// 7. Bấm tìm trận đánh cướp (Farming Matchmaking loop):
        ///    - Quét chỉ số tài nguyên đối thủ.
        ///    - So sánh với cấu hình mục tiêu.
        ///    - Nếu đạt tiêu chuẩn: Triển khai rải quân đánh, chờ trận kết thúc, lưu số liệu thống kê (Stats), bấm quay về Làng chính.
        ///    - Nếu chưa đạt: Bấm tiếp tục tìm kiếm nhà khác (Search Next).
        /// </summary>
        public void OneCycle(JsonElement cfg, CancellationToken token)
        {
            WaitIfPaused(token);
            if (CheckStop(token)) return;

            Console.WriteLine($"[FSM-CS] phase=cycle status=start village={_currentVillageIdx}");
            bool fastAttackOnly = _fastAttackQueued;
            _fastAttackQueued = false;
            if (fastAttackOnly)
            {
                Console.WriteLine("[FSM-CS] phase=cycle status=pending mode=fast_attack");
            }

            MainVillageConfig mainConfig = GetMainVillageConfig(cfg, _currentVillageIdx);

            // 1. Xác thực màn hình Làng chính (Home Base) TRƯỚC. Khi mới vào game màn hình
            // còn đang tải (screenshot trắng) nên zoom sẽ vô tác dụng — phải chờ render xong.
            WaitIfPaused(token);
            if (CheckStop(token)) return;

            Console.WriteLine("[FSM-CS] phase=home_check status=start");
            bool isHome = EnsureHomeBase(fastAttackOnly ? 8 : 50);
            if (!isHome)
            {
                Console.WriteLine("[FSM-CS ERROR] phase=cycle status=skip reason=home_not_detected");
                return;
            }

            // 2. Kéo camera rộng sau khi Home Base đã render để thấy toàn bộ mỏ tài nguyên.
            WaitIfPaused(token);
            if (CheckStop(token)) return;

            Console.WriteLine("[FSM-CS] phase=cycle status=pending step=1 details=\"initial_zoomout\"");
            ZoomOut();

            if (mainConfig.AttackMode == AttackMode.DonateOnly)
            {
                RunDonateOnlyCycle(mainConfig, token);
                _cycleCount++;
                Console.WriteLine($"[FSM-CS] phase=cycle status=success village={_currentVillageIdx} mode=donate_only");
                return;
            }

            if (!fastAttackOnly)
            {
                // 2. Click giải tỏa các popup linh tinh cản trở ở tâm màn hình
                _adb.Tap(140, 606);
                if (InterruptibleSleep(1000, token)) return;

                if (RecoverIfConnectionPopup("[WARN] Connection lost → recovering"))
                {
                    return;
                }

                // 4. Huấn luyện lính theo cấu hình
                WaitIfPaused(token);
                if (CheckStop(token)) return;

                if (RecoverIfConnectionPopup("[WARN] Connection lost before training → recovering"))
                {
                    return;
                }

                var trainConfig = GetTrainingConfig(cfg, _currentVillageIdx);

                if (trainConfig.Mode.Equals("quick", StringComparison.OrdinalIgnoreCase) && _cycleCount % 5 == 0)
                {
                    Console.WriteLine($"[TRAIN] phase=quick_train slot={trainConfig.QuickSlot} status=start");
                    _training.QuickTrain(trainConfig.QuickSlot);
                }
                else if (!trainConfig.Mode.Equals("quick", StringComparison.OrdinalIgnoreCase) && _cycleCount % 3 == 0)
                {
                    Console.WriteLine($"[TRAIN] phase=smart_train strategy={trainConfig.AttackStrategy} status=start");
                    if (!_training.SmartTrain(cfg, trainConfig.AttackStrategy))
                    {
                        Console.WriteLine("[TRAIN] phase=smart_train status=skip reason=incomplete");
                        return;
                    }
                }

                if (RecoverIfConnectionPopup("[WARN] Connection lost → recovering"))
                {
                    return;
                }

                // 5. Thu hoạch tài nguyên mỏ
                WaitIfPaused(token);
                if (CheckStop(token)) return;
                Console.WriteLine("[FSM-CS] phase=cycle status=pending step=5 details=\"collecting_resources\"");
                if (CollectResourcesPlaceholder())
                {
                    return;
                }

                TryUseCakeIfConfigured(mainConfig, token);
                TryRequestTroopsIfConfigured(mainConfig, token);

                TryUpgradeWallsFromHome(cfg, token, "after_collect");
            }

            // 6. Tìm kiếm tài nguyên (Scouting loop)
            WaitIfPaused(token);
            if (CheckStop(token)) return;

            FarmingTargetConfig targetConfig = mainConfig.Target;

            Console.WriteLine($"[CONFIG-CS] phase=startup active_village={_currentVillageIdx} gold_req={targetConfig.GoldThreshold} elixir_req={targetConfig.ElixirThreshold} dark_elixir_req={targetConfig.DarkElixirThreshold} total_req={targetConfig.TotalResourceThreshold} target_logic={targetConfig.Logic}");
            Console.WriteLine($"[SCOUT-CS] phase=scout status=start village={_currentVillageIdx} gold_req={targetConfig.GoldThreshold} elixir_req={targetConfig.ElixirThreshold} dark_elixir_req={targetConfig.DarkElixirThreshold} total_req={targetConfig.TotalResourceThreshold} target_logic={targetConfig.Logic}");

            // Bấm nút Tấn công (Attack) để mở giao diện tìm kiếm
            SearchAttack();

            int searchCount = 1;
            int maxSearches = 50; // Thử tìm tối đa 50 nhà đối thủ khác nhau
            bool battleExecuted = false;

            while (searchCount <= maxSearches && !CheckStop(token))
            {
                WaitIfPaused(token);
                if (CheckStop(token)) break;

                Console.WriteLine($"[SCOUT-CS] phase=scout status=pending index={searchCount} max={maxSearches}");

                if (RecoverIfConnectionPopup("[WARN] Connection lost during evaluation → recovering"))
                {
                    return;
                }

                // Đợi cho đến khi giao diện tìm kiếm của đối thủ được tải xong (Scouting UI)
                if (!WaitForScoutScreen())
                {
                    Console.WriteLine("[SCOUT-CS WARNING] phase=scout status=pending action=recover reason=scouting_ui_not_detected");
                    BootRecovery();
                    return;
                }

                // Đảm bảo nút "Next" tìm kiếm nhà khác hiển thị đầy đủ
                bool nextButtonFound = false;
                for (int attempt = 1; attempt <= 2; attempt++)
                {
                    WaitIfPaused(token);
                    if (CheckStop(token)) break;

                    if (IsNextButtonPresent())
                    {
                        nextButtonFound = true;
                        break;
                    }

                    Console.WriteLine($"[SCOUT-CS WARNING] phase=scout status=retry action=next attempt={attempt} max=2 reason=next_button_unavailable");
                    if (InterruptibleSleep(500, token)) break;
                }

                if (!nextButtonFound)
                {
                    Console.WriteLine("[SCOUT-CS WARNING] phase=scout status=pending action=recover reason=next_button_unavailable");
                    BootRecovery();
                    Console.WriteLine("[FSM-CS] phase=recovery status=success");
                    return;
                }

                // Quét tài nguyên hiển thị ở góc trên bên trái của nhà đối thủ
                var resources = IsTarget.ExtractResources(_adb, _vision);

                // Kiểm tra xem lượng tài nguyên có đạt tiêu chí cấu hình hay không
                bool targetAccepted = ShouldAcceptTarget(resources, targetConfig, out string targetReason);
                if (targetAccepted)
                {
                    Console.WriteLine($"[SCOUT-CS] phase=scout status=success gold={resources.Gold} elixir={resources.Elixir} dark_elixir={resources.DarkElixir} total={resources.Gold + resources.Elixir} target_logic={targetConfig.Logic} reason={targetReason} details=\"target_accepted\"");
                    Console.WriteLine("[SCOUT-CS] phase=scout status=pending action=prepare_attack");
                    if (InterruptibleSleep(1500, token)) break;

                    // Chạy script tự động rải quân tấn công
                    string attackStrategy = GetAttackStrategy(cfg, _currentVillageIdx);
                    Console.WriteLine($"[ATTACK-CS] phase=select_strategy status=success village={_currentVillageIdx} strategy={attackStrategy}");
                    _attacks.Run(attackStrategy, token, mainConfig.UseEventTroops);
                    battleExecuted = true;

                    WaitIfPaused(token);
                    if (CheckStop(token)) break;

                    // Chờ trận đấu tự động kết thúc hoặc hết giờ
                    bool battleWaitOk = WaitBattleEnd(token, mainConfig.SmartSurrender);
                    if (!battleWaitOk)
                    {
                        return;
                    }

                    bool returnedHome = false;
                    _disableDialogShapeFallback = true;
                    try
                    {
                        // Quét số sao đạt được và lượng tài nguyên thực tế nhận về
                        int starsGot = GetStarsFromScreen();
                        var gained = GainResources(starsGot);
                        Console.WriteLine($"[FSM-CS] phase=battle_stats stars={starsGot} gold={gained.Gold} elixir={gained.Elixir} dark_elixir={gained.DarkElixir} status=success");

                        // Cập nhật số liệu thống kê phiên chơi
                        if (GetBoolOrDefault(cfg, "enable_stats", false))
                        {
                            UpdateStats(_currentVillageIdx, starsGot, gained);
                        }
                        else
                        {
                            Console.WriteLine("[FSM-CS] phase=battle_stats status=skip reason=stats_disabled");
                        }
                        _sessionBattlesCompleted++;

                        // Bấm nút quay trở về Làng chính
                        returnedHome = ReturnHome();
                        _fastAttackQueued = returnedHome;
                    }
                    finally
                    {
                        _disableDialogShapeFallback = false;
                    }

                    WaitIfPaused(token);
                    if (CheckStop(token)) break;

                    if (returnedHome)
                    {
                        TryUpgradeWallsFromHome(cfg, token, "post_battle");
                    }

                    CheckAutoStop();
                    break;
                }
                else
                {
                    Console.WriteLine($"[SCOUT-CS] phase=scout status=skip gold={resources.Gold} elixir={resources.Elixir} dark_elixir={resources.DarkElixir} total={resources.Gold + resources.Elixir} target_logic={targetConfig.Logic} reason={targetReason} details=\"target_skipped\"");
                    // Bấm tìm kiếm đối thủ tiếp theo
                    SearchNext();
                    searchCount++;
                }
            }

            // Đầu hàng nếu quá 50 lần tìm kiếm không đạt yêu cầu
            if (!battleExecuted && !CheckStop(token))
            {
                Console.WriteLine("[SCOUT-CS WARNING] phase=scout status=fail reason=search_limit_reached action=return_home");
                _adb.Tap(80, 780); // Click Surrender
                if (InterruptibleSleep(1000, token)) return;
                _adb.Tap(960, 560); // Confirm OK
                if (InterruptibleSleep(2000, token)) return;
                _adb.Tap(800, 780); // Click Home
                InterruptibleSleep(5000, token);
            }

            _cycleCount++;
            Console.WriteLine($"[FSM-CS] phase=cycle status=success village={_currentVillageIdx}");
        }

        private void TryUpgradeWallsFromHome(JsonElement cfg, CancellationToken token, string phase)
        {
            var wallConfig = GetWallUpgradeConfig(cfg, _currentVillageIdx);
            Console.WriteLine($"[WALL DECISION] phase={phase} cycle={_cycleCount} enabled={wallConfig.Enabled} home=true level={wallConfig.WallLevel} gold_start={wallConfig.GoldThreshold:N0} elixir_start={wallConfig.ElixirThreshold:N0} gold_reserve={wallConfig.GoldReserve:N0} elixir_reserve={wallConfig.ElixirReserve:N0} wall_batch_limit={wallConfig.BatchLimit} wall_debug_screenshots={wallConfig.DebugScreenshots} status=check");

            if (!wallConfig.Enabled)
            {
                Console.WriteLine($"[WALL RESULT] phase={phase} status=skip reason=disabled");
                return;
            }

            if (!EnsureHomeBase(maxWaitSeconds: 20))
            {
                Console.WriteLine($"[WALL RESULT] phase={phase} status=skip reason=home_not_confirmed");
                return;
            }

            int upgradedWalls = _wallUpdater.HandleHomeResources(
                wallConfig.WallLevel,
                wallConfig.GoldThreshold,
                wallConfig.ElixirThreshold,
                wallConfig.GoldReserve,
                wallConfig.ElixirReserve,
                wallConfig.BatchLimit,
                wallConfig.DebugScreenshots,
                _cycleCount,
                token);
            if (upgradedWalls > 0 && GetBoolOrDefault(cfg, "enable_stats", false))
            {
                UpdateWallStats(_currentVillageIdx, upgradedWalls);
            }
        }

        /// <summary>
        /// Vòng lặp chính xử lý luồng bot chạy vô hạn.
        /// Hỗ trợ luân chuyển chơi giữa nhiều tài khoản khác nhau định kỳ (Switch Account) nếu cấu hình multi-account bật.
        /// </summary>
        private void BotLoop(CancellationToken token)
        {
            Console.WriteLine("[FSM-CS] phase=worker_loop status=start");

            JsonElement multiConfig = GetObjectOrDefault(Config, "multi_account");
            bool enableMulti = GetBoolOrDefault(multiConfig, "enable_multi_account", false);

            if (!enableMulti)
            {
                Console.WriteLine("[FSM-CS] phase=worker_loop status=pending mode=single_account");
                _currentVillageIdx = 1;
                while (!CheckStop(token))
                {
                    OneCycle(Config, token);
                    if (CheckStop(token)) break;
                    // Nghỉ ngắt quãng giữa các chu kỳ. Nếu vừa đánh xong, delay ngắn hơn để đánh tiếp ngay
                    InterruptibleSleep(_fastAttackQueued ? FastAttackCycleDelayMs : NormalCycleDelayMs, token);
                }
                return;
            }

            // Chế độ chạy nhiều tài khoản (Multi Account)
            AccountConfig[] accounts = GetConfiguredAccounts(multiConfig);
            int intervalSecs = Math.Max(1, GetIntOrDefault(multiConfig, "multi_interval_mins", 60)) * 60;
            bool switchByMinutes = GetBoolOrDefault(multiConfig, "switch_after_minutes_enabled", true);
            bool switchByBattles = GetBoolOrDefault(multiConfig, "switch_after_battles_enabled", false);
            int battleLimit = GetIntOrDefault(multiConfig, "switch_after_battles", 0);
            bool switchByClanPoints = GetBoolOrDefault(multiConfig, "switch_after_clan_points_enabled", false);
            int clanPointLimit = GetIntOrDefault(multiConfig, "switch_after_clan_points", 0);

            while (!CheckStop(token))
            {
                foreach (AccountConfig account in accounts)
                {
                    WaitIfPaused(token);
                    if (CheckStop(token)) break;

                    int idx = account.ProfileVillage;
                    _currentVillageIdx = idx;
                    _fastAttackQueued = false;
                    Console.WriteLine($"[FSM-CS] phase=worker_loop status=pending action=switch_account target={idx} account=\"{account.Name}\"");

                    // Thực hiện thay đổi tài khoản tương ứng
                    if (!SwitchToAccount(account, token))
                    {
                        Console.WriteLine($"[FSM-CS WARNING] phase=account_switch status=fail target={idx} account=\"{account.Name}\" action=skip_account");
                        continue;
                    }
                    _wallUpdater.ResetSavedOffset();

                    DateTime slotStart = DateTime.Now;
                    int slotBattleStart = _sessionBattlesCompleted;
                    int slotClanPointStart = ReadClanGamesPoints(idx);
                    _cycleCount = 0;

                    // Chơi tài khoản này cho đến khi một điều kiện đổi account được kích hoạt.
                    string switchReason = "none";
                    while (!ShouldSwitchAccount(
                        slotStart,
                        slotBattleStart,
                        slotClanPointStart,
                        idx,
                        switchByMinutes,
                        intervalSecs,
                        switchByBattles,
                        battleLimit,
                        switchByClanPoints,
                        clanPointLimit,
                        out switchReason) && !CheckStop(token))
                    {
                        WaitIfPaused(token);
                        OneCycle(Config, token);
                        if (CheckStop(token)) break;
                        InterruptibleSleep(_fastAttackQueued ? FastAttackCycleDelayMs : 15000, token);
                    }

                    Console.WriteLine($"[FSM-CS] phase=worker_loop status=pending action=switch_account target={idx} outcome=next reason={switchReason}");
                }

                InterruptibleSleep(5000, token);
            }

            Console.WriteLine("[FSM-CS] phase=worker_loop status=stopped");
        }

        /// <summary>
        /// Đảm bảo giao diện hiện tại là Làng chính (Home Base) bằng cách dò tìm icon Settings hoặc nút Shop.
        /// Chờ đợi tối đa maxWaitSeconds, nếu không thấy sẽ thử chạy luồng BootRecovery.
        /// </summary>
        private bool EnsureHomeBase(int maxWaitSeconds = 50, bool allowBootRecovery = true)
        {
            Console.WriteLine("[FSM-CS] phase=home_check status=start");

            DateTime start = DateTime.Now;
            while ((DateTime.Now - start).TotalSeconds < maxWaitSeconds)
            {
                if (DetectHomeBase(out string reason))
                {
                    Console.WriteLine("[FSM-CS] phase=home_check status=success");
                    return true;
                }

                if (HandleBlockingConnectionPopup("[WARN] Connection popup while waiting home → reload"))
                {
                    start = DateTime.Now;
                    Console.WriteLine("[FSM-CS] phase=home_check status=pending action=restart_wait_after_reload");
                    continue;
                }

                Console.WriteLine("[FSM-CS] phase=home_check status=pending details=\"waiting\"");
                if (InterruptibleSleep(5000, _cts?.Token ?? CancellationToken.None)) return false;
            }

            if (!allowBootRecovery)
            {
                Console.WriteLine("[FSM-CS ERROR] phase=home_check status=fail reason=recovery_retry_failed");
                return false;
            }

            Console.WriteLine("[FSM-CS ERROR] phase=home_check status=fail action=reboot reason=detection_failed");
            BootRecovery();
            return EnsureHomeBase(maxWaitSeconds: 20, allowBootRecovery: false);
        }

        /// <summary>
        /// Dò tìm giao diện Làng chính trên ảnh chụp màn hình hiện tại.
        /// </summary>
        private bool DetectHomeBase(out string reason)
        {
            reason = "not found";
            using Mat? screenshot = _adb.TakeScreenshot();
            if (screenshot == null || screenshot.Empty())
            {
                Console.WriteLine("[FSM-CS WARNING] phase=home_check status=fail reason=screenshot_failed");
                return false;
            }

            // Thử dò tìm bánh răng cài đặt
            if (TryMatchTemplate(screenshot, @"ui\game_setting.png", GameSettingHomeRoi, HomeTemplateThreshold, out _, out double settingScore))
            {
                reason = $"game_setting score={settingScore:F3}";
                return true;
            }

            // Thử dò tìm biểu tượng nút Cửa hàng (Shop) ở góc dưới phải
            if (TryMatchTemplate(screenshot, @"ui\shop.png", null, HomeTemplateThreshold, out Point shopCenter, out double shopScore))
            {
                reason = $"shop template at ({shopCenter.X},{shopCenter.Y}) score={shopScore:F3}";
                return true;
            }


            return false;
        }

        /// <summary>
        /// Chạy quy trình Khôi phục cưỡng bức (Boot Recovery):
        /// - Gửi lệnh dừng khẩn cấp Clash of Clans: am force-stop.
        /// - Chạy khởi động lại game.
        /// - Đợi game tải xong và tắt các popup chào mừng bằng chạm giải tỏa.
        /// </summary>
        public void BootRecovery()
        {
            Console.WriteLine("[FSM-CS] phase=recovery status=start action=restart_app package=\"com.supercell.clashofclans\"");

            _adb.ExecuteShell("am force-stop com.supercell.clashofclans");
            _adb.ExecuteShell("monkey -p com.supercell.clashofclans -c android.intent.category.LAUNCHER 1");

            Console.WriteLine("[FSM-CS] phase=recovery status=pending action=wait_app_load");
            if (InterruptibleSleep(10000, _cts?.Token ?? CancellationToken.None)) return;

            Console.WriteLine("[FSM-CS] phase=recovery status=pending action=clear_popups");
            _adb.Tap(146, 487); // Chạm rìa bên trái màn hình để giải tỏa nhanh các hộp thoại sự kiện
            _wallUpdater.ResetSavedOffset();
        }

        /// <summary>
        /// Bấm chọn nút Tấn công ngoài Làng chính và chuẩn bị tìm trận.
        /// Đồng thời xử lý popup sự kiện cản trở nếu có (Treasure Hunt).
        /// </summary>
        private void SearchAttack()
        {
            CancellationToken token = _cts?.Token ?? CancellationToken.None;
            _adb.Tap(113, 797); // Nút Tấn công chính
            if (InterruptibleSleep(700, token)) return;
            HandleTreasureHuntIfPresent();
            if (CheckStop(token)) return;
            _adb.Tap(272, 659); // Chọn Tìm trận đối thủ (Find Match)
            if (InterruptibleSleep(700, token)) return;
            _adb.Tap(1445, 804); // Chấp nhận phí tìm trận ban đầu
        }

        private bool HandleTreasureHuntIfPresent(bool verboseNotFound = true)
        {
            using Mat? screenshot = _adb.TakeScreenshot();
            if (screenshot == null || screenshot.Empty())
            {
                Console.WriteLine("[FSM-CS WARNING] phase=treasure_hunt status=fail reason=screenshot_failed");
                return false;
            }

            return HandleTreasureHuntIfPresent(screenshot, verboseNotFound);
        }

        /// <summary>
        /// Giải quyết popup sự kiện săn rương kho báu (Treasure Hunt) xuất hiện cản trở thao tác bot.
        /// Bấm click liên tục để dọn giải tỏa rương báu.
        /// </summary>
        private bool HandleTreasureHuntIfPresent(Mat screenshot, bool verboseNotFound = true)
        {
            if (!TryFindTreasureHuntPopup(screenshot, out Point center, out double score))
            {
                if (verboseNotFound)
                {
                    Console.WriteLine("[FSM-CS] phase=treasure_hunt status=skip reason=popup_not_found");
                }

                return false;
            }

            Console.WriteLine("[FSM-CS] phase=treasure_hunt status=pending details=\"popup_detected\"");
            for (int i = 1; i <= 5; i++)
            {
                _adb.Tap(center.X, center.Y);
                Thread.Sleep(350);
            }

            Thread.Sleep(1200);
            return true;
        }

        /// <summary>
        /// Mở rương báu thu được trong game và bấm xác nhận liên tục để nhận thưởng.
        /// </summary>
        private bool HandleOpenedTreasureChest()
        {
            Console.WriteLine("[FSM-CS] phase=treasure_hunt status=pending action=handle_opened_chest");
            for (int i = 1; i <= 5; i++)
            {
                _adb.Tap(TreasureHuntOpenedChestTapPoint.X, TreasureHuntOpenedChestTapPoint.Y);
                Thread.Sleep(350);
            }

            Thread.Sleep(2000);
            if (!TapTreasureRewardContinue())
            {
                Console.WriteLine("[FSM-CS WARNING] phase=treasure_hunt status=pending action=continue reason=action_unavailable details=\"using_fallback\"");
                _adb.Tap(TreasureHuntRewardContinueTapPoint.X, TreasureHuntRewardContinueTapPoint.Y);
                Thread.Sleep(1500);
            }

            return true;
        }

        /// <summary>
        /// Đợi nút Tiếp tục hiển thị và bấm để thoát màn hình nhận thưởng rương báu.
        /// </summary>
        private bool TapTreasureRewardContinue()
        {
            DateTime start = DateTime.Now;
            while ((DateTime.Now - start).TotalSeconds < 10)
            {
                using Mat? screenshot = _adb.TakeScreenshot();
                if (screenshot != null && !screenshot.Empty() && TryFindContinueButton(screenshot, out Point continueCenter, out double score))
                {
                    Console.WriteLine("[FSM-CS] phase=treasure_hunt status=pending action=continue details=\"action_detected\"");
                    _adb.Tap(continueCenter.X, continueCenter.Y);
                    Thread.Sleep(1500);
                    return true;
                }

                Thread.Sleep(500);
            }

            return false;
        }

        /// <summary>
        /// So khớp đa tỷ lệ tìm kiếm xem có sự xuất hiện của popup Treasure Hunt không.
        /// </summary>
        private bool TryFindTreasureHuntPopup(Mat screenshot, out Point center, out double score)
        {
            if (TryMatchTemplate(screenshot, @"ui\treasure_hunt.png", TreasureHuntRoi, TreasureHuntThreshold, out center, out score)
                || TryMatchTemplate(screenshot, @"event\treasure_hunt.png", TreasureHuntRoi, TreasureHuntThreshold, out center, out score))
            {
                return true;
            }

            double bestScore = score;
            Point bestCenter = center;

            if (TryMatchTemplateRegionMultiScale(
                    screenshot,
                    @"ui\treasure_hunt.png",
                    TreasureHuntRoi,
                    TreasureHuntChestTemplateRoi,
                    TreasureHuntMarkerThreshold,
                    out Point chestCenter,
                    out double chestScore))
            {
                center = chestCenter;
                score = chestScore;
                return true;
            }

            if (chestScore > bestScore)
            {
                bestScore = chestScore;
                bestCenter = chestCenter;
            }

            if (TryMatchTemplateRegionMultiScale(
                    screenshot,
                    @"ui\treasure_hunt.png",
                    TreasureHuntRoi,
                    TreasureHuntTextTemplateRoi,
                    TreasureHuntMarkerThreshold,
                    out Point textCenter,
                    out double textScore))
            {
                center = textCenter;
                score = textScore;
                return true;
            }

            if (textScore > bestScore)
            {
                bestScore = textScore;
                bestCenter = textCenter;
            }

            center = bestCenter;
            score = bestScore;
            return false;
        }

        /// <summary>
        /// Chạm nút Next trong giao diện tìm trận để đổi sang nhà đối thủ khác.
        /// </summary>
        private void SearchNext()
        {
            _adb.Tap(1432, 637);
        }

        /// <summary>
        /// Kiểm tra nút Next màu vàng có hiển thị ở góc dưới bên phải màn hình không.
        /// </summary>
        private bool IsNextButtonPresent()
        {
            using Mat? screenshot = _adb.TakeScreenshot();
            if (screenshot == null || screenshot.Empty())
            {
                Console.WriteLine("[SCOUT-CS WARNING] phase=scout status=fail reason=screenshot_failed");
                return false;
            }

            bool found = TryMatchTemplate(screenshot, @"ui\next_button.png", NextButtonRoi, NextButtonThreshold, out _, out double score);

            return found;
        }

        /// <summary>
        /// Đợi giao diện tìm trận (chứa thanh lính và nút đầu hàng dưới đáy) xuất hiện thành công.
        /// </summary>
        private bool WaitForScoutScreen(int timeoutSeconds = 12, int intervalMs = 500)
        {
            Console.WriteLine("[SCOUT-CS] phase=scout_wait status=start details=\"loading\"");

            DateTime start = DateTime.Now;
            while ((DateTime.Now - start).TotalSeconds < timeoutSeconds)
            {
                Thread.Sleep(350);

                using Mat? screenshot = _adb.TakeScreenshot();
                if (screenshot == null || screenshot.Empty())
                {
                    Console.WriteLine("[SCOUT-CS WARNING] phase=scout_wait status=fail reason=screenshot_failed");
                    Thread.Sleep(intervalMs);
                    continue;
                }

                // Dò biểu tượng thanh thả lính chiến trận
                if (TryMatchTemplate(screenshot, @"ui\end_battle.png", ScoutUiRoi, ScoutUiThreshold, out _, out _))
                {
                    Console.WriteLine("[SCOUT-CS] phase=scout_wait status=success details=\"ready\"");
                    return true;
                }

                // Giải tỏa nhanh popup sự kiện săn rương nếu vô tình xuất hiện cản màn hình
                if (HandleTreasureHuntIfPresent(screenshot, verboseNotFound: false))
                {
                    continue;
                }

                Thread.Sleep(intervalMs);
            }

            Console.WriteLine("[SCOUT-CS WARNING] phase=scout_wait status=fail reason=timeout");
            return false;
        }

        /// <summary>
        /// Kiểm tra sự xuất hiện của popup mất kết nối mạng. Nếu có, thực hiện khởi động lại game.
        /// </summary>
        private bool RecoverIfConnectionPopup(string warningMessage)
        {
            return HandleBlockingConnectionPopup(warningMessage);
        }

        private bool HandleBlockingConnectionPopup(string warningMessage)
        {
            if (_handlingConnectionPopup || !ConnectionPopupVisible(out string matchInfo))
            {
                return false;
            }

            _handlingConnectionPopup = true;
            try
            {
                string details = warningMessage.Replace("[WARN] ", "").Replace(" → ", "_").ToLower();
                Console.WriteLine($"[FSM-CS WARNING] phase=connection_check status=fail action=recover reason=\"connection_lost\" details=\"{details} ({matchInfo})\"");
                BootRecovery();
                return true;
            }
            finally
            {
                _handlingConnectionPopup = false;
            }
        }

        /// <summary>
        /// Kiểm tra xem có bất kỳ popup báo lỗi kết nối mạng nào đang cản màn hình không.
        /// </summary>
        private bool ConnectionPopupVisible(out string matchInfo, bool allowDialogShapeFallback = true)
        {
            matchInfo = "none";
            if (_disableDialogShapeFallback)
            {
                allowDialogShapeFallback = false;
            }

            using Mat? screenshot = _adb.TakeScreenshot();
            if (screenshot == null || screenshot.Empty())
            {
                return false;
            }

            foreach (string templateName in ConnectionPopupTemplates)
            {
                bool isLegacyConnectionTemplate = templateName.EndsWith("Client_error!.png", StringComparison.OrdinalIgnoreCase)
                    || templateName.EndsWith("Connection_lost.png", StringComparison.OrdinalIgnoreCase)
                    || templateName.EndsWith("Another_device.png", StringComparison.OrdinalIgnoreCase)
                    || templateName.EndsWith("rate_coc.png", StringComparison.OrdinalIgnoreCase);
                double threshold = templateName.EndsWith("conn.png", StringComparison.OrdinalIgnoreCase)
                    ? ConnIconPopupThreshold
                    : isLegacyConnectionTemplate ? LegacyConnectionPopupThreshold : ConnectionPopupThreshold;
                Rect? popupRoi = isLegacyConnectionTemplate ? null : ConnectionPopupRoi;

                bool matched = isLegacyConnectionTemplate
                    ? TryMatchTemplateMultiScale(screenshot, templateName, popupRoi, threshold, out Point center, out double score)
                    : TryMatchTemplate(screenshot, templateName, popupRoi, threshold, out center, out score);
                if (!matched)
                {
                    continue;
                }

                matchInfo = $"{templateName} score={score:F2} center=({center.X},{center.Y})";
                Console.WriteLine($"[FSM-CS WARNING] phase=connection_check status=fail reason=\"popup_detected\" template=\"{templateName}\"");
                return true;
            }

            if (allowDialogShapeFallback && TryDetectReloadDialogShape(screenshot, out Rect dialogRect))
            {
                matchInfo = $"reload_dialog_shape rect=({dialogRect.X},{dialogRect.Y},{dialogRect.Width},{dialogRect.Height})";
                Console.WriteLine("[FSM-CS WARNING] phase=connection_check status=fail reason=\"popup_detected\" template=\"reload_dialog_shape\"");
                return true;
            }

            return false;
        }

        private static bool TryDetectReloadDialogShape(Mat screenshot, out Rect dialogRect)
        {
            dialogRect = default;
            if (screenshot.Empty())
            {
                return false;
            }

            Rect roi = GetCenteredConnectionPopupRoi(screenshot.Width, screenshot.Height);
            using Mat crop = new Mat(screenshot, roi);
            using Mat hsv = new Mat();
            Cv2.CvtColor(crop, hsv, ColorConversionCodes.BGR2HSV);

            using Mat mask = new Mat();
            Cv2.InRange(hsv, new Scalar(0, 0, 45), new Scalar(179, 45, 105), mask);

            using Mat kernel = Cv2.GetStructuringElement(MorphShapes.Rect, new Size(15, 15));
            Cv2.MorphologyEx(mask, mask, MorphTypes.Close, kernel);
            Cv2.FindContours(mask, out Point[][] contours, out _, RetrievalModes.External, ContourApproximationModes.ApproxSimple);

            foreach (Point[] contour in contours)
            {
                Rect localRect = Cv2.BoundingRect(contour);
                double area = Cv2.ContourArea(contour);
                double fillRatio = area / Math.Max(1, localRect.Width * localRect.Height);

                double widthRatio = localRect.Width / (double)screenshot.Width;
                double heightRatio = localRect.Height / (double)screenshot.Height;

                if (widthRatio < 0.38 || widthRatio > 0.90
                    || heightRatio < 0.14 || heightRatio > 0.48
                    || fillRatio < 0.55)
                {
                    continue;
                }

                int centerX = roi.X + localRect.X + localRect.Width / 2;
                int centerY = roi.Y + localRect.Y + localRect.Height / 2;
                bool centered = centerX >= screenshot.Width * 0.20 && centerX <= screenshot.Width * 0.80
                    && centerY >= screenshot.Height * 0.25 && centerY <= screenshot.Height * 0.75;
                if (!centered)
                {
                    continue;
                }

                dialogRect = new Rect(roi.X + localRect.X, roi.Y + localRect.Y, localRect.Width, localRect.Height);
                return true;
            }

            return false;
        }

        private static Rect GetCenteredConnectionPopupRoi(int width, int height)
        {
            int x = (int)Math.Round(width * 0.08);
            int y = (int)Math.Round(height * 0.18);
            int roiWidth = (int)Math.Round(width * 0.84);
            int roiHeight = (int)Math.Round(height * 0.64);
            return ImageUtils.ClampRect(new Rect(x, y, roiWidth, roiHeight), width, height);
        }

        private bool TryMatchTemplateMultiScale(Mat source, string templateFileName, Rect? roi, double threshold, out Point center, out double score)
        {
            center = default;
            score = 0;

            if (source.Empty())
            {
                return false;
            }

            if (!TemplateAssetLoader.Exists(_templatesPath, templateFileName))
            {
                Console.WriteLine($"[VISION] phase=match_template status=fail reason=template_missing details=\"{templateFileName}\"");
                return false;
            }

            using Mat template = TemplateAssetLoader.Load(_templatesPath, templateFileName, ImreadModes.Grayscale);
            if (template.Empty())
            {
                Console.WriteLine($"[VISION] phase=match_template status=fail reason=template_unreadable details=\"{templateFileName}\"");
                return false;
            }

            Rect safeRoi = roi.HasValue ? ImageUtils.ClampRect(roi.Value, source.Width, source.Height) : new Rect(0, 0, source.Width, source.Height);
            using Mat crop = new Mat(source, safeRoi);
            using Mat gray = new Mat();
            Cv2.CvtColor(crop, gray, ColorConversionCodes.BGR2GRAY);

            double[] scales = { 0.45, 0.50, 0.55, 0.60, 0.65, 0.75, 0.90, 1.00 };
            foreach (double scale in scales)
            {
                int scaledWidth = Math.Max(1, (int)Math.Round(template.Width * scale));
                int scaledHeight = Math.Max(1, (int)Math.Round(template.Height * scale));
                if (scaledWidth > gray.Width || scaledHeight > gray.Height)
                {
                    continue;
                }

                using Mat scaledTemplate = new Mat();
                Cv2.Resize(template, scaledTemplate, new Size(scaledWidth, scaledHeight), 0, 0, InterpolationFlags.Linear);

                using Mat result = new Mat();
                Cv2.MatchTemplate(gray, scaledTemplate, result, TemplateMatchModes.CCoeffNormed);
                Cv2.MinMaxLoc(result, out _, out double currentScore, out _, out Point maxLoc);

                if (currentScore > score)
                {
                    score = currentScore;
                    center = new Point(
                        safeRoi.X + maxLoc.X + scaledTemplate.Width / 2,
                        safeRoi.Y + maxLoc.Y + scaledTemplate.Height / 2
                    );
                }
            }

            return score >= threshold;
        }

        /// <summary>
        /// Phương thức so khớp mẫu ảnh OpenCV thang xám cơ bản.
        /// </summary>
        private bool TryMatchTemplate(Mat source, string templateFileName, Rect? roi, double threshold, out Point center, out double score)
        {
            center = default;
            score = 0;

            if (source.Empty())
            {
                return false;
            }

            if (!TemplateAssetLoader.Exists(_templatesPath, templateFileName))
            {
                Console.WriteLine($"[VISION] phase=match_template status=fail reason=template_missing details=\"{templateFileName}\"");
                return false;
            }

            using Mat template = TemplateAssetLoader.Load(_templatesPath, templateFileName, ImreadModes.Grayscale);
            if (template.Empty())
            {
                Console.WriteLine($"[VISION] phase=match_template status=fail reason=template_unreadable details=\"{templateFileName}\"");
                return false;
            }

            Rect safeRoi = roi.HasValue ? ImageUtils.ClampRect(roi.Value, source.Width, source.Height) : new Rect(0, 0, source.Width, source.Height);
            if (safeRoi.Width < template.Width || safeRoi.Height < template.Height)
            {
                return false;
            }

            using Mat crop = new Mat(source, safeRoi);
            using Mat gray = new Mat();
            Cv2.CvtColor(crop, gray, ColorConversionCodes.BGR2GRAY);

            using Mat result = new Mat();
            Cv2.MatchTemplate(gray, template, result, TemplateMatchModes.CCoeffNormed);
            Cv2.MinMaxLoc(result, out _, out score, out _, out Point maxLoc);

            center = new Point(
                safeRoi.X + maxLoc.X + template.Width / 2,
                safeRoi.Y + maxLoc.Y + template.Height / 2
            );

            return score >= threshold;
        }

        private static AttackDelayConfig ReadAttackDelayConfig(JsonElement cfg)
        {
            JsonElement advanced = GetObjectOrDefault(cfg, "advanced_config");
            bool useDefault = GetBoolOrDefault(advanced, "use_default_config", true);
            JsonElement attackDelays = useDefault ? default : GetObjectOrDefault(advanced, "attack_delays");

            return new AttackDelayConfig
            {
                TroopDeployDelayMs = Clamp(GetIntOrDefault(attackDelays, "troop_deploy_delay_ms", 60), 20, 500),
                RageSpellDelayMs = Clamp(GetIntOrDefault(attackDelays, "rage_spell_delay_ms", 650), 100, 5000),
                FreezeSpellDelayMs = Clamp(GetIntOrDefault(attackDelays, "freeze_spell_delay_ms", 850), 100, 5000),
                GrandWardenAbilityDelayMs = Clamp(GetIntOrDefault(attackDelays, "grand_warden_ability_delay_ms", 2500), 500, 15000)
            };
        }

        private static AttackCoordinateConfig ReadAttackCoordinateConfig(JsonElement cfg)
        {
            JsonElement advanced = GetObjectOrDefault(cfg, "advanced_config");
            bool useDefault = GetBoolOrDefault(advanced, "use_default_config", true);
            JsonElement spellCoordinates = useDefault ? default : GetObjectOrDefault(advanced, "spell_coordinates");
            AttackCoordinateConfig coordinateConfig = new();

            foreach (string direction in new[] { "top_left", "top_right", "bottom_left", "bottom_right" })
            {
                JsonElement directionNode = GetObjectOrDefault(spellCoordinates, direction);
                SpellDeploymentGroups groups = new()
                {
                    RageInitial = ReadPointList(directionNode, "rage_initial"),
                    Freeze = ReadPointList(directionNode, "freeze"),
                    RageRemaining = ReadPointList(directionNode, "rage_remaining")
                };

                if (groups.RageInitial.Count > 0 || groups.Freeze.Count > 0 || groups.RageRemaining.Count > 0)
                {
                    coordinateConfig.SpellCoordinates[direction] = groups;
                }
            }

            return coordinateConfig;
        }

        private static List<Point> ReadPointList(JsonElement element, string propertyName)
        {
            if (element.ValueKind != JsonValueKind.Object
                || !element.TryGetProperty(propertyName, out JsonElement points)
                || points.ValueKind != JsonValueKind.Array)
            {
                return new List<Point>();
            }

            List<Point> result = new();
            foreach (JsonElement point in points.EnumerateArray())
            {
                if (TryReadPoint(point, out Point parsed))
                {
                    result.Add(parsed);
                }
            }

            return result;
        }

        private static bool TryReadPoint(JsonElement point, out Point parsed)
        {
            parsed = default;
            int x;
            int y;

            if (point.ValueKind == JsonValueKind.Object)
            {
                x = GetIntOrDefault(point, "x", -1);
                y = GetIntOrDefault(point, "y", -1);
            }
            else if (point.ValueKind == JsonValueKind.Array && point.GetArrayLength() >= 2)
            {
                JsonElement xNode = point[0];
                JsonElement yNode = point[1];
                if (!xNode.TryGetInt32(out x) || !yNode.TryGetInt32(out y))
                {
                    return false;
                }
            }
            else
            {
                return false;
            }

            if (x < 0 || y < 0)
            {
                return false;
            }

            parsed = new Point(Clamp(x, 0, 1599), Clamp(y, 0, 899));
            return true;
        }

        private static int Clamp(int value, int min, int max) => Math.Min(Math.Max(value, min), max);

        private static string GetStringOrDefault(JsonElement element, string propertyName, string fallback)
        {
            if (element.ValueKind == JsonValueKind.Object
                && element.TryGetProperty(propertyName, out JsonElement value)
                && value.ValueKind == JsonValueKind.String)
            {
                return value.GetString() ?? fallback;
            }

            return fallback;
        }

        private static int GetIntOrDefault(JsonElement element, string propertyName, int fallback)
        {
            if (element.ValueKind == JsonValueKind.Object
                && element.TryGetProperty(propertyName, out JsonElement value)
                && value.ValueKind == JsonValueKind.Number
                && value.TryGetInt32(out int result))
            {
                return result;
            }

            return fallback;
        }

        private static bool GetBoolOrDefault(JsonElement element, string propertyName, bool fallback)
        {
            if (element.ValueKind == JsonValueKind.Object
                && element.TryGetProperty(propertyName, out JsonElement value)
                && (value.ValueKind == JsonValueKind.True || value.ValueKind == JsonValueKind.False))
            {
                return value.GetBoolean();
            }

            return fallback;
        }

        private static JsonElement GetObjectOrDefault(JsonElement element, string propertyName)
        {
            if (element.ValueKind == JsonValueKind.Object
                && element.TryGetProperty(propertyName, out JsonElement value)
                && value.ValueKind == JsonValueKind.Object)
            {
                return value;
            }

            return default;
        }

        private static int GetThresholdOrDefault(
            JsonElement profile,
            JsonElement farming,
            JsonElement legacyTarget,
            string profileKey,
            string legacyKey,
            int fallback)
        {
            int rootFallback = GetIntOrDefault(
                farming,
                profileKey,
                GetIntOrDefault(legacyTarget, legacyKey, fallback));

            return GetIntOrDefault(profile, profileKey, rootFallback);
        }

        private static MainVillageConfig GetMainVillageConfig(JsonElement cfg, int villageIdx)
        {
            JsonElement profile = LoadVillageProfile(villageIdx);
            JsonElement farming = GetObjectOrDefault(cfg, "farming_thresholds");
            JsonElement target = GetObjectOrDefault(cfg, "target_data_threshold");

            var targetConfig = new FarmingTargetConfig(
                GoldThreshold: GetThresholdOrDefault(profile, farming, target, "gold_threshold", "gold", 0),
                ElixirThreshold: GetThresholdOrDefault(profile, farming, target, "elixir_threshold", "elixir", 0),
                DarkElixirThreshold: GetThresholdOrDefault(profile, farming, target, "dark_elixir_threshold", "dark_elixir", 0),
                TotalResourceThreshold: GetThresholdOrDefault(profile, farming, target, "total_resource_threshold", "total", 0),
                Logic: ParseTargetSelectionLogic(GetStringOrDefault(profile, "target_logic", GetStringOrDefault(farming, "target_logic", "total"))));

            int defaultTotal = targetConfig.GoldThreshold + targetConfig.ElixirThreshold;
            if (targetConfig.TotalResourceThreshold <= 0)
            {
                targetConfig = targetConfig with { TotalResourceThreshold = defaultTotal };
            }

            string attackModeText = GetStringOrDefault(profile, "attack_mode", GetStringOrDefault(cfg, "attack_mode", "attack"));
            AttackMode attackMode = string.Equals(attackModeText, "donate_only", StringComparison.OrdinalIgnoreCase)
                ? AttackMode.DonateOnly
                : AttackMode.Attack;

            var surrender = new SmartSurrenderConfig(
                Enabled: GetBoolOrDefault(profile, "smart_surrender_enabled", GetBoolOrDefault(cfg, "smart_surrender_enabled", false)),
                AfterSecondsEnabled: GetBoolOrDefault(profile, "surrender_after_seconds_enabled", GetBoolOrDefault(cfg, "surrender_after_seconds_enabled", false)),
                AfterSeconds: GetIntOrDefault(profile, "surrender_after_seconds", GetIntOrDefault(cfg, "surrender_after_seconds", 0)),
                LowResourcesEnabled: GetBoolOrDefault(profile, "surrender_low_resources_enabled", GetBoolOrDefault(cfg, "surrender_low_resources_enabled", false)),
                LowResourcesThreshold: GetIntOrDefault(profile, "surrender_low_resources_threshold", GetIntOrDefault(cfg, "surrender_low_resources_threshold", 0)));

            return new MainVillageConfig(
                AttackMode: attackMode,
                Target: targetConfig,
                RequestTroops: GetBoolOrDefault(profile, "request_troops", GetBoolOrDefault(cfg, "request_troops", false)),
                RequestTroopsMessage: GetStringOrDefault(profile, "request_troops_message", GetStringOrDefault(cfg, "request_troops_message", "")),
                UseEventTroops: GetBoolOrDefault(profile, "use_event_troops", GetBoolOrDefault(cfg, "use_event_troops", false)),
                UseCake: GetBoolOrDefault(profile, "use_cake", GetBoolOrDefault(cfg, "use_cake", false)),
                SmartSurrender: surrender);
        }

        private static TargetSelectionLogic ParseTargetSelectionLogic(string logic)
        {
            return logic.Trim().ToLowerInvariant() switch
            {
                "and" => TargetSelectionLogic.And,
                "or" => TargetSelectionLogic.Or,
                _ => TargetSelectionLogic.Total
            };
        }

        private static TrainingConfig GetTrainingConfig(JsonElement cfg, int villageIdx)
        {
            JsonElement profile = LoadVillageProfile(villageIdx);
            return new TrainingConfig(
                Mode: GetStringOrDefault(profile, "train_mode", GetStringOrDefault(cfg, "train_mode", "smart")),
                QuickSlot: GetIntOrDefault(profile, "quick_slot", GetIntOrDefault(cfg, "quick_slot", 1)),
                AttackStrategy: GetAttackStrategy(cfg, villageIdx));
        }

        private static string GetAttackStrategy(JsonElement cfg, int villageIdx)
        {
            JsonElement profile = LoadVillageProfile(villageIdx);
            return GetStringOrDefault(profile, "attack", GetStringOrDefault(cfg, "attack", "Dragon_Attack"));
        }

        /// <summary>
        /// Nạp thông tin cấu hình nâng cấp tường của tài khoản tương ứng từ Village profile.
        /// </summary>
        /// <param name="cfg">Tài liệu JSON chứa cấu hình.</param>
        /// <param name="villageIdx">Chỉ số tài khoản/làng cần nạp cấu hình.</param>
        private static WallUpgradeConfig GetWallUpgradeConfig(JsonElement cfg, int villageIdx)
        {
            if (cfg.ValueKind == JsonValueKind.Object && cfg.TryGetProperty("upgrade_wall", out _))
            {
                bool enabled = GetBoolOrDefault(cfg, "upgrade_wall", false);
                return CreateWallUpgradeConfig(
                    enabled,
                    GetIntOrDefault(cfg, "wall_level", 14),
                    GetWallThreshold(cfg, cfg, "wall_gold_threshold"),
                    GetWallThreshold(cfg, cfg, "wall_elixir_threshold"),
                    GetWallReserve(cfg, cfg, "wall_gold_reserve", 100_000),
                    GetWallReserve(cfg, cfg, "wall_elixir_reserve", 0),
                    GetWallBatchLimit(cfg, cfg),
                    GetBoolOrDefault(cfg, "wall_debug_screenshots", false));
            }

            JsonElement profile = LoadVillageProfile(villageIdx);

            if (profile.ValueKind == JsonValueKind.Object)
            {
                bool enabled = GetBoolOrDefault(profile, "upgrade_wall", false);
                int wallLevel = GetIntOrDefault(profile, "wall_level", GetIntOrDefault(cfg, "wall_level", 14));
                return CreateWallUpgradeConfig(
                    enabled,
                    wallLevel,
                    GetWallThreshold(profile, cfg, "wall_gold_threshold"),
                    GetWallThreshold(profile, cfg, "wall_elixir_threshold"),
                    GetWallReserve(profile, cfg, "wall_gold_reserve", 100_000),
                    GetWallReserve(profile, cfg, "wall_elixir_reserve", 0),
                    GetWallBatchLimit(profile, cfg),
                    GetBoolOrDefault(profile, "wall_debug_screenshots", GetBoolOrDefault(cfg, "wall_debug_screenshots", false)));
            }

            // Dự phòng legacy chỉ dùng khi config mới chưa có khóa upgrade_wall.
            JsonElement wall = GetObjectOrDefault(cfg, "element_state_automation");
            if (wall.ValueKind != JsonValueKind.Object || !GetBoolOrDefault(wall, "upgrade_enabled", false))
            {
                return new WallUpgradeConfig(false, 14, 5_000_000, 5_000_000, 100_000, 0, 1, false);
            }

            return CreateWallUpgradeConfig(
                true,
                GetIntOrDefault(wall, "wall_level", GetIntOrDefault(wall, "target_level", 14)),
                GetIntOrDefault(wall, "wall_gold_threshold", GetIntOrDefault(wall, "min_retained_gold", 5_000_000)),
                GetIntOrDefault(wall, "wall_elixir_threshold", GetIntOrDefault(wall, "min_retained_elixir", 5_000_000)),
                GetIntOrDefault(wall, "wall_gold_reserve", 100_000),
                GetIntOrDefault(wall, "wall_elixir_reserve", 0),
                GetWallBatchLimit(wall, cfg),
                GetBoolOrDefault(wall, "wall_debug_screenshots", GetBoolOrDefault(cfg, "wall_debug_screenshots", false)));
        }

        private static int GetWallBatchLimit(JsonElement primary, JsonElement root)
        {
            int raw = GetIntOrDefault(primary, "wall_batch_limit", GetIntOrDefault(root, "wall_batch_limit", 1));
            return Math.Clamp(raw, 0, 10);
        }

        private static int GetWallThreshold(JsonElement primary, JsonElement root, string key)
        {
            if (TryReadInt(primary, key, out int value) || TryReadInt(root, key, out value))
            {
                return value;
            }

            if (TryReadInt(primary, "wall_upgrade_threshold", out value) || TryReadInt(root, "wall_upgrade_threshold", out value))
            {
                LogLegacyWallConfigMigrated();
                return value;
            }

            return 5_000_000;
        }

        private static int GetWallReserve(JsonElement primary, JsonElement root, string key, int fallback)
        {
            if (TryReadInt(primary, key, out int value) || TryReadInt(root, key, out value))
            {
                return value;
            }

            if (TryReadInt(primary, "wall_reserve_threshold", out value) || TryReadInt(root, "wall_reserve_threshold", out value))
            {
                LogLegacyWallConfigMigrated();
                return value;
            }

            return fallback;
        }

        private static bool TryReadInt(JsonElement element, string key, out int value)
        {
            value = 0;
            return element.ValueKind == JsonValueKind.Object
                && element.TryGetProperty(key, out JsonElement property)
                && property.TryGetInt32(out value);
        }

        private static void LogLegacyWallConfigMigrated()
        {
            if (s_loggedLegacyWallConfigMigration)
            {
                return;
            }

            Console.WriteLine("[CONFIG] event=legacy_config_migrated scope=wall");
            s_loggedLegacyWallConfigMigration = true;
        }

        private static WallUpgradeConfig CreateWallUpgradeConfig(bool enabled, int wallLevel, int goldThreshold, int elixirThreshold, int goldReserve, int elixirReserve, int batchLimit, bool debugScreenshots)
        {
            int safeBatchLimit = Math.Clamp(batchLimit, 0, 10);
            if (wallLevel < WallUpgradeDecider.MinSupportedWallLevel || wallLevel > WallUpgradeDecider.MaxSupportedWallLevel)
            {
                Console.WriteLine($"[WALL WARN] phase=config status=disabled level={wallLevel} wall_batch_limit={safeBatchLimit} reason=unsupported_wall_level supported={WallUpgradeDecider.MinSupportedWallLevel}-{WallUpgradeDecider.MaxSupportedWallLevel}");
                return new WallUpgradeConfig(false, wallLevel, goldThreshold, elixirThreshold, goldReserve, elixirReserve, safeBatchLimit, debugScreenshots);
            }

            return new WallUpgradeConfig(enabled, wallLevel, goldThreshold, elixirThreshold, goldReserve, elixirReserve, safeBatchLimit, debugScreenshots);
        }

        private static JsonElement LoadVillageProfile(int villageIdx)
        {
            string fileName = $"Village_{villageIdx}.json";
            string userData = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SimpliMixi");
            string[] candidates =
            {
                Path.Combine(userData, "profiles", fileName),
                Path.Combine(Directory.GetCurrentDirectory(), "profiles", fileName),
                Path.Combine(AppContext.BaseDirectory, "profiles", fileName)
            };

            foreach (string path in candidates.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                if (!File.Exists(path))
                {
                    continue;
                }

                try
                {
                    using JsonDocument doc = JsonDocument.Parse(File.ReadAllText(path));
                    return doc.RootElement.Clone();
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[FSM-CS WARNING] phase=init status=fail action=load_profile path=\"{path}\" reason=\"{ex.Message}\"");
                    return default;
                }
            }

            return default;
        }

        private static int[] GetSelectedVillages(JsonElement multiConfig)
        {
            if (multiConfig.ValueKind == JsonValueKind.Object
                && multiConfig.TryGetProperty("selected_villages", out JsonElement selected)
                && selected.ValueKind == JsonValueKind.Array)
            {
                var villages = new List<int>();
                foreach (JsonElement item in selected.EnumerateArray())
                {
                    if (item.ValueKind == JsonValueKind.Number && item.TryGetInt32(out int villageIdx))
                    {
                        villages.Add(villageIdx);
                    }
                }

                if (villages.Count > 0)
                {
                    return villages.ToArray();
                }
            }

            return new[] { 1, 2 };
        }

        private static AccountConfig[] GetConfiguredAccounts(JsonElement multiConfig)
        {
            if (multiConfig.ValueKind == JsonValueKind.Object
                && multiConfig.TryGetProperty("accounts", out JsonElement accounts)
                && accounts.ValueKind == JsonValueKind.Array)
            {
                AccountConfig[] parsed = accounts.EnumerateArray()
                    .Select((account, index) => ParseAccountConfig(account, index + 1))
                    .Where(account => account.Enabled)
                    .ToArray();

                if (parsed.Length > 0)
                {
                    return parsed;
                }
            }

            return GetSelectedVillages(multiConfig)
                .Select(village => new AccountConfig(
                    Id: $"acc_{village}",
                    Name: $"Account {village}",
                    ProfileVillage: village,
                    TargetVillage: "main_village",
                    TemplatePath: string.Empty,
                    Enabled: true,
                    ConfigPreset: string.Empty))
                .ToArray();
        }

        private static AccountConfig ParseAccountConfig(JsonElement account, int fallbackIndex)
        {
            int profileVillage = Math.Clamp(GetIntOrDefault(account, "profileVillage", fallbackIndex), 1, 5);
            string id = GetStringOrDefault(account, "id", $"acc_{profileVillage}");
            string name = GetStringOrDefault(account, "name", $"Account {profileVillage}");
            string targetVillage = GetStringOrDefault(account, "targetVillage", "main_village");
            string templatePath = GetStringOrDefault(account, "templatePath", string.Empty);
            bool enabled = GetBoolOrDefault(account, "enabled", true);
            string configPreset = GetStringOrDefault(account, "configPreset", string.Empty);

            return new AccountConfig(id, name, profileVillage, targetVillage, templatePath, enabled, configPreset);
        }

        private bool ShouldSwitchAccount(
            DateTime slotStart,
            int slotBattleStart,
            int slotClanPointStart,
            int villageIdx,
            bool switchByMinutes,
            int intervalSecs,
            bool switchByBattles,
            int battleLimit,
            bool switchByClanPoints,
            int clanPointLimit,
            out string reason)
        {
            if (switchByBattles && battleLimit > 0 && _sessionBattlesCompleted - slotBattleStart >= battleLimit)
            {
                reason = "battle_limit";
                return true;
            }

            if (switchByClanPoints && clanPointLimit > 0 && ReadClanGamesPoints(villageIdx) - slotClanPointStart >= clanPointLimit)
            {
                reason = "clan_games_points";
                return true;
            }

            if (switchByMinutes && intervalSecs > 0 && (DateTime.Now - slotStart).TotalSeconds >= intervalSecs)
            {
                reason = "minute_limit";
                return true;
            }

            reason = "none";
            return false;
        }

        private static int ReadClanGamesPoints(int villageIdx)
        {
            string path = StatsFilePath(villageIdx);
            JsonObject stats = LoadStatsFromDisk(path);
            return GetJsonInt(stats, "clan_games_points");
        }

        // --- Liên kết thư viện ngoài (DLL Import) của hệ điều hành Windows ---
        // Phục vụ gửi phím/chuột ngầm (PostMessage) và gắn kết tiến trình (AttachThreadInput)
        [System.Runtime.InteropServices.DllImport("user32.dll", CharSet = System.Runtime.InteropServices.CharSet.Auto)]
        private static extern bool PostMessage(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);

        [System.Runtime.InteropServices.DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr FindWindow(string? lpClassName, string? lpWindowName);

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        [return: System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)]
        private static extern bool AttachThreadInput(uint idAttach, uint idAttachTo, bool fAttach);

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

        [System.Runtime.InteropServices.DllImport("kernel32.dll")]
        private static extern uint GetCurrentThreadId();

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        [return: System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)]
        private static extern bool GetWindowRect(IntPtr hWnd, out WinRect lpRect);

        private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        [return: System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)]
        private static extern bool EnumChildWindows(IntPtr hWndParent, EnumWindowsProc lpEnumFunc, IntPtr lParam);

        [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
        private struct WinRect
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
        }

        /// <summary>
        /// Tìm Handle cửa sổ chính (MainWindowHandle) của Windows dựa trên danh sách các tên tiến trình (Process) ứng viên.
        /// </summary>
        private static IntPtr FindMainWindowByProcessName(params string[] processNames)
        {
            foreach (string processName in processNames)
            {
                try
                {
                    foreach (var process in System.Diagnostics.Process.GetProcessesByName(processName))
                    {
                        if (process.MainWindowHandle != IntPtr.Zero)
                        {
                            return process.MainWindowHandle;
                        }
                    }
                }
                catch
                {
                    // Quản lý ngoại lệ nếu tiến trình bị tắt đột ngột
                }
            }

            return IntPtr.Zero;
        }

        /// <summary>
        /// Gửi phím tắt ngầm đến cửa sổ Handle mà không cần kích hoạt cửa sổ đó lên hàng trước (Background Key Send).
        /// Hỗ trợ gắn kết luồng dữ liệu đầu vào (AttachThreadInput) để nâng cao độ chính xác gửi phím ngầm của Win32.
        /// </summary>
        private static void SendKeyToWindow(IntPtr hWnd, IntPtr virtualKey, int repetitions, int gapMs)
        {
            const uint WM_KEYDOWN = 0x0100;
            const uint WM_KEYUP = 0x0101;

            uint currentThreadId = GetCurrentThreadId();
            uint targetThreadId = GetWindowThreadProcessId(hWnd, out _);

            for (int i = 0; i < repetitions; i++)
            {
                bool attached = false;
                if (targetThreadId != 0 && targetThreadId != currentThreadId)
                {
                    // Gắn kết luồng nhập dữ liệu của bot với tiến trình giả lập để Windows cho phép PostMessage phím hệ thống
                    attached = AttachThreadInput(currentThreadId, targetThreadId, true);
                }

                try
                {
                    PostMessage(hWnd, WM_KEYDOWN, virtualKey, IntPtr.Zero);
                    Thread.Sleep(20);
                    PostMessage(hWnd, WM_KEYUP, virtualKey, IntPtr.Zero);
                }
                finally
                {
                    if (attached)
                    {
                        AttachThreadInput(currentThreadId, targetThreadId, false);
                    }
                }

                Thread.Sleep(gapMs);
            }
        }

        private static bool ShouldAcceptTarget((int Gold, int Elixir, int DarkElixir) resources, FarmingTargetConfig config, out string reason)
        {
            int total = resources.Gold + resources.Elixir;
            bool goldOk = resources.Gold >= config.GoldThreshold;
            bool elixirOk = resources.Elixir >= config.ElixirThreshold;
            bool darkOk = config.DarkElixirThreshold <= 0 || resources.DarkElixir >= config.DarkElixirThreshold;
            bool totalOk = total >= config.TotalResourceThreshold;

            bool accepted = config.Logic switch
            {
                TargetSelectionLogic.And => goldOk && elixirOk && darkOk,
                TargetSelectionLogic.Or => goldOk || elixirOk || darkOk,
                _ => totalOk && darkOk
            };

            reason = config.Logic switch
            {
                TargetSelectionLogic.And => $"and gold_ok={goldOk} elixir_ok={elixirOk} dark_ok={darkOk}",
                TargetSelectionLogic.Or => $"or gold_ok={goldOk} elixir_ok={elixirOk} dark_ok={darkOk}",
                _ => $"total total_ok={totalOk} dark_ok={darkOk}"
            };
            return accepted;
        }

        private void RunDonateOnlyCycle(MainVillageConfig config, CancellationToken token)
        {
            Console.WriteLine("[DONATE-CS] phase=donate_only status=start");
            TryUseCakeIfConfigured(config, token);
            TryRequestTroopsIfConfigured(config, token);
            TryDonateOnce(token);
            InterruptibleSleep(5000, token);
            Console.WriteLine("[DONATE-CS] phase=donate_only status=success");
        }

        private void TryRequestTroopsIfConfigured(MainVillageConfig config, CancellationToken token)
        {
            if (!config.RequestTroops || CheckStop(token)) return;

            Console.WriteLine("[REQUEST-CS] phase=request_troops status=start");
            if (TapFirstVisibleTemplate(new[] { @"ui\request_button_unavailable", "request_button_unavailable" }, 0.78, null, out _, tap: false))
            {
                Console.WriteLine("[REQUEST-CS] phase=request_troops status=skip reason=cooldown_or_unavailable");
                return;
            }

            if (!TapFirstVisibleTemplate(new[] { @"ui\request_troops", @"ui\request_button", "request_button" }, 0.70, null, out string matched))
            {
                Console.WriteLine("[REQUEST-CS] phase=request_troops status=fail reason=request_button_not_found");
                return;
            }

            if (!string.IsNullOrWhiteSpace(config.RequestTroopsMessage))
            {
                string escaped = config.RequestTroopsMessage.Replace("'", "");
                _adb.ExecuteShell($"input text '{escaped.Replace(" ", "%s")}'");
                InterruptibleSleep(300, token);
            }

            TapFirstVisibleTemplate(new[] { @"ui\request_button", "request_button" }, 0.70, null, out _, tap: true);
            Console.WriteLine($"[REQUEST-CS] phase=request_troops status=success template=\"{matched}\"");
        }

        private void TryUseCakeIfConfigured(MainVillageConfig config, CancellationToken token)
        {
            if (!config.UseCake || CheckStop(token)) return;

            Console.WriteLine("[EVENT-CS] phase=use_cake status=start");
            if (TapFirstVisibleTemplate(new[] { @"ui\clan_castle_cake", "clan_castle_cake" }, 0.72, null, out string matched))
            {
                Console.WriteLine($"[EVENT-CS] phase=use_cake status=success template=\"{matched}\"");
                InterruptibleSleep(1000, token);
                return;
            }

            Console.WriteLine("[EVENT-CS] phase=use_cake status=skip reason=item_not_found");
        }

        private void TryDonateOnce(CancellationToken token)
        {
            if (CheckStop(token)) return;

            Console.WriteLine("[DONATE-CS] phase=scan_chat status=start");
            if (!TapFirstVisibleTemplate(new[] { @"ui\donate_button", "donate_button" }, 0.72, null, out string matched))
            {
                Console.WriteLine("[DONATE-CS] phase=scan_chat status=skip reason=donate_button_not_found");
                return;
            }

            Console.WriteLine($"[DONATE-CS] phase=donate status=pending template=\"{matched}\" details=\"donate_panel_opened\"");
            InterruptibleSleep(700, token);
        }

        private bool TapFirstVisibleTemplate(string[] templates, double threshold, Rect? roi, out string matchedTemplate, bool tap = true)
        {
            matchedTemplate = string.Empty;
            using Mat? screenshot = _adb.TakeScreenshot();
            if (screenshot == null || screenshot.Empty()) return false;

            foreach (string template in templates)
            {
                Point? center = _vision.FindElement(screenshot, template, threshold, roi, out double score);
                if (center == null) continue;

                matchedTemplate = template;
                Console.WriteLine($"[VISION] phase=template_match status=success template=\"{template}\" score={score:F2} center=({center.Value.X},{center.Value.Y})");
                if (tap)
                {
                    _adb.Tap(center.Value.X, center.Value.Y);
                }
                return true;
            }

            return false;
        }

        private bool ShouldSmartSurrender(DateTime battleStart, SmartSurrenderConfig config, out string reason)
        {
            reason = "none";
            double elapsedSeconds = (DateTime.Now - battleStart).TotalSeconds;
            if (config.AfterSecondsEnabled && config.AfterSeconds > 0 && elapsedSeconds >= config.AfterSeconds)
            {
                reason = "time_limit";
                return true;
            }

            if (config.LowResourcesEnabled && config.LowResourcesThreshold > 0)
            {
                var resources = IsTarget.ExtractResources(_adb, _vision);
                int remainingTotal = resources.Gold + resources.Elixir;
                if (remainingTotal > 0 && remainingTotal <= config.LowResourcesThreshold)
                {
                    reason = "low_resources";
                    return true;
                }
            }

            return false;
        }

        private void ExecuteSurrender(string reason, CancellationToken token)
        {
            _adb.Tap(80, 780);
            if (InterruptibleSleep(1000, token)) return;
            _adb.Tap(960, 560);
            Console.WriteLine($"[ATTACK-CS] phase=surrender status=success reason={reason}");
            InterruptibleSleep(2000, token);
        }

        /// <summary>
        /// Vòng lặp chờ đợi trận đánh kết thúc.
        /// Quét nhận diện liên tục nút Tiếp tục (Continue) hoặc vạch hiển thị kết quả chiến tích cướp tài nguyên.
        /// </summary>
        private bool WaitBattleEnd(CancellationToken token, SmartSurrenderConfig? surrenderConfig = null)
        {
            Console.WriteLine("[FSM-CS] phase=battle_wait status=start");

            DateTime start = DateTime.Now;
            int stableResultMatches = 0;
            bool waitingLogged = false;
            bool resultDetectedLogged = false;
            bool smartSurrenderExecuted = false;
            while (!CheckStop(token))
            {
                WaitIfPaused(token);
                if (CheckStop(token)) return false;

                if (BattleEnded(out string resultMatchInfo))
                {
                    stableResultMatches++;
                    if (!resultDetectedLogged)
                    {
                        Console.WriteLine("[FSM-CS] phase=battle_wait status=pending details=\"result_screen_detected\"");
                        resultDetectedLogged = true;
                    }

                    if (stableResultMatches >= ResultScreenStableMatches)
                    {
                        Console.WriteLine("[FSM-CS] phase=battle_wait status=success");
                        Thread.Sleep(1000);
                        return true;
                    }
                }
                else
                {
                    stableResultMatches = 0;
                    if (!waitingLogged)
                    {
                        Console.WriteLine("[FSM-CS] phase=battle_wait status=pending details=\"waiting\"");
                        waitingLogged = true;
                    }
                }

                if (ConnectionPopupVisible(out string matchInfo, allowDialogShapeFallback: false))
                {
                    Console.WriteLine($"[FSM-CS WARNING] phase=battle_wait status=fail reason=connection_lost details=\"{matchInfo}\"");
                    BootRecovery();
                    return false;
                }

                if (surrenderConfig?.Enabled == true && !smartSurrenderExecuted && !resultDetectedLogged && ShouldSmartSurrender(start, surrenderConfig, out string surrenderReason))
                {
                    Console.WriteLine($"[ATTACK-CS] phase=surrender status=start reason={surrenderReason}");
                    smartSurrenderExecuted = true;
                    ExecuteSurrender("smart_" + surrenderReason, token);
                    continue;
                }

                if ((DateTime.Now - start).TotalSeconds >= MaxWaitBattleSeconds)
                {
                    Console.WriteLine("[FSM-CS WARNING] phase=battle_wait status=fail reason=timeout");
                    return false;
                }

                Thread.Sleep(1000);
            }

            return false;
        }

        /// <summary>
        /// Dò tìm xem màn hình kết quả trận đấu đã hiển thị chưa bằng cách tìm cả nút Continue và icon tài nguyên thu về.
        /// </summary>
        private bool BattleEnded(out string matchInfo)
        {
            matchInfo = "continue score=0.00, result-marker score=0.00";

            using Mat? screenshot = _adb.TakeScreenshot();
            if (screenshot == null || screenshot.Empty())
            {
                return false;
            }

            bool hasContinue = TryFindContinueButton(screenshot, out Point center, out double continueScore);
            bool hasResultMarker = TryMatchTemplateRegion(
                screenshot,
                @"ui\resources_gained.png",
                ResultYouGotRoi,
                ResultYouGotRoi,
                ResultYouGotThreshold,
                out _,
                out double markerScore);

            matchInfo = $"continue score={continueScore:F2} center=({center.X},{center.Y}), result-marker score={markerScore:F2}";

            if (hasContinue && hasResultMarker)
            {
                return true;
            }

            return false;
        }

        private bool TryFindContinueButton(Mat screenshot, out Point center, out double score)
        {
            return TryMatchTemplate(screenshot, @"ui\return_home.png", ResultContinueRoi, ResultContinueThreshold, out center, out score)
                || TryMatchTemplate(screenshot, "return_home.png", ResultContinueRoi, ResultContinueThreshold, out center, out score);
        }

        private bool DismissStarBonusIfPresent()
        {
            using Mat? screenshot = _adb.TakeScreenshot();
            if (screenshot == null || screenshot.Empty())
            {
                return false;
            }

            if (!TryFindStarBonusPopup(screenshot, out _, out double score))
            {
                return false;
            }

            Console.WriteLine($"[FSM-CS] phase=reward_check status=success action=dismiss_popup score={score:F2}");
            _adb.Tap(StarBonusOkayTapPoint.X, StarBonusOkayTapPoint.Y);
            Thread.Sleep(1500);
            return true;
        }

        private bool TryFindStarBonusPopup(Mat screenshot, out Point center, out double score)
        {
            center = default;
            score = 0;

            bool hasUiTemplate = TemplateAssetLoader.Exists(_templatesPath, @"ui\star_bonus_received.png");
            bool hasRootTemplate = TemplateAssetLoader.Exists(_templatesPath, "star_bonus_received.png");
            if (!hasUiTemplate && !hasRootTemplate)
            {
                return false;
            }

            return (hasUiTemplate && TryMatchTemplate(screenshot, @"ui\star_bonus_received.png", StarBonusPopupRoi, StarBonusPopupThreshold, out center, out score))
                || (hasRootTemplate && TryMatchTemplate(screenshot, "star_bonus_received.png", StarBonusPopupRoi, StarBonusPopupThreshold, out center, out score));
        }

        private bool TryMatchTemplateRegion(
            Mat source,
            string templateFileName,
            Rect sourceRoi,
            Rect templateRoi,
            double threshold,
            out Point center,
            out double score)
        {
            center = default;
            score = 0;

            if (source.Empty())
            {
                return false;
            }

            if (!TemplateAssetLoader.Exists(_templatesPath, templateFileName))
            {
                Console.WriteLine($"[VISION] phase=match_template status=fail reason=template_missing details=\"{templateFileName}\"");
                return false;
            }

            using Mat fullTemplate = TemplateAssetLoader.Load(_templatesPath, templateFileName, ImreadModes.Grayscale);
            if (fullTemplate.Empty())
            {
                Console.WriteLine($"[VISION] phase=match_template status=fail reason=template_unreadable details=\"{templateFileName}\"");
                return false;
            }

            Rect safeSourceRoi = ImageUtils.ClampRect(sourceRoi, source.Width, source.Height);
            Rect safeTemplateRoi = ImageUtils.ClampRect(templateRoi, fullTemplate.Width, fullTemplate.Height);
            if (safeSourceRoi.Width <= 0 || safeSourceRoi.Height <= 0 || safeTemplateRoi.Width <= 0 || safeTemplateRoi.Height <= 0)
            {
                return false;
            }

            using Mat template = new Mat(fullTemplate, safeTemplateRoi);
            if (safeSourceRoi.Width < template.Width || safeSourceRoi.Height < template.Height)
            {
                return false;
            }

            using Mat sourceCrop = new Mat(source, safeSourceRoi);
            using Mat graySource = new Mat();
            Cv2.CvtColor(sourceCrop, graySource, ColorConversionCodes.BGR2GRAY);

            using Mat result = new Mat();
            Cv2.MatchTemplate(graySource, template, result, TemplateMatchModes.CCoeffNormed);
            Cv2.MinMaxLoc(result, out _, out score, out _, out Point maxLoc);

            center = new Point(
                safeSourceRoi.X + maxLoc.X + template.Width / 2,
                safeSourceRoi.Y + maxLoc.Y + template.Height / 2
            );

            return score >= threshold;
        }

        /// <summary>
        /// Khớp mẫu ảnh vùng chọn nâng cao hỗ trợ quét đa tỷ lệ (Multi-Scale) từ 1.0 đến 1.25 lần.
        /// Dành cho các dòng giả lập hiển thị tỷ lệ rương báu (Treasure Hunt) co giãn nhẹ.
        /// </summary>
        private bool TryMatchTemplateRegionMultiScale(
            Mat source,
            string templateFileName,
            Rect sourceRoi,
            Rect templateRoi,
            double threshold,
            out Point center,
            out double score)
        {
            center = default;
            score = 0;

            if (source.Empty())
            {
                return false;
            }

            if (!TemplateAssetLoader.Exists(_templatesPath, templateFileName))
            {
                Console.WriteLine($"[VISION] phase=match_template status=fail reason=template_missing details=\"{templateFileName}\"");
                return false;
            }

            using Mat fullTemplate = TemplateAssetLoader.Load(_templatesPath, templateFileName, ImreadModes.Grayscale);
            if (fullTemplate.Empty())
            {
                Console.WriteLine($"[VISION] phase=match_template status=fail reason=template_unreadable details=\"{templateFileName}\"");
                return false;
            }

            Rect safeSourceRoi = ImageUtils.ClampRect(sourceRoi, source.Width, source.Height);
            Rect safeTemplateRoi = ImageUtils.ClampRect(templateRoi, fullTemplate.Width, fullTemplate.Height);
            if (safeSourceRoi.Width <= 0 || safeSourceRoi.Height <= 0 || safeTemplateRoi.Width <= 0 || safeTemplateRoi.Height <= 0)
            {
                return false;
            }

            using Mat sourceCrop = new Mat(source, safeSourceRoi);
            using Mat sourceGray = new Mat();
            Cv2.CvtColor(sourceCrop, sourceGray, ColorConversionCodes.BGR2GRAY);

            using Mat templateCrop = new Mat(fullTemplate, safeTemplateRoi);
            double[] scales = { 1.00, 1.05, 1.10, 1.15, 1.20, 1.25 };
            foreach (double scale in scales)
            {
                int scaledWidth = Math.Max(1, (int)Math.Round(templateCrop.Width * scale));
                int scaledHeight = Math.Max(1, (int)Math.Round(templateCrop.Height * scale));
                if (scaledWidth > sourceGray.Width || scaledHeight > sourceGray.Height)
                {
                    continue;
                }

                using Mat scaledTemplate = new Mat();
                Cv2.Resize(templateCrop, scaledTemplate, new Size(scaledWidth, scaledHeight), 0, 0, InterpolationFlags.Linear);

                using Mat result = new Mat();
                Cv2.MatchTemplate(sourceGray, scaledTemplate, result, TemplateMatchModes.CCoeffNormed);
                Cv2.MinMaxLoc(result, out _, out double currentScore, out _, out Point maxLoc);

                if (currentScore > score)
                {
                    score = currentScore;
                    center = new Point(
                        safeSourceRoi.X + maxLoc.X + scaledTemplate.Width / 2,
                        safeSourceRoi.Y + maxLoc.Y + scaledTemplate.Height / 2
                    );
                }
            }

            return score >= threshold;
        }

        /// <summary>
        /// Thực hiện quét đếm số sao đạt được từ màn hình kết thúc trận đấu.
        /// Thử dò tìm các template one_star, two_star, three_star tương ứng.
        /// </summary>
        private int GetStarsFromScreen()
        {
            Thread.Sleep(500);

            using Mat? screenshot = _adb.TakeScreenshot();
            Thread.Sleep(500);

            if (screenshot == null || screenshot.Empty())
            {
                return 0;
            }

            if (!TryMatchTemplate(screenshot, @"ui\one_star.png", Rect.FromLTRB(518, 90, 747, 316), 0.40, out _, out _))
            {
                return 0;
            }

            if (!TryMatchTemplate(screenshot, @"ui\two_star.png", Rect.FromLTRB(670, 106, 926, 285), 0.40, out _, out _))
            {
                return 1;
            }

            return TryMatchTemplate(screenshot, @"ui\three_star.png", Rect.FromLTRB(840, 96, 1064, 317), 0.40, out _, out _)
                ? 3
                : 2;
        }

        /// <summary>
        /// Thực hiện đọc số lượng tài nguyên (Vàng, Elixir, Hắc dầu) cướp được từ màn hình kết thúc trận đấu.
        /// Tổng tài nguyên = Lượng cướp được từ kho chứa đối thủ (Left ROI) + Lượng thưởng thêm theo số sao (Right ROI).
        /// </summary>
        private (int Gold, int Elixir, int DarkElixir) GainResources(int stars)
        {
            Thread.Sleep(500);

            using Mat? screenshot = _adb.TakeScreenshot();
            Thread.Sleep(500);

            if (screenshot == null || screenshot.Empty())
            {
                return (0, 0, 0);
            }

            SaveDebugImage(screenshot, "debug_stats_result.png");

            // Đọc số lượng cướp được bên trái (Left Side)
            int goldLeft = OcrResourceSum(screenshot, Rect.FromLTRB(586, 372, 825, 420), "gold_loot", 1000);
            int elixirLeft = OcrResourceSum(screenshot, Rect.FromLTRB(590, 431, 827, 482), "elixir_loot", 1000);
            int deLeft = OcrResourceSum(screenshot, Rect.FromLTRB(643, 489, 826, 539), "dark_loot", 100);

            int goldRight = 0;
            int elixirRight = 0;
            int deRight = 0;

            // Nếu đạt tối thiểu 1 sao, đọc thêm lượng thưởng bonus liên minh bên phải (Right Side)
            if (stars > 0)
            {
                goldRight = OcrResourceSum(screenshot, Rect.FromLTRB(1012, 444, 1176, 490), "gold_bonus", 1000);
                elixirRight = OcrResourceSum(screenshot, Rect.FromLTRB(1016, 493, 1176, 537), "elixir_bonus", 1000);
                deRight = OcrResourceSum(screenshot, Rect.FromLTRB(1036, 541, 1176, 584), "dark_bonus", 100);
            }

            return (goldLeft + goldRight, elixirLeft + elixirRight, deLeft + deRight);
        }

        private int OcrResourceSum(Mat screenshot, Rect roi, string label, int minValidValue)
        {
            SaveStatsCrop(screenshot, roi, label);

            // Thử dùng RGB Thresh trước
            if (_vision.TryExtractNumericalMetrics(screenshot, roi, out int value, out double confidence, useRgbThresh: true))
            {
                if (IsPlausibleResourceValue(value, confidence, minValidValue, label, "rgb"))
                {
                    return value;
                }
            }

            // Thử nhị phân xám dự phòng
            if (_vision.TryExtractNumericalMetrics(screenshot, roi, out value, out confidence))
            {
                if (IsPlausibleResourceValue(value, confidence, minValidValue, label, "gray"))
                {
                    return value;
                }
            }

            Console.WriteLine($"[FSM-CS] phase=stats_ocr label=\"{label}\" status=fail reason=unreadable");
            return 0;
        }

        private static bool IsPlausibleResourceValue(int value, double confidence, int minValidValue, string label, string mode)
        {
            bool plausible = value == 0 || value >= minValidValue;
            if (confidence < 0.62)
            {
                Console.WriteLine($"[FSM-CS] phase=stats_ocr label=\"{label}\" status=fail reason=confidence_low");
                return false;
            }

            if (!plausible)
            {
                Console.WriteLine($"[FSM-CS] phase=stats_ocr label=\"{label}\" status=fail reason=implausible_value");
                return false;
            }

            Console.WriteLine($"[FSM-CS] phase=stats_ocr label=\"{label}\" status=success value={value}");
            return true;
        }

        private static void SaveStatsCrop(Mat screenshot, Rect roi, string label)
        {
            try
            {
                Rect safeRoi = ImageUtils.ClampRect(roi, screenshot.Width, screenshot.Height);
                if (safeRoi.Width <= 0 || safeRoi.Height <= 0)
                {
                    return;
                }

                using Mat crop = new Mat(screenshot, safeRoi);
                SaveDebugImage(crop, $"debug_stats_{label}.png");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[FSM-CS WARNING] phase=stats_ocr status=fail action=save_crop label=\"{label}\" reason=\"{ex.Message}\"");
            }
        }

        private static void SaveDebugImage(Mat image, string fileName)
        {
            try
            {
                Directory.CreateDirectory(WritableLogsDirectory);
                Cv2.ImWrite(Path.Combine(WritableLogsDirectory, fileName), image);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[FSM-CS WARNING] phase=log status=fail action=save_debug_image file=\"{fileName}\" reason=\"{ex.Message}\"");
            }
        }

        private static string ResolveWritableLogsDirectory()
        {
            string appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            return Path.Combine(appData, "SimpliMixi", "logs");
        }

        /// <summary>
        /// Bấm quay trở về Làng chính sau khi trận đánh kết thúc.
        /// Giải tỏa rương báu hoặc bấm Back hệ thống Android nếu bị kẹt.
        /// </summary>
        private bool ReturnHome()
        {
            Console.WriteLine("[FSM-CS] phase=return_home status=start");

            const int maxReturnAttempts = 3;
            for (int attempt = 1; attempt <= maxReturnAttempts; attempt++)
            {
                Console.WriteLine($"[FSM-CS] phase=return_home status=pending attempt={attempt}");

                using Mat? screenshot = _adb.TakeScreenshot();
                if (screenshot != null && !screenshot.Empty() && TryFindContinueButton(screenshot, out Point continueCenter, out double score))
                {
                    Console.WriteLine($"[FSM-CS] phase=return_home status=pending action=continue score={score:F2} attempt={attempt}");
                    _adb.Tap(continueCenter.X, continueCenter.Y);
                }
                else
                {
                    Console.WriteLine($"[FSM-CS WARNING] phase=return_home status=pending action=fallback_tap reason=continue_unavailable attempt={attempt}");
                    _adb.Tap(788, 768);
                }

                Thread.Sleep(2500);
                DismissStarBonusIfPresent();

                if (DetectHomeBase(out string homeReason))
                {
                    Console.WriteLine($"[FSM-CS] phase=return_home status=success reason=home_detected details=\"{homeReason}\" attempt={attempt}");
                    Console.WriteLine("[FSM-CS] phase=return_home action=ensure_home status=success");
                    return true;
                }

                if (HandleTreasureHuntIfPresent(verboseNotFound: false))
                {
                    Console.WriteLine($"[FSM-CS] phase=return_home status=pending action=clear_treasure_hunt attempt={attempt}");
                    Thread.Sleep(1500);
                }
                else
                {
                    HandleOpenedTreasureChest();
                }

                if (DetectHomeBase(out homeReason))
                {
                    Console.WriteLine($"[FSM-CS] phase=return_home status=success reason=home_detected details=\"{homeReason}\" attempt={attempt}");
                    Console.WriteLine("[FSM-CS] phase=return_home action=ensure_home status=success");
                    return true;
                }

                Console.WriteLine($"[FSM-CS] phase=return_home status=clear_overlay action=android_back reason=home_blocked attempt={attempt}");
                _adb.ExecuteShell("input keyevent KEYCODE_BACK");
                Thread.Sleep(1500);
            }

            bool homeConfirmed = EnsureHomeBase(maxWaitSeconds: 20);
            Console.WriteLine($"[FSM-CS] phase=return_home action=ensure_home status={(homeConfirmed ? "success" : "fail")}");
            return homeConfirmed;
        }

        /// <summary>
        /// Lưu trữ thông số tài nguyên cướp được lũy kế vào tệp JSON trên đĩa cứng để WPF UI hiển thị thống kê.
        /// </summary>
        private void UpdateStats(int villageIdx, int starsGot, (int Gold, int Elixir, int DarkElixir) gained)
        {
            string path = StatsFilePath(villageIdx);
            JsonObject stats = LoadStatsFromDisk(path);

            stats["gold"] = GetJsonInt(stats, "gold") + gained.Gold;
            stats["elixir"] = GetJsonInt(stats, "elixir") + gained.Elixir;
            stats["de"] = GetJsonInt(stats, "de") + gained.DarkElixir;
            stats["attacks"] = GetJsonInt(stats, "attacks") + 1;
            stats["last_update_ts"] = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

            JsonObject stars = stats["stars"] as JsonObject ?? DefaultStarsObject();
            string key = Math.Clamp(starsGot, 0, 3).ToString();
            stars[key] = GetJsonInt(stars, key) + 1;
            stats["stars"] = stars;

            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, stats.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
            Console.WriteLine($"[FSM-CS] phase=battle_stats status=success action=save_file path=\"{path}\"");
        }

        private void UpdateWallStats(int villageIdx, int upgradedCount)
        {
            if (upgradedCount <= 0) return;

            string path = StatsFilePath(villageIdx);
            JsonObject stats = LoadStatsFromDisk(path);
            stats["walls_upgraded"] = GetJsonInt(stats, "walls_upgraded") + upgradedCount;
            stats["last_update_ts"] = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, stats.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
            Console.WriteLine($"[FSM-CS] phase=wall_stats status=success count={upgradedCount} action=save_file path=\"{path}\"");
        }

        private static string StatsFilePath(int villageIdx)
        {
            string appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            return Path.Combine(appData, "SimpliMixi", "profiles", $"Stats_{villageIdx}.json");
        }

        private enum AttackMode
        {
            Attack,
            DonateOnly
        }

        private enum TargetSelectionLogic
        {
            Or,
            And,
            Total
        }

        private sealed record FarmingTargetConfig(
            int GoldThreshold,
            int ElixirThreshold,
            int DarkElixirThreshold,
            int TotalResourceThreshold,
            TargetSelectionLogic Logic);

        private sealed record SmartSurrenderConfig(
            bool Enabled,
            bool AfterSecondsEnabled,
            int AfterSeconds,
            bool LowResourcesEnabled,
            int LowResourcesThreshold);

        private sealed record MainVillageConfig(
            AttackMode AttackMode,
            FarmingTargetConfig Target,
            bool RequestTroops,
            string RequestTroopsMessage,
            bool UseEventTroops,
            bool UseCake,
            SmartSurrenderConfig SmartSurrender);

        private sealed record WallUpgradeConfig(
            bool Enabled,
            int WallLevel,
            int GoldThreshold,
            int ElixirThreshold,
            int GoldReserve,
            int ElixirReserve,
            int BatchLimit,
            bool DebugScreenshots);

        private sealed record TrainingConfig(
            string Mode,
            int QuickSlot,
            string AttackStrategy);

        private sealed record AccountConfig(
            string Id,
            string Name,
            int ProfileVillage,
            string TargetVillage,
            string TemplatePath,
            bool Enabled,
            string ConfigPreset = "");

        private static JsonObject LoadStatsFromDisk(string path)
        {
            try
            {
                if (File.Exists(path))
                {
                    JsonNode? node = JsonNode.Parse(File.ReadAllText(path));
                    if (node is JsonObject obj)
                    {
                        obj["stars"] ??= DefaultStarsObject();
                        return obj;
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[FSM-CS WARNING] phase=battle_stats status=fail action=load_file path=\"{path}\" reason=\"{ex.Message}\"");
            }

            return new JsonObject
            {
                ["gold"] = 0,
                ["elixir"] = 0,
                ["de"] = 0,
                ["attacks"] = 0,
                ["stars"] = DefaultStarsObject(),
                ["last_update_ts"] = 0
            };
        }

        private static JsonObject DefaultStarsObject()
        {
            return new JsonObject
            {
                ["0"] = 0,
                ["1"] = 0,
                ["2"] = 0,
                ["3"] = 0
            };
        }

        private static int GetJsonInt(JsonObject obj, string key)
        {
            if (!obj.TryGetPropertyValue(key, out JsonNode? node) || node == null)
            {
                return 0;
            }

            return node.GetValueKind() == JsonValueKind.Number && node.GetValue<int>() is int value
                ? value
                : 0;
        }

        /// <summary>
        /// Thực hiện cử chỉ thu nhỏ góc nhìn bản đồ (Zoom Out):
        /// - Đối với MEmu: Gắn kết luồng và gửi phím F3 ngầm qua PostMessage WM_KEYDOWN Win32 API.
        /// - Đối với BlueStacks: Gửi cử chỉ PinchIn đa điểm qua ADB UIAutomator2.
        /// </summary>
        public void ZoomOut()
        {
            Console.WriteLine("[FSM-CS] phase=camera_zoom status=start");

            // Kiểm tra xem giả lập MEmu có đang mở không
            IntPtr memuParent = FindMainWindowByProcessName("MEmu");
            if (memuParent == IntPtr.Zero)
            {
                memuParent = FindWindow(null, "MEmu");
            }

            // Kiểm tra xem giả lập BlueStacks có đang mở không
            IntPtr bsParent = FindMainWindowByProcessName("HD-Player", "BlueStacks");
            if (bsParent == IntPtr.Zero)
            {
                bsParent = FindWindow(null, "BlueStacks App Player");
            }

            if (memuParent != IntPtr.Zero)
            {
                Console.WriteLine("[FSM-CS] phase=camera_zoom status=pending details=\"memu_detected\"");

                // Gửi mã phím F3 (Virtual Key Code = 0x72) 4 lần ngầm vào MEmu để thực hiện thu nhỏ camera
                SendKeyToWindow(memuParent, (IntPtr)0x72, repetitions: 4, gapMs: 1000);
                Console.WriteLine("[FSM-CS] phase=camera_zoom status=success details=\"memu\"");
            }
            else if (bsParent != IntPtr.Zero)
            {
                Console.WriteLine("[FSM-CS] phase=camera_zoom status=pending details=\"bluestacks_detected\"");

                // CoC render bằng OpenGL/SurfaceView nên PostMessage(WM_MOUSEWHEEL) không tới được
                // game (nhưng vẫn trả true → success giả). Dùng thẳng UIAutomator2 pinch-in như
                // Simplicity (zoom_out.py): count=3, interval=0.5s.
                bool ok = _adb.PinchInZoomOut(count: 3, durationMs: 450, intervalMs: 500);
                if (ok)
                {
                    Console.WriteLine("[FSM-CS] phase=camera_zoom status=success details=\"bluestacks_adb_pinch\"");
                }
                else
                {
                    Console.WriteLine("[FSM-CS WARNING] phase=camera_zoom status=fail reason=no_confirmation");
                }
            }
            else
            {
                Console.WriteLine("[FSM-CS WARNING] phase=camera_zoom status=skip reason=emulator_window_not_found");
            }
        }

        /// <summary>
        /// Thu hoạch mỏ tài nguyên (Gold/Elixir/Dark Elixir Collector) trên màn hình Làng chính.
        /// Dò tìm các bong bóng icon tài nguyên lơ lửng trên mỏ và thực hiện chạm (Tap).
        /// </summary>
        private bool CollectResourcesPlaceholder()
        {
            if (HandleBlockingConnectionPopup("[WARN] Connection popup before collect → reload"))
            {
                return true;
            }

            string[] collectorTemplates =
            {
                @"resources\elixir_collector.png",
                @"resources\DE_collector.png",
                @"resources\gold_collector.png"
            };

            using Mat? screenshot = _adb.TakeScreenshot();
            if (screenshot == null || screenshot.Empty())
            {
                Console.WriteLine("[FSM-CS WARNING] phase=collect_resources status=fail reason=screenshot_failed");
                return false;
            }

            using Mat grayScreenshot = new Mat();
            Cv2.CvtColor(screenshot, grayScreenshot, ColorConversionCodes.BGR2GRAY);

            foreach (string templateName in collectorTemplates)
            {
                if (!TemplateAssetLoader.Exists(_templatesPath, templateName))
                {
                    Console.WriteLine($"[VISION] phase=collect_resources status=fail reason=template_missing details=\"{templateName}\"");
                    continue;
                }

                if (HandleBlockingConnectionPopup("[WARN] Connection popup during collect → reload"))
                {
                    return true;
                }

                using Mat template = TemplateAssetLoader.Load(_templatesPath, templateName, ImreadModes.Grayscale);
                if (template.Empty())
                {
                    Console.WriteLine($"[VISION] phase=collect_resources status=fail reason=template_unreadable details=\"{templateName}\"");
                    continue;
                }

                using Mat result = new Mat();
                Cv2.MatchTemplate(grayScreenshot, template, result, TemplateMatchModes.CCoeffNormed);
                Cv2.MinMaxLoc(result, out _, out double maxVal, out _, out Point maxLoc);

                // Ngưỡng tin cậy thu hoạch từ 65% trở lên
                if (maxVal < 0.65)
                {
                    Console.WriteLine($"[FSM-CS] phase=collect_resources status=skip item=\"{templateName}\" reason=below_threshold");
                    continue;
                }

                int centerX = maxLoc.X + template.Width / 2;
                int centerY = maxLoc.Y + template.Height / 2;
                Console.WriteLine($"[FSM-CS] phase=collect_resources status=success item=\"{templateName}\"");
                _adb.Tap(centerX, centerY);
                if (InterruptibleSleep(500, _cts?.Token ?? CancellationToken.None))
                {
                    return true;
                }
            }

            return false;
        }

        private bool SwitchToAccount(AccountConfig account, CancellationToken token)
        {
            string previousAccount = _activeAccountName;
            Console.WriteLine($"[ACCOUNT-CS] phase=switch status=start current=\"{previousAccount}\" target=\"{account.Name}\" village={account.ProfileVillage} target_village={account.TargetVillage}");

            if (!EnsureHomeBase(maxWaitSeconds: 20))
            {
                Console.WriteLine("[ACCOUNT-CS] phase=switch status=fail reason=home_not_detected");
                return false;
            }

            if (!TapFirstVisibleTemplate(new[] { @"ui\settings_logo", "settings_logo", "game_setting" }, 0.68, GameSettingHomeRoi, out string settingsTemplate))
            {
                Console.WriteLine("[ACCOUNT-CS] phase=switch status=fail step=open_settings reason=settings_button_not_found");
                return false;
            }
            Console.WriteLine($"[ACCOUNT-CS] phase=switch status=pending step=open_settings template=\"{settingsTemplate}\"");
            if (InterruptibleSleep(1200, token)) return false;

            if (!TapFirstVisibleTemplate(new[] { @"ui\supercell_ID", "supercell_ID" }, 0.68, null, out string supercellTemplate))
            {
                Console.WriteLine("[ACCOUNT-CS] phase=switch status=fail step=open_supercell_id reason=template_not_found");
                _adb.ExecuteShell("input keyevent KEYCODE_BACK");
                return false;
            }
            Console.WriteLine($"[ACCOUNT-CS] phase=switch status=pending step=open_supercell_id template=\"{supercellTemplate}\"");
            if (InterruptibleSleep(1800, token)) return false;

            if (!TapFirstVisibleTemplate(new[] { @"ui\switch_button", "switch_button", @"ui\icon_switch", "icon_switch" }, 0.68, null, out string switchTemplate))
            {
                Console.WriteLine("[ACCOUNT-CS] phase=switch status=fail step=open_switch_account reason=template_not_found");
                _adb.ExecuteShell("input keyevent KEYCODE_BACK");
                return false;
            }
            Console.WriteLine($"[ACCOUNT-CS] phase=switch status=pending step=open_switch_account template=\"{switchTemplate}\"");
            if (InterruptibleSleep(1800, token)) return false;

            if (!TapAccountTemplate(account, out double accountScore))
            {
                Console.WriteLine($"[ACCOUNT-CS] phase=switch status=pending step=show_all_accounts account=\"{account.Name}\"");
                TryShowAllAccounts(token);
            }

            if (!TapAccountTemplate(account, out accountScore))
            {
                Console.WriteLine($"[ACCOUNT-CS] phase=switch status=fail step=select_account reason=account_template_not_found account=\"{account.Name}\" template=\"{account.TemplatePath}\"");
                _adb.ExecuteShell("input keyevent KEYCODE_BACK");
                return false;
            }
            Console.WriteLine($"[ACCOUNT-CS] phase=switch status=pending step=select_account account=\"{account.Name}\" score={accountScore:F2}");
            if (InterruptibleSleep(2500, token)) return false;

            TapFirstVisibleTemplate(new[] { @"ui\play_button", "play_button", @"ui\open_button", "open_button", @"ui\open_button_2", "open_button_2" }, 0.66, null, out _, tap: true);
            InterruptibleSleep(5000, token);

            bool loaded = EnsureHomeBase(maxWaitSeconds: 45);
            if (loaded)
            {
                _activeAccountName = account.Name;
                if (!string.IsNullOrEmpty(account.ConfigPreset))
                {
                    ApplyPresetToProfile(account.ProfileVillage, account.ConfigPreset);
                }
            }
            Console.WriteLine($"[ACCOUNT-CS] phase=switch status={(loaded ? "success" : "fail")} current=\"{previousAccount}\" target=\"{account.Name}\" village={account.ProfileVillage}");
            return loaded;
        }

        private static void ApplyPresetToProfile(int villageId, string presetIdOrName)
        {
            if (string.IsNullOrEmpty(presetIdOrName)) return;

            string userData = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SimpliMixi");
            string presetsPath = Path.Combine(userData, "Config", "presets.json");
            if (!File.Exists(presetsPath)) return;

            try
            {
                string presetsJson = File.ReadAllText(presetsPath);
                var presetsNode = JsonNode.Parse(presetsJson) as JsonArray;
                if (presetsNode == null) return;

                JsonObject? targetPresetConfig = null;
                foreach (var node in presetsNode)
                {
                    if (node is JsonObject presetObj)
                    {
                        string? id = presetObj["id"]?.ToString();
                        string? name = presetObj["name"]?.ToString();
                        if (string.Equals(id, presetIdOrName, StringComparison.OrdinalIgnoreCase) ||
                            string.Equals(name, presetIdOrName, StringComparison.OrdinalIgnoreCase))
                        {
                            targetPresetConfig = presetObj["config"] as JsonObject;
                            break;
                        }
                    }
                }

                if (targetPresetConfig == null) return;

                string profilePath = Path.Combine(userData, "profiles", $"Village_{villageId}.json");
                JsonObject profile;
                if (File.Exists(profilePath))
                {
                    string profileJson = File.ReadAllText(profilePath);
                    profile = JsonNode.Parse(profileJson) as JsonObject ?? new JsonObject();
                }
                else
                {
                    profile = new JsonObject();
                }

                foreach (var kvp in targetPresetConfig)
                {
                    var clonedValue = kvp.Value?.DeepClone();
                    profile[kvp.Key] = clonedValue;
                }

                Directory.CreateDirectory(Path.GetDirectoryName(profilePath)!);
                var options = new JsonSerializerOptions { WriteIndented = true };
                File.WriteAllText(profilePath, profile.ToJsonString(options));
                Console.WriteLine($"[ACCOUNT-CS] Successfully applied preset '{presetIdOrName}' to profile Village_{villageId}.json");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ACCOUNT-CS] Error applying preset: {ex.Message}");
            }
        }

        private void TryShowAllAccounts(CancellationToken token)
        {
            if (TapFirstVisibleTemplate(new[] { @"ui\account_counter_2", "account_counter_2", @"ui\account_counter", "account_counter" }, 0.66, null, out string counterTemplate))
            {
                Console.WriteLine($"[ACCOUNT-CS] phase=switch status=pending step=show_all_accounts template=\"{counterTemplate}\"");
                InterruptibleSleep(1200, token);
                return;
            }

            _adb.Swipe(820, 720, 820, 260, 450);
            InterruptibleSleep(700, token);
        }

        private bool TapAccountTemplate(AccountConfig account, out double score)
        {
            score = 0;
            if (string.IsNullOrWhiteSpace(account.TemplatePath))
            {
                return false;
            }

            using Mat? screenshot = _adb.TakeScreenshot();
            if (screenshot == null || screenshot.Empty())
            {
                return false;
            }

            string? templatePath = ResolveAccountTemplatePath(account.TemplatePath);
            if (templatePath == null)
            {
                Console.WriteLine($"[ACCOUNT-CS] phase=switch status=fail reason=template_file_missing template=\"{account.TemplatePath}\"");
                return false;
            }

            using Mat template = Cv2.ImRead(templatePath, ImreadModes.Grayscale);
            if (template.Empty())
            {
                Console.WriteLine($"[ACCOUNT-CS] phase=switch status=fail reason=template_unreadable template=\"{templatePath}\"");
                return false;
            }

            using Mat gray = new Mat();
            Cv2.CvtColor(screenshot, gray, ColorConversionCodes.BGR2GRAY);
            if (gray.Width < template.Width || gray.Height < template.Height)
            {
                return false;
            }

            using Mat result = new Mat();
            Cv2.MatchTemplate(gray, template, result, TemplateMatchModes.CCoeffNormed);
            Cv2.MinMaxLoc(result, out _, out score, out _, out Point maxLoc);
            if (score < 0.70)
            {
                return false;
            }

            int centerX = maxLoc.X + template.Width / 2;
            int centerY = maxLoc.Y + template.Height / 2;
            _adb.Tap(centerX, centerY);
            return true;
        }

        private string? ResolveAccountTemplatePath(string templatePath)
        {
            string trimmed = templatePath.Trim();
            string[] candidates = Path.IsPathRooted(trimmed)
                ? new[] { trimmed }
                : new[]
                {
                    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SimpliMixi", "account_templates", trimmed),
                    Path.Combine(Directory.GetCurrentDirectory(), trimmed),
                    Path.Combine(AppContext.BaseDirectory, trimmed),
                    Path.Combine(_templatesPath, "accounts", trimmed),
                    Path.Combine(_templatesPath, trimmed)
                };

            return candidates.FirstOrDefault(File.Exists);
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            Stop();
            _pauseEvent.Dispose();
            _cts?.Dispose();
            _vision.Dispose();
        }
    }
}
