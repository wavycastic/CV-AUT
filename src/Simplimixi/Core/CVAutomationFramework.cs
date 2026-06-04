using System;
using System.IO;
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
        private readonly ManualResetEvent _pauseEvent = new(true);
        private volatile bool _isRunning;
        private int _cycleCount;
        private int _currentVillageIdx = 1;
        private volatile bool _fastAttackQueued; // Kích hoạt bỏ qua bước chuẩn bị nếu vừa đánh xong về thẳng Làng chính
        private bool _disposed;

        // Các vùng ROI giao diện chuẩn 1600x900px phục vụ xác thực
        private static readonly Rect GameSettingHomeRoi = Rect.FromLTRB(1445, 499, 1599, 708);
        private static readonly Rect NextButtonRoi = Rect.FromLTRB(1291, 563, 1592, 721);
        private static readonly Rect ScoutUiRoi = Rect.FromLTRB(2, 612, 222, 724);
        private static readonly Rect BattleEndedRoi = Rect.FromLTRB(632, 222, 989, 841);
        private static readonly Rect ResultYouGotRoi = Rect.FromLTRB(720, 330, 910, 390);
        private static readonly Rect ResultContinueRoi = Rect.FromLTRB(590, 670, 1020, 860);
        private static readonly Rect ConnectionPopupRoi = Rect.FromLTRB(360, 180, 1240, 720);
        
        // Vùng ROI cho sự kiện rương báu (Treasure Hunt) xuất hiện gây nghẽn màn hình
        private static readonly Rect TreasureHuntRoi = Rect.FromLTRB(940, 80, 1450, 830);
        private static readonly Rect TreasureHuntChestTemplateRoi = Rect.FromLTRB(105, 65, 210, 145);
        private static readonly Rect TreasureHuntTextTemplateRoi = Rect.FromLTRB(15, 210, 300, 275);
        private static readonly Point TreasureHuntOpenedChestTapPoint = new(800, 455);
        private static readonly Point TreasureHuntRewardContinueTapPoint = new(800, 750);
        
        // Ngưỡng tin cậy khớp mẫu
        private const double HomeTemplateThreshold = 0.70;
        private const double ConnectionPopupThreshold = 0.88;
        private const double ConnIconPopupThreshold = 0.94;
        private const double NextButtonThreshold = 0.35;
        private const double ScoutUiThreshold = 0.70;
        private const double TreasureHuntThreshold = 0.70;
        private const double TreasureHuntMarkerThreshold = 0.82;
        private const double ResultContinueThreshold = 0.38;
        private const double ResultYouGotThreshold = 0.55;
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

            _templatesPath = Path.Combine(AppContext.BaseDirectory, "Templates");
            _vision = new VisionEngine(_templatesPath);
            _training = new Training(_adb, _templatesPath, _vision);
            _attacks = new Attacks(_adb, _vision);
            _wallUpdater = new WallUpdater(_adb, _vision, _templatesPath);

            Console.WriteLine("[FSM-CS] Phân hệ lõi Máy trạng thái đã khởi tạo thành công.");
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
                Console.WriteLine($"[FSM-CS WARNING] Lỗi đọc cấu hình: {ex.Message}. Sử dụng cấu hình mặc định.");
            }

            // Gán cấu hình mặc định dự phòng
            string defaultJson = @"{
                ""device_connection"": {""host"": ""127.0.0.1"", ""port"": 5556},
                ""farming_thresholds"": {""gold_threshold"": 650000, ""elixir_threshold"": 650000, ""dark_elixir_threshold"": 0},
                ""element_state_automation"": {""upgrade_enabled"": true, ""wall_level"": 12, ""min_retained_gold"": 5000000},
                ""clan_capital"": {""enable_clan_capital"": true, ""capital_hall_level"": 9},
                ""enable_stats"": true,
                ""multi_account"": {""enable_multi_account"": true, ""multi_interval_mins"": 60, ""selected_villages"": [1, 2]}
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

            Task.Run(() => StartWorker(_cts.Token));
            Console.WriteLine("[FSM-CS] Vòng lặp tự động hóa đã bắt đầu chạy ngầm...");
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

                Console.WriteLine("🧠 Checking if we're on the home base screen...");
                BotLoop(token);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[FSM-CS ERROR] Lỗi khởi động bot: {ex.Message}");
                _isRunning = false;
            }
        }

        /// <summary>
        /// Dừng bot và giải phóng Task.
        /// </summary>
        public void Stop()
        {
            if (!_isRunning) return;

            _isRunning = false;
            _fastAttackQueued = false;
            _cts?.Cancel();
            Console.WriteLine("[FSM-CS] Đã gửi lệnh dừng khẩn cấp Máy trạng thái.");
        }

        /// <summary>
        /// Tạm dừng tạm thời luồng chạy bot.
        /// </summary>
        public void Pause()
        {
            _pauseEvent.Reset();
            Console.WriteLine("[FSM-CS] Đã tạm dừng luồng chạy bot.");
        }

        /// <summary>
        /// Tiếp tục luồng chạy bot đã tạm dừng.
        /// </summary>
        public void Resume()
        {
            _pauseEvent.Set();
            Console.WriteLine("[FSM-CS] Tiếp tục chạy luồng bot.");
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
                    Console.WriteLine($"[FSM-CS] Test loop cycle {i}/{cycleLimit}.");
                    OneCycle(Config, token);
                    if (i < cycleLimit && !CheckStop(token))
                    {
                        Thread.Sleep(_fastAttackQueued ? FastAttackCycleDelayMs : NormalCycleDelayMs);
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

            Console.WriteLine($"\n--- [FSM-CS] Bắt đầu Chu kỳ đơn (Làng_{_currentVillageIdx}) ---");
            bool fastAttackOnly = _fastAttackQueued;
            _fastAttackQueued = false;
            if (fastAttackOnly)
            {
                Console.WriteLine("[FSM-CS] Fast attack mode: vừa về home sau battle, bỏ qua prep và tìm trận ngay.");
            }

            // 1. Xác thực màn hình Làng chính (Home Base)
            WaitIfPaused(token);
            if (CheckStop(token)) return;

            Console.WriteLine("[FSM-CS] Bước 1: Xác thực tiêu điểm Làng chính...");
            bool isHome = EnsureHomeBase(fastAttackOnly ? 8 : 50);
            if (!isHome)
            {
                Console.WriteLine("[FSM-CS ERROR] Không thể xác định màn hình Làng chính. Bỏ qua chu kỳ này.");
                return;
            }

            if (!fastAttackOnly)
            {
                // 2. Click giải tỏa các popup linh tinh cản trở ở tâm màn hình
                _adb.Tap(140, 606);
                Thread.Sleep(1000);

                if (RecoverIfConnectionPopup("[WARN] Connection lost → recovering"))
                {
                    return;
                }

                // 3. Kéo camera giãn góc nhìn rộng (Multi-Zoom Out)
                WaitIfPaused(token);
                if (CheckStop(token)) return;
                Console.WriteLine("[FSM-CS] Bước 2: Kéo camera góc rộng (Zoom Out)...");
                ZoomOut();

                // 4. Huấn luyện lính theo cấu hình
                WaitIfPaused(token);
                if (CheckStop(token)) return;

                string trainMode = GetStringOrDefault(cfg, "train_mode", "smart");
                int quickSlot = GetIntOrDefault(cfg, "quick_slot", 1);

                if (trainMode.Equals("quick", StringComparison.OrdinalIgnoreCase) && _cycleCount % 5 == 0)
                {
                    Console.WriteLine($"[FSM-CS] Bước 3: Luyện quân nhanh (Quick Train Slot {quickSlot})...");
                    _training.QuickTrain(quickSlot);
                }
                else if (!trainMode.Equals("quick", StringComparison.OrdinalIgnoreCase) && _cycleCount % 3 == 0)
                {
                    Console.WriteLine($"[FSM-CS] Bước 3: Smart Train theo cấu hình attack='{GetStringOrDefault(cfg, "attack", "Dragon_Attack")}'...");
                    _training.SmartTrain(cfg);
                }

                if (RecoverIfConnectionPopup("[WARN] Connection lost → recovering"))
                {
                    return;
                }

                // 5. Nâng cấp tường nếu tài nguyên vượt ngưỡng cấu hình
                WaitIfPaused(token);
                if (CheckStop(token)) return;

                var wallConfig = GetWallUpgradeConfig(cfg, _currentVillageIdx);
                if (wallConfig.Enabled)
                {
                    Console.WriteLine($"[FSM-CS] Bước 4: Wall Updater - nâng tường level {wallConfig.WallLevel} nếu vượt ngưỡng...");
                    _wallUpdater.HandleHomeResources(
                        wallConfig.WallLevel,
                        wallConfig.GoldThreshold,
                        wallConfig.ElixirThreshold);
                }

                // 6. Thu hoạch tài nguyên mỏ
                WaitIfPaused(token);
                if (CheckStop(token)) return;
                Console.WriteLine("[FSM-CS] Bước 5: Tự động thu hoạch tài nguyên tại các mỏ sản xuất...");
                CollectResourcesPlaceholder();
            }

            // 7. Tìm kiếm tài nguyên (Scouting loop)
            WaitIfPaused(token);
            if (CheckStop(token)) return;

            var (goldReq, elixirReq, deReq) = GetFarmingThresholds(cfg);

            Console.WriteLine($"\n==============================================");
            Console.WriteLine($"[FSM-CS] TÌM TRẬN ĐẤU (Yêu cầu: Vàng >= {goldReq:N0} | Dầu hồng >= {elixirReq:N0})");
            Console.WriteLine($"==============================================");

            // Bấm nút Tấn công (Attack) để mở giao diện tìm kiếm
            SearchAttack();

            int searchCount = 1;
            int maxSearches = 50; // Thử tìm tối đa 50 nhà đối thủ khác nhau
            bool battleExecuted = false;

            while (searchCount <= maxSearches && !CheckStop(token))
            {
                WaitIfPaused(token);
                if (CheckStop(token)) break;

                Console.WriteLine($"\n[FSM-CS] Phân tích nhà đối thủ {searchCount}/{maxSearches}...");

                if (RecoverIfConnectionPopup("[WARN] Connection lost during evaluation → recovering"))
                {
                    return;
                }

                // Đợi cho đến khi giao diện tìm kiếm của đối thủ được tải xong (Scouting UI)
                if (!WaitForScoutScreen())
                {
                    Console.WriteLine("[WARN] Scouting UI not detected → recovering");
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

                    Console.WriteLine($"⚠️ Next button missing—retrying ({attempt}/2)");
                    Thread.Sleep(500);
                }

                if (!nextButtonFound)
                {
                    Console.WriteLine("⚠️ Next button still missing after 2 quick attempts—triggering recovery");
                    BootRecovery();
                    Console.WriteLine("✅ Recovered successfully");
                    return;
                }

                // Quét tài nguyên hiển thị ở góc trên bên trái của nhà đối thủ
                var resources = IsTarget.ExtractResources(_adb, _vision);

                // Kiểm tra xem lượng tài nguyên có đạt tiêu chí cấu hình hay không
                if (resources.Gold >= goldReq && resources.Elixir >= elixirReq && resources.DarkElixir >= deReq)
                {
                    Console.WriteLine($"🔥 [FSM-CS] ĐÃ ĐẠT TIÊU CHÍ! Gold={resources.Gold:N0} >= {goldReq:N0} | Elixir={resources.Elixir:N0} >= {elixirReq:N0}");
                    Console.WriteLine("[FSM-CS] Chờ 1.5s để trang trí/base overlay của đối thủ ẩn hết trước khi đánh...");
                    Thread.Sleep(1500);
                    if (CheckStop(token)) break;

                    // Chạy script tự động rải quân tấn công
                    string attackStrategy = GetStringOrDefault(cfg, "attack", "Dragon_Attack");
                    _attacks.Run(attackStrategy);
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
                    Console.WriteLine($"[STATS] Battle result: {starsGot} star(s), gained Gold={gained.Gold:N0} Elixir={gained.Elixir:N0} Dark={gained.DarkElixir:N0}");

                    // Cập nhật số liệu thống kê phiên chơi
                    if (GetBoolOrDefault(cfg, "enable_stats", false))
                    {
                        UpdateStats(_currentVillageIdx, starsGot, gained);
                    }
                    else
                    {
                        Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] [WORKER] ENABLE_STATS=False, skipping stats update");
                    }

                    // Bấm nút quay trở về Làng chính
                    _fastAttackQueued = ReturnHome();
                    break;
                }
                else
                {
                    Console.WriteLine("[FSM-CS] Tài nguyên thấp. Bỏ qua nhà này.");
                    // Bấm tìm kiếm đối thủ tiếp theo
                    SearchNext();
                    searchCount++;
                }
            }

            // Đầu hàng nếu quá 50 lần tìm kiếm không đạt yêu cầu
            if (!battleExecuted && !CheckStop(token))
            {
                Console.WriteLine("[FSM-CS WARN] Đã tìm kiếm 50 lần không thấy nhà phù hợp. Surrendering...");
                _adb.Tap(80, 780); // Click Surrender
                Thread.Sleep(1000);
                _adb.Tap(960, 560); // Confirm OK
                Thread.Sleep(2000);
                _adb.Tap(800, 780); // Click Home
                Thread.Sleep(5000);
            }

            _cycleCount++;
            Console.WriteLine($"--- [FSM-CS] Kết thúc Chu kỳ đơn (Làng_{_currentVillageIdx}) ---\n");
        }

        /// <summary>
        /// Vòng lặp chính xử lý luồng bot chạy vô hạn.
        /// Hỗ trợ luân chuyển chơi giữa nhiều tài khoản khác nhau định kỳ (Switch Account) nếu cấu hình multi-account bật.
        /// </summary>
        private void BotLoop(CancellationToken token)
        {
            Console.WriteLine("[FSM-CS] Vòng lặp bất đồng bộ bắt đầu xử lý...");

            JsonElement multiConfig = GetObjectOrDefault(Config, "multi_account");
            bool enableMulti = GetBoolOrDefault(multiConfig, "enable_multi_account", false);

            if (!enableMulti)
            {
                Console.WriteLine("[FSM-CS] Chế độ chạy đơn tài khoản (Single Account Mode).");
                _currentVillageIdx = 1;
                while (!CheckStop(token))
                {
                    OneCycle(Config, token);
                    // Nghỉ ngắt quãng giữa các chu kỳ. Nếu vừa đánh xong, delay ngắn hơn để đánh tiếp ngay
                    Thread.Sleep(_fastAttackQueued ? FastAttackCycleDelayMs : NormalCycleDelayMs);
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
                    Console.WriteLine($"[FSM-CS] Đang thực hiện chuyển sang Làng_{idx}...");

                    // Thực hiện thay đổi tài khoản tương ứng
                    SwitchToVillagePlaceholder(idx);

                    DateTime slotStart = DateTime.Now;
                    _cycleCount = 0;

                    // Chơi tài khoản này cho đến khi hết thời lượng phân bổ (mặc định 60 phút)
                    while ((DateTime.Now - slotStart).TotalSeconds < intervalSecs && !CheckStop(token))
                    {
                        WaitIfPaused(token);
                        OneCycle(Config, token);
                        Thread.Sleep(_fastAttackQueued ? FastAttackCycleDelayMs : 15000);
                    }

                    Console.WriteLine($"[FSM-CS] Hoàn tất thời gian chơi của Làng_{idx}.");
                }

                Thread.Sleep(5000);
            }

            Console.WriteLine("[FSM-CS] Vòng lặp chạy ngầm đã dừng hoàn toàn.");
        }

        /// <summary>
        /// Đảm bảo giao diện hiện tại là Làng chính (Home Base) bằng cách dò tìm icon Settings hoặc nút Shop.
        /// Chờ đợi tối đa maxWaitSeconds, nếu không thấy sẽ thử chạy luồng BootRecovery.
        /// </summary>
        private bool EnsureHomeBase(int maxWaitSeconds = 50, bool allowBootRecovery = true)
        {
            Console.WriteLine("[BASE] Checking if we're on the home base screen...");

            DateTime start = DateTime.Now;
            while ((DateTime.Now - start).TotalSeconds < maxWaitSeconds)
            {
                if (DetectHomeBase(out string reason))
                {
                    Console.WriteLine($"[BASE] Home base confirmed ({reason}).");
                    return true;
                }

                Console.WriteLine("[BASE] Still not on home base. Retrying in 5s...");
                Thread.Sleep(5000);
            }

            if (!allowBootRecovery)
            {
                Console.WriteLine("[BASE] Failed to detect home base after recovery retry.");
                return false;
            }

            Console.WriteLine("[BASE] Failed to detect home base. Initiating reboot sequence...");
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
                Console.WriteLine("[BASE WARNING] Failed to read screenshot.");
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

            Console.WriteLine($"[BASE] game_setting score={settingScore:F3}; shop template not confirmed.");
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
            Console.WriteLine("[RECOVERY] Restarting Clash of Clans...");

            _adb.ExecuteShell("am force-stop com.supercell.clashofclans");
            _adb.ExecuteShell("monkey -p com.supercell.clashofclans -c android.intent.category.LAUNCHER 1");

            Console.WriteLine("[RECOVERY] Waiting 10 seconds for game to load...");
            Thread.Sleep(10000);

            Console.WriteLine("[RECOVERY] Dismissing pop-ups...");
            _adb.Tap(146, 487); // Chạm góc dưới bên trái để giải tỏa nhanh các hộp thoại sự kiện
        }

        /// <summary>
        /// Bấm chọn nút Tấn công ngoài Làng chính và chuẩn bị tìm trận.
        /// Đồng thời xử lý popup sự kiện cản trở nếu có (Treasure Hunt).
        /// </summary>
        private void SearchAttack()
        {
            _adb.Tap(113, 797); // Nút Tấn công chính
            Thread.Sleep(700);
            HandleTreasureHuntIfPresent();
            _adb.Tap(272, 659); // Chọn Tìm trận đối thủ (Find Match)
            Thread.Sleep(700);
            _adb.Tap(1445, 804); // Chấp nhận phí tìm trận ban đầu
        }

        private bool HandleTreasureHuntIfPresent(bool verboseNotFound = true)
        {
            using Mat? screenshot = _adb.TakeScreenshot();
            if (screenshot == null || screenshot.Empty())
            {
                Console.WriteLine("[TREASURE] Screenshot failed while checking Treasure Hunt popup.");
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
                    Console.WriteLine($"[TREASURE] Treasure Hunt popup not found (score={score:F2}).");
                }

                return false;
            }

            Console.WriteLine($"[TREASURE] Treasure Hunt popup detected at ({center.X},{center.Y}) score={score:F2}.");
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
            Console.WriteLine($"[TREASURE] Possible opened Treasure Hunt chest screen. Tapping chest at ({TreasureHuntOpenedChestTapPoint.X},{TreasureHuntOpenedChestTapPoint.Y}) 5 times...");
            for (int i = 1; i <= 5; i++)
            {
                _adb.Tap(TreasureHuntOpenedChestTapPoint.X, TreasureHuntOpenedChestTapPoint.Y);
                Thread.Sleep(350);
            }

            Thread.Sleep(2000);
            if (!TapTreasureRewardContinue())
            {
                Console.WriteLine($"[TREASURE] Reward Continue template not confirmed. Using fallback tap at ({TreasureHuntRewardContinueTapPoint.X},{TreasureHuntRewardContinueTapPoint.Y}).");
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
                    Console.WriteLine($"[TREASURE] Reward Continue found at ({continueCenter.X},{continueCenter.Y}) score={score:F2}. Tapping...");
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
                Console.WriteLine("❌ Could not load screenshot for next button check.");
                return false;
            }

            bool found = TryMatchTemplate(screenshot, "next_button.png", NextButtonRoi, NextButtonThreshold, out _, out double score);
            Console.WriteLine($"    [DEBUG] Next-btn match score: {score:F2}");
            return found;
        }

        /// <summary>
        /// Đợi giao diện tìm trận (chứa thanh lính và nút đầu hàng dưới đáy) xuất hiện thành công.
        /// </summary>
        private bool WaitForScoutScreen(int timeoutSeconds = 12, int intervalMs = 500)
        {
            Console.WriteLine("[WAIT] Scouting UI loading…");

            DateTime start = DateTime.Now;
            while ((DateTime.Now - start).TotalSeconds < timeoutSeconds)
            {
                Thread.Sleep(350);

                using Mat? screenshot = _adb.TakeScreenshot();
                if (screenshot == null || screenshot.Empty())
                {
                    Console.WriteLine("❌ Screenshot failed.");
                    Thread.Sleep(intervalMs);
                    continue;
                }

                // Dò biểu tượng thanh thả lính chiến trận
                if (TryMatchTemplate(screenshot, "end_battle.png", ScoutUiRoi, ScoutUiThreshold, out _, out _))
                {
                    Console.WriteLine("[OK] Scouting UI detected.");
                    return true;
                }

                // Giải tỏa nhanh popup sự kiện săn rương nếu vô tình xuất hiện cản màn hình
                if (HandleTreasureHuntIfPresent(screenshot, verboseNotFound: false))
                {
                    continue;
                }

                Thread.Sleep(intervalMs);
            }

            Console.WriteLine("[WARN] Scouting UI never detected.");
            return false;
        }

        /// <summary>
        /// Kiểm tra sự xuất hiện của popup mất kết nối mạng. Nếu có, thực hiện khởi động lại game.
        /// </summary>
        private bool RecoverIfConnectionPopup(string warningMessage)
        {
            if (!ConnectionPopupVisible(out string matchInfo))
            {
                return false;
            }

            Console.WriteLine($"{warningMessage} ({matchInfo})");
            BootRecovery();
            return true;
        }

        /// <summary>
        /// Kiểm tra xem có bất kỳ popup báo lỗi kết nối mạng nào đang cản màn hình không.
        /// </summary>
        private bool ConnectionPopupVisible(out string matchInfo)
        {
            matchInfo = "none";

            using Mat? screenshot = _adb.TakeScreenshot();
            if (screenshot == null || screenshot.Empty())
            {
                return false;
            }

            foreach (string templateName in ConnectionPopupTemplates)
            {
                double threshold = templateName.EndsWith("conn.png", StringComparison.OrdinalIgnoreCase)
                    ? ConnIconPopupThreshold
                    : ConnectionPopupThreshold;

                if (!TryMatchTemplate(screenshot, templateName, ConnectionPopupRoi, threshold, out Point center, out double score))
                {
                    continue;
                }

                matchInfo = $"{templateName} score={score:F2} center=({center.X},{center.Y})";
                Console.WriteLine($"[OK] Detected \"{templateName}\" (score={score:F2}, center=({center.X},{center.Y}))");
                return true;
            }

            return false;
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

            string templatePath = Path.Combine(_templatesPath, templateFileName);
            if (!File.Exists(templatePath))
            {
                Console.WriteLine($"[VISION WARNING] Template không tồn tại: {templatePath}");
                return false;
            }

            using Mat template = Cv2.ImRead(templatePath, ImreadModes.Grayscale);
            if (template.Empty())
            {
                Console.WriteLine($"[VISION WARNING] Không đọc được template: {templatePath}");
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

        private static (int Gold, int Elixir, int DarkElixir) GetFarmingThresholds(JsonElement cfg)
        {
            JsonElement farming = GetObjectOrDefault(cfg, "farming_thresholds");
            if (farming.ValueKind == JsonValueKind.Object)
            {
                return (
                    GetIntOrDefault(farming, "gold_threshold", 0),
                    GetIntOrDefault(farming, "elixir_threshold", 0),
                    GetIntOrDefault(farming, "dark_elixir_threshold", 0)
                );
            }

            JsonElement target = GetObjectOrDefault(cfg, "target_data_threshold");
            return (
                GetIntOrDefault(target, "gold", 0),
                GetIntOrDefault(target, "elixir", 0),
                GetIntOrDefault(target, "dark_elixir", 0)
            );
        }

        /// <summary>
        /// Nạp thông tin cấu hình nâng cấp tường của tài khoản tương ứng từ Village profile.
        /// </summary>
        private static WallUpgradeConfig GetWallUpgradeConfig(JsonElement cfg, int villageIdx)
        {
            JsonElement profile = LoadVillageProfile(villageIdx);

            if (profile.ValueKind == JsonValueKind.Object)
            {
                bool enabled = GetBoolOrDefault(profile, "upgrade_wall", false);
                return new WallUpgradeConfig(
                    Enabled: enabled,
                    WallLevel: GetIntOrDefault(profile, "wall_level", 12),
                    GoldThreshold: GetIntOrDefault(profile, "wall_gold_threshold", 5_000_000),
                    ElixirThreshold: GetIntOrDefault(profile, "wall_elixir_threshold", 5_000_000));
            }

            // Dự phòng: Nạp cấu hình từ legacy element_state_automation
            JsonElement wall = GetObjectOrDefault(cfg, "element_state_automation");
            if (wall.ValueKind != JsonValueKind.Object || !GetBoolOrDefault(wall, "upgrade_enabled", false))
            {
                return new WallUpgradeConfig(false, 12, 5_000_000, 5_000_000);
            }

            return new WallUpgradeConfig(
                Enabled: true,
                WallLevel: GetIntOrDefault(wall, "wall_level", GetIntOrDefault(wall, "target_level", 12)),
                GoldThreshold: GetIntOrDefault(wall, "wall_gold_threshold", GetIntOrDefault(wall, "min_retained_gold", 5_000_000)),
                ElixirThreshold: GetIntOrDefault(wall, "wall_elixir_threshold", GetIntOrDefault(wall, "min_retained_elixir", 5_000_000)));
        }

        private static JsonElement LoadVillageProfile(int villageIdx)
        {
            string path = Path.Combine(Directory.GetCurrentDirectory(), "profiles", $"Village_{villageIdx}.json");
            if (!File.Exists(path))
            {
                return default;
            }

            try
            {
                using JsonDocument doc = JsonDocument.Parse(File.ReadAllText(path));
                return doc.RootElement.Clone();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[FSM-CS WARNING] Không đọc được profile wall {path}: {ex.Message}");
                return default;
            }
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
            Console.WriteLine("⏳ Waiting for battle to finish…");

            DateTime start = DateTime.Now;
            int stableResultMatches = 0;
            while (!CheckStop(token))
            {
                WaitIfPaused(token);
                if (CheckStop(token)) return false;

                if (ConnectionPopupVisible(out string matchInfo))
                {
                    Console.WriteLine($"\n[WARN] Connection lost detected—recovering… ({matchInfo})");
                    BootRecovery();
                    return false;
                }

                if (BattleEnded(out string resultMatchInfo))
                {
                    stableResultMatches++;
                    Console.Write($"\r[BATTLE] result screen detected ({stableResultMatches}/{ResultScreenStableMatches}) {resultMatchInfo}");

                    if (stableResultMatches >= ResultScreenStableMatches)
                    {
                        Console.WriteLine("\n🏁 Battle ended");
                        Thread.Sleep(1000);
                        return true;
                    }
                }
                else
                {
                    stableResultMatches = 0;
                }

                if ((DateTime.Now - start).TotalSeconds >= MaxWaitBattleSeconds)
                {
                    Console.WriteLine("\r⏰ Timeout waiting for result screen. Skipping stats/return-home tap for safety.");
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

            Console.Write($"\r[BATTLE] waiting for result screen... {matchInfo}");
            return false;
        }

        private bool TryFindContinueButton(Mat screenshot, out Point center, out double score)
        {
            return TryMatchTemplate(screenshot, @"ui\continue.png", ResultContinueRoi, ResultContinueThreshold, out center, out score)
                || TryMatchTemplate(screenshot, "continue.png", ResultContinueRoi, ResultContinueThreshold, out center, out score);
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

            string templatePath = Path.Combine(_templatesPath, templateFileName);
            if (!File.Exists(templatePath))
            {
                Console.WriteLine($"[VISION WARNING] Template không tồn tại: {templatePath}");
                return false;
            }

            using Mat fullTemplate = Cv2.ImRead(templatePath, ImreadModes.Grayscale);
            if (fullTemplate.Empty())
            {
                Console.WriteLine($"[VISION WARNING] Không đọc được template: {templatePath}");
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

            string templatePath = Path.Combine(_templatesPath, templateFileName);
            if (!File.Exists(templatePath))
            {
                Console.WriteLine($"[VISION WARNING] Template không tồn tại: {templatePath}");
                return false;
            }

            using Mat fullTemplate = Cv2.ImRead(templatePath, ImreadModes.Grayscale);
            if (fullTemplate.Empty())
            {
                Console.WriteLine($"[VISION WARNING] Không đọc được template: {templatePath}");
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

            Directory.CreateDirectory("logs");
            Cv2.ImWrite(Path.Combine("logs", "debug_stats_result.png"), screenshot);

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

            Console.WriteLine($"[STATS OCR] {label}: unreadable -> 0");
            return 0;
        }

        private static bool IsPlausibleResourceValue(int value, double confidence, int minValidValue, string label, string mode)
        {
            bool plausible = value == 0 || value >= minValidValue;
            if (confidence < 0.62)
            {
                Console.WriteLine($"[STATS OCR] {label}: rejected {value:N0} via {mode}, confidence={confidence:F2} < 0.62");
                return false;
            }

            if (!plausible)
            {
                Console.WriteLine($"[STATS OCR] {label}: rejected tiny value {value:N0} via {mode}, confidence={confidence:F2}");
                return false;
            }

            Console.WriteLine($"[STATS OCR] {label}: {value:N0} via {mode}, confidence={confidence:F2}");
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
                Cv2.ImWrite(Path.Combine("logs", $"debug_stats_{label}.png"), crop);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[STATS OCR WARNING] Không thể lưu crop {label}: {ex.Message}");
            }
        }

        /// <summary>
        /// Bấm quay trở về Làng chính sau khi trận đánh kết thúc.
        /// Giải tỏa rương báu hoặc bấm Back hệ thống Android nếu bị kẹt.
        /// </summary>
        private bool ReturnHome()
        {
            Console.WriteLine("[FSM-CS] Quay trở về làng chính...");

            using Mat? screenshot = _adb.TakeScreenshot();
            if (screenshot != null && !screenshot.Empty() && TryFindContinueButton(screenshot, out Point continueCenter, out double score))
            {
                Console.WriteLine($"[FSM-CS] Found Return Home/Continue button at ({continueCenter.X},{continueCenter.Y}) score={score:F2}.");
                _adb.Tap(continueCenter.X, continueCenter.Y);
            }
            else
            {
                Console.WriteLine("[FSM-CS WARN] Continue template not found at return step; using fallback tap.");
                _adb.Tap(788, 768);
            }

            Thread.Sleep(3000);
            if (!DetectHomeBase(out _))
            {
                // Giải quyết rương báu nếu xuất hiện đột ngột cản trở
                if (!HandleTreasureHuntIfPresent(verboseNotFound: false))
                {
                    HandleOpenedTreasureChest();
                }

                if (!DetectHomeBase(out _))
                {
                    Console.WriteLine("[FSM-CS] Still not on home base after Treasure handling. Sending one BACK.");
                    _adb.ExecuteShell("input keyevent KEYCODE_BACK"); // Gửi lệnh phím Back của Android để giải tỏa nhanh các popup xếp chồng
                    Thread.Sleep(1500);
                }
            }

            return EnsureHomeBase(maxWaitSeconds: 20);
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

            File.WriteAllText(path, stats.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
            Console.WriteLine($"[STATS] Updated {path}");
        }

        private static string StatsFilePath(int villageIdx)
        {
            return Path.Combine(Directory.GetCurrentDirectory(), "profiles", $"Stats_{villageIdx}.json");
        }

        private sealed record WallUpgradeConfig(
            bool Enabled,
            int WallLevel,
            int GoldThreshold,
            int ElixirThreshold);

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
                Console.WriteLine($"[STATS WARNING] Cannot load stats file {path}: {ex.Message}");
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
            Console.WriteLine("[FSM-CS] Đang thực hiện thu nhỏ góc nhìn bản đồ (Zoom Out)...");

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
                Console.WriteLine($"[FSM-CS] Phát hiện giả lập MEmu HWND: {memuParent}. Gửi F3 theo cơ chế Simplicity...");
                
                // Gửi mã phím F3 (Virtual Key Code = 0x72) 4 lần ngầm vào MEmu để thực hiện thu nhỏ camera
                SendKeyToWindow(memuParent, (IntPtr)0x72, repetitions: 4, gapMs: 1000);
                Console.WriteLine("[FSM-CS] Gửi lệnh Zoom Out ngầm tới MEmu hoàn tất.");
            }
            else if (bsParent != IntPtr.Zero)
            {
                Console.WriteLine($"[FSM-CS] Phát hiện giả lập BlueStacks HWND: {bsParent}. Gửi pinch-in zoom-out qua ADB...");
                
                // Gửi JSON-RPC pinchIn zoom out đa điểm qua ADB
                bool ok = _adb.PinchInZoomOut(count: 5, durationMs: 450, intervalMs: 350);
                if (ok)
                {
                    Console.WriteLine("[FSM-CS] Gửi lệnh Zoom Out BlueStacks qua ADB hoàn tất.");
                }
                else
                {
                    Console.WriteLine("[FSM-CS WARNING] Zoom Out BlueStacks qua ADB không trả về thành công.");
                }
            }
            else
            {
                Console.WriteLine("[FSM-CS WARNING] Không tìm thấy cửa sổ giả lập BlueStacks hoặc MEmu đang chạy trên Windows. Bỏ qua lệnh Zoom Out...");
            }
        }

        /// <summary>
        /// Thu hoạch mỏ tài nguyên (Gold/Elixir/Dark Elixir Collector) trên màn hình Làng chính.
        /// Dò tìm các bong bóng icon tài nguyên lơ lửng trên mỏ và thực hiện chạm (Tap).
        /// </summary>
        private void CollectResourcesPlaceholder()
        {
            string[] collectorTemplates =
            {
                "elixir_collector.png",
                "DE_collector.png",
                "gold_collector.png"
            };

            using Mat? screenshot = _adb.TakeScreenshot();
            if (screenshot == null || screenshot.Empty())
            {
                Console.WriteLine("[FSM-CS WARNING] Không thể chụp ảnh để thu hoạch mỏ tài nguyên.");
                return;
            }

            using Mat grayScreenshot = new Mat();
            Cv2.CvtColor(screenshot, grayScreenshot, ColorConversionCodes.BGR2GRAY);

            foreach (string templateName in collectorTemplates)
            {
                string templatePath = Path.Combine(_templatesPath, templateName);
                if (!File.Exists(templatePath))
                {
                    Console.WriteLine($"[FSM-CS WARNING] Thiếu template thu hoạch: {templatePath}");
                    continue;
                }

                using Mat template = Cv2.ImRead(templatePath, ImreadModes.Grayscale);
                if (template.Empty())
                {
                    Console.WriteLine($"[FSM-CS WARNING] Không đọc được template thu hoạch: {templatePath}");
                    continue;
                }

                using Mat result = new Mat();
                Cv2.MatchTemplate(grayScreenshot, template, result, TemplateMatchModes.CCoeffNormed);
                Cv2.MinMaxLoc(result, out _, out double maxVal, out _, out Point maxLoc);

                // Ngưỡng tin cậy thu hoạch từ 65% trở lên
                if (maxVal < 0.65)
                {
                    Console.WriteLine($"[FSM-CS] Bỏ qua {templateName}: confidence {maxVal:F2} < 0.65.");
                    continue;
                }

                int centerX = maxLoc.X + template.Width / 2;
                int centerY = maxLoc.Y + template.Height / 2;
                Console.WriteLine($"[FSM-CS] Thu hoạch {templateName} tại ({centerX}, {centerY}) confidence={maxVal:F2}.");
                _adb.Tap(centerX, centerY);
                Thread.Sleep(500);
            }
        }

        private void SwitchToVillagePlaceholder(int villageIdx)
        {
            // Placeholder cho chức năng luân chuyển tài khoản trong tương lai
            Console.WriteLine($"[FSM-CS-SWITCH] Bấm Settings -> Đăng nhập tài khoản Làng_{villageIdx}...");
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
