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
using static CvAut.ConfigManager;

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
    internal partial class CVAutomationFramework : IAutomationRunner
    {
        private readonly ADBHelper _adb;
        private readonly VisionEngine _vision;
        private readonly Training _training;
        private Attacks _attacks;
        private readonly WallUpdater _wallUpdater;
        private readonly BuilderBaseNavigator _builderBaseNavigator;
        private readonly BuilderBaseResources _builderBaseResources;
        private readonly BuilderBaseReport _builderBaseReport;
        private readonly BuilderBaseArmyManager _builderBaseArmyManager;
        private readonly BuilderBaseAttacks _builderBaseAttacks;
        private readonly BuilderBaseClockTower _builderBaseClockTower;
        private readonly BuilderBaseWallUpdater _builderBaseWallUpdater;
        private readonly BuilderBaseMaintenance _builderBaseMaintenance;
        private readonly Random _builderBaseRandom = new();
        private readonly string _templatesPath;
        private readonly string _configPath;

        // Extracted services
        private readonly ConfigService _configService;
        private readonly StatsRepository _stats;
        private readonly PopupHandlerService _popups;
        private readonly ZoomService _zoom;
        private readonly AccountSwitcher _accounts;

        private CancellationTokenSource? _cts;
        private Task? _workerTask;
        private readonly ManualResetEvent _pauseEvent = new(true);
        private volatile bool _isRunning;
        private int _cycleCount;
        private int _currentVillageIdx = 1;
        private volatile bool _fastAttackQueued;
        private bool _disposed;
        private DateTime _sessionStartedAt;
        private DateTime? _pauseStartedAt;
        private TimeSpan _pausedDuration = TimeSpan.Zero;
        private int _sessionBattlesCompleted;
        private static bool s_loggedBuilderBaseAssetAudit;

        public JsonElement Config => _configService.Config;

        /// <summary>
        /// Public constructor (backward compatible). Creates dependencies internally.
        /// </summary>
        public CVAutomationFramework(string configPath = "Config/test_config.json")
            : this(CreateServices(configPath))
        {
        }

        private CVAutomationFramework((ConfigService Config, ADBHelper Adb, VisionEngine Vision, string TemplatesPath) services)
            : this(services.Config, services.Adb, services.Vision, services.TemplatesPath)
        {
        }

        private CVAutomationFramework(ConfigService configService, ADBHelper adb, VisionEngine vision, string templatesPath)
            : this("", configService, adb, vision, templatesPath,
                  new StatsRepository(adb, vision, templatesPath),
                  new PopupHandlerService(adb, vision, templatesPath),
                  new ZoomService(adb),
                  new AccountSwitcher(adb, vision, templatesPath, maxWait => true))
        {
        }

        /// <summary>
        /// Internal constructor accepting pre-built services (used by BotOrchestrator).
        /// </summary>
        internal CVAutomationFramework(string configPath, ConfigService configService,
            ADBHelper adb, VisionEngine vision, string templatesPath)
            : this(configPath, configService, adb, vision, templatesPath,
                  new StatsRepository(adb, vision, templatesPath),
                  new PopupHandlerService(adb, vision, templatesPath),
                  new ZoomService(adb),
                  new AccountSwitcher(adb, vision, templatesPath, maxWait => true))
        {
        }

        private CVAutomationFramework(string configPath, ConfigService configService,
            ADBHelper adb, VisionEngine vision, string templatesPath,
            StatsRepository stats, PopupHandlerService popups, ZoomService zoom, AccountSwitcher accounts)
        {
            _configPath = configPath;
            _configService = configService;
            _stats = stats;
            _popups = popups;
            _zoom = zoom;
            _accounts = accounts;

            var cfg = configService.Config;
            var devConfig = cfg.GetProperty("device_connection");
            string host = devConfig.GetProperty("host").GetString() ?? "127.0.0.1";
            int port = devConfig.GetProperty("port").GetInt32();
            string? serial = devConfig.TryGetProperty("serial", out JsonElement serialElement) ? serialElement.GetString() : null;

            _adb = adb;
            _adb.BeforeInputAction = null;
            _templatesPath = templatesPath;
            _vision = vision;
            _training = new Training(_adb, _templatesPath, _vision);
            _attacks = new Attacks(_adb, _vision, _templatesPath,
                ConfigManager.ReadAttackDelayConfig(cfg), ConfigManager.ReadAttackCoordinateConfig(cfg));
            _wallUpdater = new WallUpdater(_adb, _vision, _templatesPath);
            _builderBaseNavigator = new BuilderBaseNavigator(_adb, _vision);
            _builderBaseResources = new BuilderBaseResources(_adb, _vision, _builderBaseNavigator);
            _builderBaseReport = new BuilderBaseReport(_adb, _vision, _builderBaseNavigator);
            _builderBaseArmyManager = new BuilderBaseArmyManager(_adb, _vision, _builderBaseNavigator);
            _builderBaseAttacks = new BuilderBaseAttacks(_adb, _vision, _builderBaseNavigator);
            _builderBaseClockTower = new BuilderBaseClockTower(_adb, _vision, _builderBaseNavigator);
            _builderBaseWallUpdater = new BuilderBaseWallUpdater(_adb, _vision, _builderBaseNavigator);
            _builderBaseMaintenance = new BuilderBaseMaintenance(_adb, _vision, _builderBaseNavigator, _templatesPath);

            Console.WriteLine("[FSM-CS] phase=init status=success details=\"automation_core_initialized\"");
        }

        private static (ConfigService, ADBHelper, VisionEngine, string) CreateServices(string configPath)
        {
            var config = new ConfigService(configPath);
            var cfg = config.Config;
            var devConfig = cfg.GetProperty("device_connection");
            string host = devConfig.GetProperty("host").GetString() ?? "127.0.0.1";
            int port = devConfig.GetProperty("port").GetInt32();
            string? serial = devConfig.TryGetProperty("serial", out JsonElement serialElement) ? serialElement.GetString() : null;
            var adb = new ADBHelper(host, port, serial);
            string templatesPath = Path.Combine(AppContext.BaseDirectory, "assets", "Templates");
            var vision = new VisionEngine(templatesPath);
            return (config, adb, vision, templatesPath);
        }

        private static (ConfigService, ADBHelper, VisionEngine, string) CreateServicesFromConfig(ConfigService config, ADBHelper adb, string templatesPath)
        {
            return (config, adb, new VisionEngine(templatesPath), templatesPath);
        }

        private void LoadConfig(string path) => _configService.Reload();

        public void Start()
        {
            if (_isRunning) return;

            _configService.Reload();
            _attacks = new Attacks(_adb, _vision, _templatesPath, ConfigManager.ReadAttackDelayConfig(Config), ConfigManager.ReadAttackCoordinateConfig(Config));

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
                        InterruptibleSleep(_fastAttackQueued ? AutomationThresholds.FastAttackCycleDelayMs : AutomationThresholds.NormalCycleDelayMs, token);
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
                int remaining = Math.Min(500, Math.Max(1, (int)(end - DateTime.Now).TotalMilliseconds));
                if (ThreadingUtil.InterruptibleSleep(remaining, token) || !_isRunning)
                    return true;
                _popups.HandleBlockingConnectionPopup("[WARN] Connection popup during wait → recover");
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

            // 1. Xác thực màn hình game đã tải xong TRƯỚC. Khi mới vào game màn hình
            // còn đang tải (screenshot trắng) nên zoom sẽ vô tác dụng — phải chờ render xong.
            WaitIfPaused(token);
            if (CheckStop(token)) return;

            bool nightVillageMode = IsNightVillageMode(cfg, _currentVillageIdx);
            if (!nightVillageMode)
            {
                Console.WriteLine("[FSM-CS] phase=home_check status=start");
                bool isLoaded = EnsureHomeBase(fastAttackOnly ? 8 : 50);
                if (!isLoaded)
                {
                    Console.WriteLine("[FSM-CS ERROR] phase=cycle status=skip reason=home_not_detected");
                    return;
                }
            }

            if (nightVillageMode)
            {
                OneBuilderBaseCycle(cfg, token);
                return;
            }

            MainVillageConfig mainConfig = GetMainVillageConfig(cfg, _currentVillageIdx);

            // 2. Kéo camera rộng sau khi Home Base đã render để thấy toàn bộ mỏ tài nguyên.
            WaitIfPaused(token);
            if (CheckStop(token)) return;

            Console.WriteLine("[FSM-CS] phase=cycle status=pending step=1 details=\"initial_zoomout\"");
            _zoom.ZoomOut();

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

                if (_popups.HandleBlockingConnectionPopup("[WARN] Connection lost → recovering"))
                {
                    return;
                }

                // 4. Huấn luyện lính theo cấu hình
                WaitIfPaused(token);
                if (CheckStop(token)) return;

                if (_popups.HandleBlockingConnectionPopup("[WARN] Connection lost before training → recovering"))
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

                if (_popups.HandleBlockingConnectionPopup("[WARN] Connection lost → recovering"))
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

                if (_popups.HandleBlockingConnectionPopup("[WARN] Connection lost during evaluation → recovering"))
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
                    // Quét số sao đạt được và lượng tài nguyên thực tế nhận về
                    int starsGot = _stats.GetStarsFromScreen();
                    var gained = _stats.GainResources(starsGot);
                    Console.WriteLine($"[FSM-CS] phase=battle_stats stars={starsGot} gold={gained.Gold} elixir={gained.Elixir} dark_elixir={gained.DarkElixir} status=success");

                    // Cập nhật số liệu thống kê phiên chơi
                    if (GetBoolOrDefault(cfg, "enable_stats", false))
                    {
                        _stats.UpdateStats(_currentVillageIdx, starsGot, gained);
                    }
                    else
                    {
                        Console.WriteLine("[FSM-CS] phase=battle_stats status=skip reason=stats_disabled");
                    }
                    _sessionBattlesCompleted++;

                    // Bấm nút quay trở về Làng chính
                    returnedHome = ReturnHome();
                    _fastAttackQueued = returnedHome;

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

        private void OneBuilderBaseCycle(JsonElement cfg, CancellationToken token)
        {
            WaitIfPaused(token);
            if (CheckStop(token)) return;

            Console.WriteLine($"[DEBUG][BB-CS] phase=cycle status=start village={_currentVillageIdx}");

            if (!EnsureBuilderBaseEntry(token))
            {
                Console.WriteLine("[BB-CS] phase=cycle status=fail reason=switch_to_builder_base_failed");
                return;
            }

            WaitIfPaused(token);
            if (CheckStop(token)) return;

            JsonElement night = GetObjectOrDefault(cfg, "night_village");
            string farmMode = GetStringOrDefault(night, "farm_mode", "trophy");
            bool forceAttackForClanGames = GetBoolOrDefault(night, "force_attack_for_clan_games", false);
            bool trophyRangeEnabled = GetBoolOrDefault(night, "trophy_range_enabled", false);
            int minTrophy = Math.Clamp(GetIntOrDefault(night, "min_cups", 0), 0, 10000);
            int maxTrophy = Math.Clamp(GetIntOrDefault(night, "max_cups", 5000), 0, 10000);
            bool haltOnGoldFull = GetBoolOrDefault(night, "halt_on_gold_full", false);
            bool haltOnElixirFull = GetBoolOrDefault(night, "halt_on_elixir_full", false);
            bool upgradeWall = GetBoolOrDefault(night, "upgrade_wall", false);
            bool enableAttack = GetBoolOrDefault(night, "enable_attack", true);
            bool boostClockTower = GetBoolOrDefault(night, "boost_clock_tower", false);
            int maxAttacksPerCycle = Math.Clamp(GetIntOrDefault(night, "max_attacks_per_cycle", 20), 1, 100);
            var armyOptions = new BuilderBaseArmyOptions(
                Enabled: true,
                Formation: GetStringOrDefault(night, "army_formation", "auto"),
                RequireHero: GetBoolOrDefault(night, "wait_for_heroes", true));
            var battleOptions = new BuilderBaseBattleOptions(
                DropOrder: GetStringOrDefault(night, "drop_order", "BattleMachine|BattleCopter|BoxerGiant|DropShip|HogGlider|Bomber|SuperPekka|PowerPekka|BabyDragon|CannonCart|ElectrofireWizard|NightWitch|RagedBarbarian|BetaMinion|SneakyArcher"),
                UseCustomDropOrder: GetBoolOrDefault(night, "custom_drop_order_enabled", false),
                NextTroopDelayMs: 600,
                SameTroopDelayMs: 180,
                HandleBomber: GetBoolOrDefault(night, "handle_bomber", true));
            var maintenanceOptions = new BuilderBaseMaintenanceOptions(
                SuggestedUpgrades: GetBoolOrDefault(night, "suggested_upgrades", false),
                StarLaboratory: GetBoolOrDefault(night, "star_laboratory", false),
                UpgradeBattleMachine: GetBoolOrDefault(night, "upgrade_battle_machine", false),
                UpgradeBattleCopter: GetBoolOrDefault(night, "upgrade_battle_copter", false),
                PlaceNewBuildings: GetBoolOrDefault(night, "place_new_buildings", false),
                IgnoreGoldUpgrades: GetBoolOrDefault(night, "ignore_gold_upgrades", false),
                IgnoreElixirUpgrades: GetBoolOrDefault(night, "ignore_elixir_upgrades", false),
                IgnoreHallUpgrades: GetBoolOrDefault(night, "ignore_hall_upgrades", true),
                IgnoreWallUpgrades: GetBoolOrDefault(night, "ignore_wall_upgrades", true),
                StarLaboratoryTroop: GetStringOrDefault(night, "star_laboratory_troop", "auto"),
                VillageIdx: _currentVillageIdx,
                StarLaboratoryDebugScreenshots: GetBoolOrDefault(night, "star_laboratory_debug_screenshots", GetBoolOrDefault(night, "debug_screenshots", false)));

            LogBuilderBaseBaselineAssetAudit(armyOptions, battleOptions, maintenanceOptions, boostClockTower, upgradeWall);

            Console.WriteLine($"[BB-CS] phase=cycle status=pending step=collect upgrade_wall={upgradeWall} enable_attack={enableAttack} boost_clock_tower={boostClockTower} trophy_range={trophyRangeEnabled} min_trophy={minTrophy} max_trophy={maxTrophy} halt_gold_full={haltOnGoldFull} halt_elixir_full={haltOnElixirFull} force_clan_games={forceAttackForClanGames} suggested_upgrades={maintenanceOptions.SuggestedUpgrades} star_laboratory={maintenanceOptions.StarLaboratory} hero_upgrades={maintenanceOptions.UpgradeBattleMachine || maintenanceOptions.UpgradeBattleCopter} army_management={armyOptions.Enabled} army_formation={armyOptions.Formation}  custom_drop_order={battleOptions.UseCustomDropOrder}");
            BuilderBaseReportSnapshot beforeReport = _builderBaseReport.Read();
            Console.WriteLine($"[BB-CS] phase=cycle status=pending step=report_before gold={beforeReport.Gold} elixir={beforeReport.Elixir} trophy={beforeReport.Trophy} free_builders={beforeReport.FreeBuilders} total_builders={beforeReport.TotalBuilders} builder_hall_level={beforeReport.BuilderHallLevel} loot_available={beforeReport.LootAvailable} remaining_stars={beforeReport.RemainingStars} max_stars={beforeReport.MaxStars} gold_storage_full={beforeReport.GoldStorageFull} elixir_storage_full={beforeReport.ElixirStorageFull}");
            int collected = _builderBaseResources.Collect(token);
            Console.WriteLine($"[BB-CS] phase=cycle status=pending step=collect_resources taps={collected}");

            if (boostClockTower)
            {
                bool boosted = _builderBaseClockTower.TryBoost(token);
                Console.WriteLine($"[BB-CS] phase=cycle status=pending step=clock_tower_boost success={boosted}");
            }
            if (upgradeWall && !CheckStop(token))
            {
                bool wallUpgraded = _builderBaseWallUpdater.TryUpgradeOne(token);
                Console.WriteLine($"[BB-CS] phase=cycle status=pending step=wall_upgrade_done success={wallUpgraded}");
                _stats.UpdateWallStats(_currentVillageIdx, wallUpgraded ? 1 : 0);
            }

            if (!CheckStop(token)
                && (maintenanceOptions.SuggestedUpgrades || maintenanceOptions.StarLaboratory
                    || maintenanceOptions.UpgradeBattleMachine || maintenanceOptions.UpgradeBattleCopter))
            {
                Console.WriteLine("[BB-CS] phase=cycle status=pending step=maintenance_skipped reason=temporary_scope_attack_and_wall_only");
            }

            if (!CheckStop(token))
            {
                BuilderBaseReportSnapshot afterMaintenanceReport = _builderBaseReport.Read();
                Console.WriteLine($"[BB-CS] phase=cycle status=pending step=report_after_maintenance gold={afterMaintenanceReport.Gold} elixir={afterMaintenanceReport.Elixir} trophy={afterMaintenanceReport.Trophy} free_builders={afterMaintenanceReport.FreeBuilders} total_builders={afterMaintenanceReport.TotalBuilders} builder_hall_level={afterMaintenanceReport.BuilderHallLevel} loot_available={afterMaintenanceReport.LootAvailable} remaining_stars={afterMaintenanceReport.RemainingStars} max_stars={afterMaintenanceReport.MaxStars} gold_storage_full={afterMaintenanceReport.GoldStorageFull} elixir_storage_full={afterMaintenanceReport.ElixirStorageFull}");
            }

            if (enableAttack && !CheckStop(token))
            {
                int completedAttacks = 0;
                int attempts = 0;
                int consecutiveFailures = 0;
                const int maxConsecutiveFailures = 3;
                for (int attack = 1; attack <= maxAttacksPerCycle && !CheckStop(token); attack++)
                {
                    BuilderBaseReportSnapshot attackReport;
                    if (forceAttackForClanGames)
                    {
                        attackReport = _builderBaseReport.Read();
                        Console.WriteLine($"[BB-CS] phase=prepare_attack status=force_clan_games index={attack} loot_available={attackReport.LootAvailable} remaining_stars={attackReport.RemainingStars} max_stars={attackReport.MaxStars} gold_storage_full={attackReport.GoldStorageFull} elixir_storage_full={attackReport.ElixirStorageFull}");
                    }
                    else
                    {
                        attackReport = ReadDebouncedReport(farmMode, trophyRangeEnabled, minTrophy, maxTrophy, haltOnGoldFull, haltOnElixirFull, token, out bool shouldStop, out string stopReason);
                        if (shouldStop || CheckStop(token))
                        {
                            if (shouldStop)
                                Console.WriteLine($"[BB-CS] phase=prepare_attack status=skip index={attack} reason={stopReason} attack_avail={attackReport.AttackAvailable} attack_known={attackReport.AttackAvailabilityKnown} star_bonus_avail={attackReport.StarBonusAvailable} remaining_stars={attackReport.RemainingStars} max_stars={attackReport.MaxStars} trophy={attackReport.Trophy} min={minTrophy} max={maxTrophy} gold_storage_full={attackReport.GoldStorageFull} elixir_storage_full={attackReport.ElixirStorageFull} report_reliable={attackReport.Reliable}");
                            break;
                        }
                    }

                    attempts++;

                    bool isDropTrophy = farmMode.Equals("drop_trophy", StringComparison.OrdinalIgnoreCase);
                    if (!isDropTrophy)
                    {
                        if (!_builderBaseArmyManager.EnsureReadyForAttack(armyOptions, token))
                        {
                            Console.WriteLine($"[BB-CS] phase=cycle status=pending step=army_not_ready index={attack}");
                            break;
                        }
                    }

                    BuilderBaseBattleResult battleResult = isDropTrophy
                        ? _builderBaseAttacks.RunDropTrophyAttack(token)
                        : _builderBaseAttacks.RunSingleAttack(battleOptions, token);
                    Console.WriteLine($"[BB-CS] phase=cycle status=pending step=attack_done index={attack} success={battleResult.ReturnedHome} damage={battleResult.Damage} stars={battleResult.Stars} stage2={battleResult.Stage2Entered}");
                    bool counted = battleResult.ReturnedHome;
                    if (counted)
                    {
                        _stats.UpdateBuilderBaseAttackStats(_currentVillageIdx, battleResult);
                        completedAttacks++;
                        consecutiveFailures = 0;
                    }
                    else
                    {
                        consecutiveFailures++;
                        Console.WriteLine($"[BB-CS] phase=cycle status=pending step=attack_not_counted index={attack} reason=abort_or_return_home_failed consecutive_failures={consecutiveFailures} max_allowed={maxConsecutiveFailures}");
                    }

                    if (!PostBuilderBaseAttackMaintenance(maintenanceOptions, token, battleResult.ReturnedHome))
                    {
                        Console.WriteLine($"[BB-CS] phase=cycle status=fail step=post_attack_maintenance index={attack} reason=builder_base_recovery_failed");
                        break;
                    }

                    if (consecutiveFailures >= maxConsecutiveFailures)
                    {
                        Console.WriteLine($"[BB-CS] phase=cycle status=pending step=attack_stop reason=consecutive_attack_failures limit={maxConsecutiveFailures}");
                        break;
                    }
                }
                if (attempts >= maxAttacksPerCycle && !CheckStop(token))
                {
                    Console.WriteLine($"[BB-CS] phase=cycle status=pending step=attack_stop reason=max_attacks_per_cycle limit={maxAttacksPerCycle}");
                }
                Console.WriteLine($"[BB-CS] phase=cycle status=pending step=attacks_complete completed={completedAttacks} attempts={attempts}");
            }

            _cycleCount++;
            Console.WriteLine($"[BB-CS] phase=cycle status=success village={_currentVillageIdx}");
        }

        internal BuilderBaseReportSnapshot ReadDebouncedReport(
            string farmMode,
            bool trophyRangeEnabled,
            int minTrophy,
            int maxTrophy,
            bool haltOnGoldFull,
            bool haltOnElixirFull,
            CancellationToken token,
            out bool shouldStop,
            out string stopReason)
        {
            return ReadDebouncedReport(
                () => _builderBaseReport.Read(),
                farmMode,
                trophyRangeEnabled,
                minTrophy,
                maxTrophy,
                haltOnGoldFull,
                haltOnElixirFull,
                token,
                (ms, t) => InterruptibleSleep(ms, t),
                out shouldStop,
                out stopReason);
        }

        internal static BuilderBaseReportSnapshot ReadDebouncedReport(
            Func<BuilderBaseReportSnapshot> readReport,
            string farmMode,
            bool trophyRangeEnabled,
            int minTrophy,
            int maxTrophy,
            bool haltOnGoldFull,
            bool haltOnElixirFull,
            CancellationToken token,
            Func<int, CancellationToken, bool>? sleepFunc,
            out bool shouldStop,
            out string stopReason)
        {
            shouldStop = false;
            stopReason = "none";

            if (token.IsCancellationRequested)
            {
                return BuilderBaseReportSnapshot.UnknownSnapshot();
            }

            BuilderBaseReportSnapshot report = null!;

            for (int check = 1; check <= 2; check++)
            {
                report = readReport();

                if (!ShouldStopBuilderBaseAttacks(farmMode, report, trophyRangeEnabled, minTrophy, maxTrophy, haltOnGoldFull, haltOnElixirFull, out stopReason))
                {
                    shouldStop = false;
                    return report;
                }

                bool needsConfirmation = stopReason == "loot_exhausted" || stopReason == "star_bonus_completed";
                if (!needsConfirmation || check == 2)
                {
                    shouldStop = true;
                    return report;
                }

                Console.WriteLine($"[BB-CS] phase=prepare_attack status=pending reason={stopReason} debouncing={check}/2");
                if (sleepFunc != null && sleepFunc(500, token))
                {
                    shouldStop = false;
                    return report;
                }
            }

            return report;
        }

        internal static bool ShouldStopBuilderBaseAttacks(
            string farmMode,
            BuilderBaseReportSnapshot report,
            bool trophyRangeEnabled,
            int minTrophy,
            int maxTrophy,
            bool haltOnGoldFull,
            bool haltOnElixirFull,
            out string reason)
        {
            if (trophyRangeEnabled && report.Trophy > 0)
            {
                if (farmMode.Equals("drop_trophy", StringComparison.OrdinalIgnoreCase))
                {
                    if (report.Trophy <= minTrophy)
                    {
                        reason = "trophy_reached_min";
                        return true;
                    }
                }
                else
                {
                    if (report.Trophy >= maxTrophy)
                    {
                        reason = "trophy_reached_max";
                        return true;
                    }
                }
            }

            if ((haltOnGoldFull && report.GoldStorageFull) || (haltOnElixirFull && report.ElixirStorageFull))
            {
                reason = "storage_full";
                return true;
            }

            bool isDropTrophy = farmMode.Equals("drop_trophy", StringComparison.OrdinalIgnoreCase);
            bool isTrophy = farmMode.Equals("trophy", StringComparison.OrdinalIgnoreCase) || farmMode.Equals("auto", StringComparison.OrdinalIgnoreCase);

            if (!isDropTrophy && !isTrophy)
            {
                if (farmMode.Equals("star_bonus", StringComparison.OrdinalIgnoreCase)
                    && report.Reliable && report.StarBonusKnown && !report.StarBonusAvailable)
                {
                    reason = "star_bonus_completed";
                    return true;
                }

                if (report.Reliable && report.AttackAvailabilityKnown && !report.AttackAvailable)
                {
                    reason = "loot_exhausted";
                    return true;
                }
            }

            reason = "none";
            return false;
        }

        private bool PostBuilderBaseAttackMaintenance(BuilderBaseMaintenanceOptions maintenanceOptions, CancellationToken token, bool returnedHome)
        {
            Console.WriteLine($"[BB-CS] phase=post_attack status=start returned_home={returnedHome}");
            DismissBuilderBasePopups(token);

            if (!_builderBaseNavigator.IsOnBuilderBase())
            {
                Console.WriteLine("[BB-CS] phase=post_attack status=pending step=recover_builder_base reason=not_on_builder_base");
                if (!EnsureBuilderBaseEntry(token)) return false;
            }

            DismissBuilderBasePopups(token);
            _builderBaseNavigator.ZoomOutApprox(token);
            if (InterruptibleSleep(700, token)) return false;

            

            DismissBuilderBasePopups(token);
            _builderBaseNavigator.ZoomOutApprox(token);
            bool ok = _builderBaseNavigator.IsOnBuilderBase();
            Console.WriteLine($"[BB-CS] phase=post_attack status={(ok ? "success" : "fail")} step=verify_builder_base");
            return ok;
        }

        private void LogBuilderBaseBaselineAssetAudit(
            BuilderBaseArmyOptions armyOptions,
            BuilderBaseBattleOptions battleOptions,
            BuilderBaseMaintenanceOptions maintenanceOptions,
            bool boostClockTower,
            bool upgradeWall)
        {
            if (s_loggedBuilderBaseAssetAudit) return;
            s_loggedBuilderBaseAssetAudit = true;

            var required = new SortedSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                @"ui\switch_builder",
                @"ui\game_setting",
                @"ui\shop",
                @"ui\builder_available",
                @"ui\x_night",
                @"resources\gold_collector",
                @"resources\elixir_collector",
                @"resources\collect",
                @"ui\attack_button",
                @"ui\start_battle",
                @"ui\return_home",
                @"ui\surrender_button"
            };

            if (armyOptions.Enabled || battleOptions.UseCustomDropOrder)
            {
                required.UnionWith(new[]
                {
                    @"troops\builder_base\raged_barbarian",
                    @"troops\builder_base\raged_barbarian_click",
                    @"troops\builder_base\power_pekka",
                    @"troops\builder_base\power_pekka_click",
                    @"heroes\battle_machine",
                    @"heroes\battle_machine_a"
                });
            }

            if (boostClockTower)
            {
                required.UnionWith(new[] { @"ui\clock_available", @"ui\free_boost", @"ui\boost" });
            }

            if (upgradeWall)
            {
                required.UnionWith(new[] { @"walls\wall", @"ui\icon_wall" });
            }

            if (maintenanceOptions.SuggestedUpgrades || maintenanceOptions.UpgradeBattleMachine || maintenanceOptions.UpgradeBattleCopter)
            {
                required.UnionWith(new[] { @"ui\builder_available", @"ui\open_upgrade", @"ui\icon_up", @"resources\gold", @"resources\elixir" });
            }

            if (maintenanceOptions.StarLaboratory)
            {
                required.UnionWith(new[] { @"builder_base\star_laboratory", @"ui\laboratory", @"ui\research" });
            }

            string[] missing = required.Where(template => !TemplateAssetLoader.Exists(_templatesPath, template)).ToArray();
            if (missing.Length == 0)
            {
                Console.WriteLine($"[BB-CS] phase=asset_audit status=success checked={required.Count}");
                return;
            }

            Console.WriteLine($"[BB-CS WARNING] phase=asset_audit status=partial checked={required.Count} missing={missing.Length} templates=\"{string.Join(",", missing)}\" action=skip_template_dependent_steps");
        }

        private void TryUpgradeWallsFromHome(JsonElement cfg, CancellationToken token, string phase)
        {
            var wallConfig = GetWallUpgradeConfig(cfg, _currentVillageIdx);
            Console.WriteLine($"[WALL DECISION] phase={phase} cycle={_cycleCount} enabled={wallConfig.Enabled} home=true gold_start={wallConfig.GoldThreshold:N0} elixir_start={wallConfig.ElixirThreshold:N0} gold_reserve={wallConfig.GoldReserve:N0} elixir_reserve={wallConfig.ElixirReserve:N0} batch_limit={wallConfig.BatchLimit} wall_debug_screenshots={wallConfig.DebugScreenshots} status=check");

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
                _stats.UpdateWallStats(_currentVillageIdx, upgradedWalls);
            }
        }

        /// <summary>
        /// Vòng lặp chính xử lý luồng bot chạy vô hạn.
        /// Hỗ trợ luân chuyển chơi giữa nhiều tài khoản khác nhau định kỳ (Switch Account) nếu cấu hình multi-account bật.
        /// </summary>
        private void BotLoop(CancellationToken token)
        {
            Console.WriteLine("[DEBUG][FSM-CS] phase=worker_loop status=start");

            JsonElement multiConfig = GetObjectOrDefault(Config, "multi_account");
            bool enableMulti = GetBoolOrDefault(multiConfig, "enable_multi_account", false);

            if (!enableMulti)
            {
                Console.WriteLine("[DEBUG][FSM-CS] phase=worker_loop status=pending mode=single_account");
                _currentVillageIdx = 1;
                while (!CheckStop(token))
                {
                    OneCycle(Config, token);
                    if (CheckStop(token)) break;
                    // Nghỉ ngắt quãng giữa các chu kỳ. Nếu vừa đánh xong, delay ngắn hơn để đánh tiếp ngay
                    InterruptibleSleep(_fastAttackQueued ? AutomationThresholds.FastAttackCycleDelayMs : AutomationThresholds.NormalCycleDelayMs, token);
                }
                return;
            }

            // Chế độ chạy nhiều tài khoản (Multi Account)
            AccountConfig[] accounts = _accounts.GetConfiguredAccounts(multiConfig);
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
                    if (!_accounts.SwitchToAccount(account, token))
                    {
                        Console.WriteLine($"[FSM-CS WARNING] phase=account_switch status=fail target={idx} account=\"{account.Name}\" action=skip_account");
                        continue;
                    }
                    _wallUpdater.ResetSavedOffset();

                    DateTime slotStart = DateTime.Now;
                    int slotBattleStart = _sessionBattlesCompleted;
                    int slotClanPointStart = ConfigService.ReadClanGamesPoints(idx);
                    _cycleCount = 0;

                    // Chơi tài khoản này cho đến khi một điều kiện đổi account được kích hoạt.
                    string switchReason = "none";
                    while (!_accounts.ShouldSwitchAccount(
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
                        _sessionBattlesCompleted,
                        out switchReason) && !CheckStop(token))
                    {
                        WaitIfPaused(token);
                        OneCycle(Config, token);
                        if (CheckStop(token)) break;
                        InterruptibleSleep(_fastAttackQueued ? AutomationThresholds.FastAttackCycleDelayMs : 15000, token);
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

                if (_popups.HandleBlockingConnectionPopup("[WARN] Connection popup while waiting home → reload"))
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
            if (_vision.FindElement(screenshot, @"ui\game_setting.png", AutomationThresholds.HomeTemplateThreshold, AutomationRoiConstants.GameSettingHomeRoi, out double settingScore) is { } gs)
            {
                reason = $"game_setting score={settingScore:F3}";
                return true;
            }

            // Thử dò tìm biểu tượng nút Cửa hàng (Shop) ở góc dưới phải
            if (_vision.FindElement(screenshot, @"ui\shop.png", AutomationThresholds.HomeTemplateThreshold, null, out double shopScore) is { } shop)
            {
                reason = $"shop template at ({shop.X},{shop.Y}) score={shopScore:F3}";
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
            _popups.HandleTreasureHuntIfPresent();
            if (CheckStop(token)) return;
            _adb.Tap(272, 659); // Chọn Tìm trận đối thủ (Find Match)
            if (InterruptibleSleep(700, token)) return;
            _adb.Tap(1445, 804); // Chấp nhận phí tìm trận ban đầu
        }

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

            bool found = _vision.FindElement(screenshot, @"ui\next_button.png", AutomationThresholds.NextButtonThreshold, AutomationRoiConstants.NextButtonRoi, out _) != null;
            return found;
        }

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
                if (_vision.FindElement(screenshot, @"ui\end_battle.png", AutomationThresholds.ScoutUiThreshold, AutomationRoiConstants.ScoutUiRoi, out _) != null)
                {
                    Console.WriteLine("[SCOUT-CS] phase=scout_wait status=success details=\"ready\"");
                    return true;
                }

                // Giải tỏa nhanh popup sự kiện săn rương nếu vô tình xuất hiện cản màn hình
                if (_popups.HandleTreasureHuntIfPresent(verboseNotFound: false))
                {
                    continue;
                }

                Thread.Sleep(intervalMs);
            }

            Console.WriteLine("[SCOUT-CS WARNING] phase=scout_wait status=fail reason=timeout");
            return false;
        }

        private static MainVillageConfig GetMainVillageConfig(JsonElement cfg, int villageIdx) => ConfigService.GetMainVillageConfig(cfg, villageIdx);
        private static TrainingConfig GetTrainingConfig(JsonElement cfg, int villageIdx) => ConfigService.GetTrainingConfig(cfg, villageIdx);
        private static string GetAttackStrategy(JsonElement cfg, int villageIdx) => ConfigService.GetAttackStrategy(cfg, villageIdx);
        private static WallUpgradeConfig GetWallUpgradeConfig(JsonElement cfg, int villageIdx) => ConfigService.GetWallUpgradeConfig(cfg, villageIdx);

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

                    if (stableResultMatches >= AutomationThresholds.ResultScreenStableMatches)
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

                if (_popups.ConnectionPopupVisible(out string matchInfo, allowDialogShapeFallback: false))
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

                if ((DateTime.Now - start).TotalSeconds >= AutomationThresholds.MaxWaitBattleSeconds)
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

            if (IsActiveBattlePresent(screenshot, out double endBattleScore))
            {
                matchInfo = $"active_battle end_battle_score={endBattleScore:F2}";
                return false;
            }

            bool hasContinue = TryFindContinueButton(screenshot, out Point center, out double continueScore);
            bool hasResultMarker = _vision.FindElement(screenshot, @"ui\resources_gained.png", AutomationThresholds.ResultYouGotThreshold, AutomationRoiConstants.ResultYouGotRoi, out double markerScore) != null;

            matchInfo = $"continue score={continueScore:F2} center=({center.X},{center.Y}), result-marker score={markerScore:F2}";

            return hasContinue || hasResultMarker;
        }

        private bool IsActiveBattlePresent(Mat screenshot, out double endBattleScore)
        {
            return BattleScreenDetector.IsActiveBattlePresent(_vision, screenshot, out endBattleScore);
        }

        private bool TryFindContinueButton(Mat screenshot, out Point center, out double score)
        {
            Point? found = _vision.FindElement(screenshot, @"ui\return_home.png", AutomationThresholds.ResultContinueThreshold, AutomationRoiConstants.ResultContinueRoi, out score);
            if (found.HasValue) { center = found.Value; return true; }
            found = _vision.FindElement(screenshot, @"ui\return_home_n.png", AutomationThresholds.ResultContinueThreshold, AutomationRoiConstants.ResultContinueRoi, out score);
            if (found.HasValue) { center = found.Value; return true; }
            center = default;
            return false;
        }

        public void SaveDebugImage(Mat image, string fileName) => _stats.SaveDebugImage(image, fileName);

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
                _popups.DismissStarBonusIfPresent();

                if (DetectHomeBase(out string homeReason))
                {
                    Console.WriteLine($"[FSM-CS] phase=return_home status=success reason=home_detected details=\"{homeReason}\" attempt={attempt}");
                    Console.WriteLine("[FSM-CS] phase=return_home action=ensure_home status=success");
                    return true;
                }

                if (_popups.HandleTreasureHuntIfPresent(verboseNotFound: false))
                {
                    Console.WriteLine($"[FSM-CS] phase=return_home status=pending action=clear_treasure_hunt attempt={attempt}");
                    Thread.Sleep(1500);
                }
                else
                {
                    _popups.HandleTreasureHuntIfPresent(verboseNotFound: false);
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

        // Stats methods moved to StatsRepository

        /// <summary>
        /// Thực hiện cử chỉ thu nhỏ góc nhìn bản đồ (Zoom Out):
        /// - Đối với MEmu: Gắn kết luồng và gửi phím F3 ngầm qua PostMessage WM_KEYDOWN Win32 API.
        /// - Đối với BlueStacks: Gửi cử chỉ PinchIn đa điểm qua ADB UIAutomator2.
        /// </summary>
        public void ZoomOut() => _zoom.ZoomOut();

        /// <summary>
        /// Thu hoạch mỏ tài nguyên (Gold/Elixir/Dark Elixir Collector) trên màn hình Làng chính.
        /// Dò tìm các bong bóng icon tài nguyên lơ lửng trên mỏ và thực hiện chạm (Tap).
        /// </summary>
        private bool CollectResourcesPlaceholder()
        {
            if (_popups.HandleBlockingConnectionPopup("[WARN] Connection popup before collect → reload"))
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

                if (_popups.HandleBlockingConnectionPopup("[WARN] Connection popup during collect → reload"))
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
