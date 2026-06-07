using System;
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
    public class CVAutomationFramework : IDisposable
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
        private bool _disposed;
        private bool _handlingConnectionPopup;

        private static readonly string WritableLogsDirectory = ResolveWritableLogsDirectory();
        private const int MinSupportedWallLevel = 8;
        private const int MaxSupportedWallLevel = 17;

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
            "Another_device.png",
            "Connection_lost.png",
            "Client_error!.png",
            "rate_coc.png",
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

            _adb = new ADBHelper(host, port);

            _templatesPath = Path.Combine(AppContext.BaseDirectory, "assets", "Templates");
            _adb.BeforeInputAction = null;
            _vision = new VisionEngine(_templatesPath);
            _training = new Training(_adb, _templatesPath, _vision);
            _attacks = new Attacks(_adb, _vision);
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
                ""farming_thresholds"": {""gold_threshold"": 650000, ""elixir_threshold"": 650000, ""dark_elixir_threshold"": 1000},
                ""upgrade_wall"": false,
                ""wall_level"": 14,
                ""wall_gold_threshold"": 5000000,
                ""wall_elixir_threshold"": 5000000,
                ""enable_stats"": true,
                ""multi_account"": {""enable_multi_account"": false, ""multi_interval_mins"": 60, ""selected_villages"": [1]}
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

                // Đảm bảo BlueStacks đã được bật và mở CoC
                if (!EmulatorBootstrapper.EnsureReady(_adb, host, port, token))
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
            _pauseEvent.Reset();
            Console.WriteLine("[FSM-CS] phase=worker status=paused");
        }

        /// <summary>
        /// Tiếp tục luồng chạy bot đã tạm dừng.
        /// </summary>
        public void Resume()
        {
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
            return token.IsCancellationRequested || !_isRunning;
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

            // 1. Xác thực màn hình Làng chính (Home Base)
            WaitIfPaused(token);
            if (CheckStop(token)) return;

            Console.WriteLine("[FSM-CS] phase=home_check status=start");
            bool isHome = EnsureHomeBase(fastAttackOnly ? 8 : 50);
            if (!isHome)
            {
                Console.WriteLine("[FSM-CS ERROR] phase=cycle status=skip reason=home_not_detected");
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

                // 3. Kéo camera giãn góc nhìn rộng (Multi-Zoom Out)
                WaitIfPaused(token);
                if (CheckStop(token)) return;
                Console.WriteLine("[FSM-CS] phase=cycle status=pending step=3 details=\"adjusting_camera\"");
                ZoomOut();

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
            }

            // 6. Tìm kiếm tài nguyên (Scouting loop)
            WaitIfPaused(token);
            if (CheckStop(token)) return;

            var (goldReq, elixirReq, deReq) = GetFarmingThresholds(cfg, _currentVillageIdx);

            Console.WriteLine($"[CONFIG-CS] phase=startup active_village={_currentVillageIdx} gold_req={goldReq} elixir_req={elixirReq} dark_elixir_req={deReq}");
            Console.WriteLine($"[SCOUT-CS] phase=scout status=start village={_currentVillageIdx} gold_req={goldReq} elixir_req={elixirReq} dark_elixir_req={deReq}");

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
                if (resources.Gold >= goldReq && resources.Elixir >= elixirReq && resources.DarkElixir >= deReq)
                {
                    Console.WriteLine($"[SCOUT-CS] phase=scout status=success gold={resources.Gold} elixir={resources.Elixir} dark_elixir={resources.DarkElixir} details=\"target_accepted\"");
                    Console.WriteLine("[SCOUT-CS] phase=scout status=pending action=prepare_attack");
                    if (InterruptibleSleep(1500, token)) break;

                    // Chạy script tự động rải quân tấn công
                    string attackStrategy = GetAttackStrategy(cfg, _currentVillageIdx);
                    Console.WriteLine($"[ATTACK-CS] phase=select_strategy status=success village={_currentVillageIdx} strategy={attackStrategy}");
                    _attacks.Run(attackStrategy, token);
                    battleExecuted = true;

                    WaitIfPaused(token);
                    if (CheckStop(token)) break;

                    // Chờ trận đấu tự động kết thúc hoặc hết giờ
                    bool battleWaitOk = WaitBattleEnd(token);
                    if (!battleWaitOk)
                    {
                        return;
                    }

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

                    // Bấm nút quay trở về Làng chính
                    bool returnedHome = ReturnHome();
                    _fastAttackQueued = returnedHome;

                    WaitIfPaused(token);
                    if (CheckStop(token)) break;

                    if (returnedHome)
                    {
                        var wallConfig = GetWallUpgradeConfig(cfg, _currentVillageIdx);
                        Console.WriteLine($"[WALL DECISION] phase=post_battle enabled={wallConfig.Enabled} home={returnedHome} level={wallConfig.WallLevel} gold={wallConfig.GoldThreshold:N0} elixir={wallConfig.ElixirThreshold:N0} status=check");

                        if (wallConfig.Enabled)
                        {
                            if (EnsureHomeBase(maxWaitSeconds: 20))
                            {
                                _wallUpdater.HandleHomeResources(
                                    wallConfig.WallLevel,
                                    wallConfig.GoldThreshold,
                                    wallConfig.ElixirThreshold);
                            }
                            else
                            {
                                Console.WriteLine("[WALL RESULT] phase=post_battle status=skip reason=home_not_confirmed");
                            }
                        }
                        else
                        {
                            Console.WriteLine("[WALL RESULT] phase=post_battle status=skip reason=disabled");
                        }
                    }

                    break;
                }
                else
                {
                    Console.WriteLine("[SCOUT-CS] phase=scout status=skip details=\"target_skipped\"");
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
                    // Nghỉ ngắt quãng giữa các chu kỳ. Nếu vừa đánh xong, delay ngắn hơn để đánh tiếp ngay
                    InterruptibleSleep(_fastAttackQueued ? FastAttackCycleDelayMs : NormalCycleDelayMs, token);
                }
                return;
            }

            // Chế độ chạy nhiều tài khoản (Multi Account)
            int intervalSecs = GetIntOrDefault(multiConfig, "multi_interval_mins", 60) * 60;

            while (!CheckStop(token))
            {
                int[] selectedVillages = GetSelectedVillages(multiConfig);

                foreach (int idx in selectedVillages)
                {
                    WaitIfPaused(token);
                    if (CheckStop(token)) break;

                    _currentVillageIdx = idx;
                    _fastAttackQueued = false;
                    Console.WriteLine($"[FSM-CS] phase=worker_loop status=pending action=switch_village target={idx}");

                    // Thực hiện thay đổi tài khoản tương ứng
                    SwitchToVillagePlaceholder(idx);
                    _wallUpdater.ResetSavedOffset();

                    DateTime slotStart = DateTime.Now;
                    _cycleCount = 0;

                    // Chơi tài khoản này cho đến khi hết thời lượng phân bổ (mặc định 60 phút)
                    while ((DateTime.Now - slotStart).TotalSeconds < intervalSecs && !CheckStop(token))
                    {
                        WaitIfPaused(token);
                        OneCycle(Config, token);
                        InterruptibleSleep(_fastAttackQueued ? FastAttackCycleDelayMs : 15000, token);
                    }

                    Console.WriteLine($"[FSM-CS] phase=worker_loop status=pending action=switch_village target={idx} outcome=success");
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
            if (TryMatchTemplate(screenshot, "game_setting.png", GameSettingHomeRoi, HomeTemplateThreshold, out _, out double settingScore))
            {
                reason = $"game_setting score={settingScore:F3}";
                return true;
            }

            // Thử dò tìm biểu tượng nút Cửa hàng (Shop) ở góc dưới phải
            if (TryMatchTemplate(screenshot, "shop.png", null, HomeTemplateThreshold, out Point shopCenter, out double shopScore))
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
            if (TryMatchTemplate(screenshot, "treasure_hunt.png", TreasureHuntRoi, TreasureHuntThreshold, out center, out score)
                || TryMatchTemplate(screenshot, @"ui\treasure_hunt.png", TreasureHuntRoi, TreasureHuntThreshold, out center, out score))
            {
                return true;
            }

            double bestScore = score;
            Point bestCenter = center;

            if (TryMatchTemplateRegionMultiScale(
                    screenshot,
                    "treasure_hunt.png",
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
                    "treasure_hunt.png",
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

            bool found = TryMatchTemplate(screenshot, "next_button.png", NextButtonRoi, NextButtonThreshold, out _, out double score);

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
                if (TryMatchTemplate(screenshot, "end_battle.png", ScoutUiRoi, ScoutUiThreshold, out _, out _))
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

            using Mat? screenshot = _adb.TakeScreenshot();
            if (screenshot == null || screenshot.Empty())
            {
                return false;
            }

            foreach (string templateName in ConnectionPopupTemplates)
            {
                bool isLegacyConnectionTemplate = templateName.Equals("Client_error!.png", StringComparison.OrdinalIgnoreCase)
                    || templateName.Equals("Connection_lost.png", StringComparison.OrdinalIgnoreCase)
                    || templateName.Equals("Another_device.png", StringComparison.OrdinalIgnoreCase)
                    || templateName.Equals("rate_coc.png", StringComparison.OrdinalIgnoreCase);
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

        private static (int Gold, int Elixir, int DarkElixir) GetFarmingThresholds(JsonElement cfg, int villageIdx)
        {
            JsonElement profile = LoadVillageProfile(villageIdx);
            JsonElement farming = GetObjectOrDefault(cfg, "farming_thresholds");
            JsonElement target = GetObjectOrDefault(cfg, "target_data_threshold");

            return (
                GetThresholdOrDefault(profile, farming, target, "gold_threshold", "gold", 0),
                GetThresholdOrDefault(profile, farming, target, "elixir_threshold", "elixir", 0),
                GetThresholdOrDefault(profile, farming, target, "dark_elixir_threshold", "dark_elixir", 0)
            );
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
            JsonElement profile = LoadVillageProfile(villageIdx);

            if (profile.ValueKind == JsonValueKind.Object)
            {
                bool enabled = GetBoolOrDefault(profile, "upgrade_wall", false);
                int wallLevel = GetIntOrDefault(profile, "wall_level", GetIntOrDefault(cfg, "wall_level", 14));
                return CreateWallUpgradeConfig(
                    enabled,
                    wallLevel,
                    GetIntOrDefault(profile, "wall_gold_threshold", GetIntOrDefault(cfg, "wall_gold_threshold", 5_000_000)),
                    GetIntOrDefault(profile, "wall_elixir_threshold", GetIntOrDefault(cfg, "wall_elixir_threshold", 5_000_000)));
            }

            if (cfg.ValueKind == JsonValueKind.Object && cfg.TryGetProperty("upgrade_wall", out _))
            {
                bool enabled = GetBoolOrDefault(cfg, "upgrade_wall", false);
                return CreateWallUpgradeConfig(
                    enabled,
                    GetIntOrDefault(cfg, "wall_level", 14),
                    GetIntOrDefault(cfg, "wall_gold_threshold", 5_000_000),
                    GetIntOrDefault(cfg, "wall_elixir_threshold", 5_000_000));
            }

            // Dự phòng legacy chỉ dùng khi config mới chưa có khóa upgrade_wall.
            JsonElement wall = GetObjectOrDefault(cfg, "element_state_automation");
            if (wall.ValueKind != JsonValueKind.Object || !GetBoolOrDefault(wall, "upgrade_enabled", false))
            {
                return new WallUpgradeConfig(false, 14, 5_000_000, 5_000_000);
            }

            return CreateWallUpgradeConfig(
                true,
                GetIntOrDefault(wall, "wall_level", GetIntOrDefault(wall, "target_level", 14)),
                GetIntOrDefault(wall, "wall_gold_threshold", GetIntOrDefault(wall, "min_retained_gold", 5_000_000)),
                GetIntOrDefault(wall, "wall_elixir_threshold", GetIntOrDefault(wall, "min_retained_elixir", 5_000_000)));
        }

        private static WallUpgradeConfig CreateWallUpgradeConfig(bool enabled, int wallLevel, int goldThreshold, int elixirThreshold)
        {
            if (wallLevel < MinSupportedWallLevel || wallLevel > MaxSupportedWallLevel)
            {
                Console.WriteLine($"[WALL WARN] phase=config status=disabled level={wallLevel} reason=unsupported_wall_level supported={MinSupportedWallLevel}-{MaxSupportedWallLevel}");
                return new WallUpgradeConfig(false, wallLevel, goldThreshold, elixirThreshold);
            }

            return new WallUpgradeConfig(enabled, wallLevel, goldThreshold, elixirThreshold);
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

        // --- Liên kết thư viện ngoài (DLL Import) của hệ điều hành Windows ---
        // Phục vụ gửi phím ngầm (PostMessage) và gắn kết tiến trình (AttachThreadInput)
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

        /// <summary>
        /// Vòng lặp chờ đợi trận đánh kết thúc.
        /// Quét nhận diện liên tục nút Tiếp tục (Continue) hoặc vạch hiển thị kết quả chiến tích cướp tài nguyên.
        /// </summary>
        private bool WaitBattleEnd(CancellationToken token)
        {
            Console.WriteLine("[FSM-CS] phase=battle_wait status=start");

            DateTime start = DateTime.Now;
            int stableResultMatches = 0;
            bool waitingLogged = false;
            bool resultDetectedLogged = false;
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

                if (ConnectionPopupVisible(out string matchInfo, allowDialogShapeFallback: !resultDetectedLogged))
                {
                    Console.WriteLine("[FSM-CS WARNING] phase=battle_wait status=fail reason=connection_lost");
                    BootRecovery();
                    return false;
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
            return TryMatchTemplate(screenshot, @"ui\continue.png", ResultContinueRoi, ResultContinueThreshold, out center, out score)
                || TryMatchTemplate(screenshot, "continue.png", ResultContinueRoi, ResultContinueThreshold, out center, out score);
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

            if (!TryMatchTemplate(screenshot, "one_star.png", Rect.FromLTRB(518, 90, 747, 316), 0.40, out _, out _))
            {
                return 0;
            }

            if (!TryMatchTemplate(screenshot, "two_star.png", Rect.FromLTRB(670, 106, 926, 285), 0.40, out _, out _))
            {
                return 1;
            }

            return TryMatchTemplate(screenshot, "three_star.png", Rect.FromLTRB(840, 96, 1064, 317), 0.40, out _, out _)
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

        private static string StatsFilePath(int villageIdx)
        {
            string appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            return Path.Combine(appData, "SimpliMixi", "profiles", $"Stats_{villageIdx}.json");
        }

        private sealed record WallUpgradeConfig(
            bool Enabled,
            int WallLevel,
            int GoldThreshold,
            int ElixirThreshold);

        private sealed record TrainingConfig(
            string Mode,
            int QuickSlot,
            string AttackStrategy);

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

                // Gửi JSON-RPC pinchIn zoom out đa điểm qua ADB
                bool ok = _adb.PinchInZoomOut(count: 5, durationMs: 450, intervalMs: 350);
                if (ok)
                {
                    Console.WriteLine("[FSM-CS] phase=camera_zoom status=success details=\"bluestacks\"");
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
                "elixir_collector.png",
                "DE_collector.png",
                "gold_collector.png"
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

        private void SwitchToVillagePlaceholder(int villageIdx)
        {
            // Placeholder cho chức năng luân chuyển tài khoản trong tương lai
            Console.WriteLine($"[FSM-CS] phase=worker_loop status=pending action=switch_village target={villageIdx}");
            Thread.Sleep(3000);
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
