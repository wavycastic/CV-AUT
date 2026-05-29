using System;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows.Forms;
using OpenCvSharp;
using Timer = System.Threading.Timer;

namespace CvAut
{
    class Program
    {
        private const int MenuWidth = 92;
        private const int SummaryWidth = 78;

        private static readonly MenuItem[] MainMenu =
        {
            new("1", "Kiểm thử offline", "OCR tài nguyên từ ảnh mẫu và kiểm thử attack offline", "Test", RunOfflineTestFromMenu),
            new("2", "FSM live loop", "Chạy bot theo FSM với điều khiển Pause/Resume/Stop", "Bot", RunLiveFSMLoopFromMenu),
            new("3", "Quét đối thủ", "Chụp giả lập và đọc loot nhà đối thủ", "Live OCR", RunLiveScoutingTestFromMenu),
            new("4", "Quét làng chính", "Đọc tài nguyên làng chính ở góc phải màn hình", "Live OCR", RunLiveHomeBaseTestFromMenu),
            new("5", "Zoom Out live", "Gửi thao tác zoom out tới giả lập", "Thiết bị", RunLiveZoomOutTestFromMenu),
            new("6", "Smart Train", "Kiểm tra OCR quân và logic train một lần", "Army", RunLiveSmartTrainTestFromMenu),
            new("7", "Boot Recovery", "Force-stop rồi mở lại Clash of Clans", "Thiết bị", RunLiveBootRecoveryTestFromMenu),
            new("8", "Run bot vô hạn", "Chạy bot liên tục, P pause, R tiếp tục, S dừng", "Bot", RunInfiniteBotFromMenu),
            new("9", "Thoát", "Đóng chương trình", "Hệ thống", null)
        };

        [STAThread]
        static void Main(string[] args)
        {
            if (!args.Any(a => string.Equals(a, "--console", StringComparison.OrdinalIgnoreCase)))
            {
                ApplicationConfiguration.Initialize();
                Application.Run(new BotControlForm());
                return;
            }

            try { Console.Clear(); } catch {}

            string configPath = "CV-AUT-PY/test_config.json";
            string templatesPath = Path.Combine(AppContext.BaseDirectory, "Templates");
            if (args.Any(a => string.Equals(a, "--diagnose-saved-army-window", StringComparison.OrdinalIgnoreCase)))
            {
                Training.DiagnoseSavedArmyWindow("live_army_window_debug.png", templatesPath);
                return;
            }

            while (true)
            {
                MenuItem selected = ReadMainMenuChoice();
                if (selected.Action == null)
                {
                    WriteInfoLine("Đang đóng ứng dụng. Hẹn gặp lại bạn!");
                    break;
                }

                selected.Action(configPath, templatesPath);
            }
        }

        private static void RunOfflineTestFromMenu(string _, string templatesPath) => RunOfflineTest(templatesPath);
        private static void RunLiveFSMLoopFromMenu(string configPath, string _) => RunLiveFSMLoop(configPath);
        private static void RunLiveScoutingTestFromMenu(string _, string templatesPath) => RunLiveScoutingTest(templatesPath);
        private static void RunLiveHomeBaseTestFromMenu(string _, string templatesPath) => RunLiveHomeBaseTest(templatesPath);
        private static void RunLiveZoomOutTestFromMenu(string configPath, string _) => RunLiveZoomOutTest(configPath);
        private static void RunLiveSmartTrainTestFromMenu(string configPath, string templatesPath) => RunLiveSmartTrainTest(configPath, templatesPath);
        private static void RunLiveBootRecoveryTestFromMenu(string configPath, string _) => RunLiveBootRecoveryTest(configPath);
        private static void RunLiveWorkflowTemplateTestFromMenu(string configPath, string _) => RunLiveWorkflowTemplateTest(configPath);
        private static void RunInfiniteBotFromMenu(string configPath, string _) => RunLiveFSMLoop(configPath);

        private static MenuItem ReadMainMenuChoice()
        {
            int selectedIndex = 7;

            while (true)
            {
                DrawMainMenu(selectedIndex);
                ConsoleKeyInfo key = Console.ReadKey(intercept: true);

                if (key.Key == ConsoleKey.UpArrow)
                {
                    selectedIndex = selectedIndex == 0 ? MainMenu.Length - 1 : selectedIndex - 1;
                }
                else if (key.Key == ConsoleKey.DownArrow)
                {
                    selectedIndex = (selectedIndex + 1) % MainMenu.Length;
                }
                else if (key.Key == ConsoleKey.Enter)
                {
                    return MainMenu[selectedIndex];
                }
                else
                {
                    string keyChar = key.KeyChar.ToString();
                    int index = Array.FindIndex(MainMenu, item => item.Key == keyChar);
                    if (index >= 0)
                    {
                        return MainMenu[index];
                    }
                }
            }
        }

        private static void DrawMainMenu(int selectedIndex)
        {
            try { Console.Clear(); } catch {}

            Console.Title = "CV-AUT C# Control Console";
            WriteRule(ConsoleColor.DarkCyan);
            WriteCentered("CV-AUT C# CONTROL CONSOLE", ConsoleColor.Cyan);
            WriteCentered("Tu dong hoa Clash of Clans | FSM + OCR + ADB", ConsoleColor.Gray);
            WriteRule(ConsoleColor.DarkCyan);
            Console.WriteLine();
            WriteStatusBar("UP/DOWN chon muc", "ENTER chay", "1-9 phim tat", "P/R/S khi bot dang chay");
            Console.WriteLine();

            for (int i = 0; i < MainMenu.Length; i++)
            {
                MenuItem item = MainMenu[i];
                bool selected = i == selectedIndex;
                DrawMenuRow(item, selected);
            }

            Console.WriteLine();
            WriteRule(ConsoleColor.DarkGray);
            WriteMuted("Mac dinh app se mo WinForms. Dung --console de vao man hinh nay.");
            WriteMuted("Phim tat bot: P = Pause, R = Resume, S = Stop.");
        }

        private static void PrintBanner(string title, string? subtitle = null)
        {
            try { Console.Clear(); } catch {}
            WriteRule(ConsoleColor.DarkCyan);
            WriteCentered(title, ConsoleColor.Cyan);
            if (!string.IsNullOrWhiteSpace(subtitle))
            {
                WriteCentered(subtitle, ConsoleColor.Gray);
            }
            WriteRule(ConsoleColor.DarkCyan);
            Console.WriteLine();
        }

        private static string Center(string text, int width)
        {
            if (text.Length >= width) return text;
            int left = (width - text.Length) / 2;
            return new string(' ', left) + text;
        }

        private static void DrawMenuRow(MenuItem item, bool selected)
        {
            ConsoleColor accent = item.Action == null ? ConsoleColor.Red : ConsoleColor.Green;
            Console.ForegroundColor = selected ? accent : ConsoleColor.DarkGray;
            Console.Write(selected ? "  > " : "    ");
            Console.Write($"[{item.Key}] ");

            Console.ForegroundColor = selected ? ConsoleColor.White : ConsoleColor.Gray;
            Console.Write($"{item.Title,-19}");

            Console.ForegroundColor = selected ? ConsoleColor.Yellow : ConsoleColor.DarkCyan;
            Console.Write($" {item.Category,-10}");

            Console.ForegroundColor = selected ? ConsoleColor.Gray : ConsoleColor.DarkGray;
            Console.WriteLine($" {TrimToWidth(item.Description, 48)}");
            Console.ResetColor();
        }

        private static void WriteRule(ConsoleColor color)
        {
            Console.ForegroundColor = color;
            Console.WriteLine(new string('=', MenuWidth));
            Console.ResetColor();
        }

        private static void WriteCentered(string text, ConsoleColor color)
        {
            Console.ForegroundColor = color;
            Console.WriteLine(Center(text, MenuWidth));
            Console.ResetColor();
        }

        private static void WriteStatusBar(params string[] segments)
        {
            Console.ForegroundColor = ConsoleColor.Black;
            Console.BackgroundColor = ConsoleColor.DarkCyan;
            Console.Write("  ");
            Console.Write(string.Join("  |  ", segments).PadRight(MenuWidth - 4));
            Console.WriteLine("  ");
            Console.ResetColor();
        }

        private static void WriteMuted(string text)
        {
            Console.ForegroundColor = ConsoleColor.DarkGray;
            Console.WriteLine(text);
            Console.ResetColor();
        }

        private static void WriteInfoLine(string text)
        {
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine(text);
            Console.ResetColor();
        }

        private static string TrimToWidth(string text, int width)
        {
            if (text.Length <= width)
            {
                return text;
            }

            return text[..Math.Max(0, width - 3)] + "...";
        }

        private static void RunOfflineTest(string templatesPath)
        {
            try { Console.Clear(); } catch {}
            Console.WriteLine("=== CHẠY KIỂM THỬ NGOẠI TUYẾN (OFFLINE MOCK TEST) ===");

            VisionEngine vision = new VisionEngine(templatesPath);
            string offlineImagePath = Path.Combine(AppContext.BaseDirectory, "Templates", "ui", "enemy_resources.png");

            if (File.Exists(offlineImagePath))
            {
                Console.WriteLine("[TEST-CS] Phát hiện tệp kiểm thử ngoại tuyến...");
                using Mat testImg = Cv2.ImRead(offlineImagePath, ImreadModes.Color);
                if (!testImg.Empty())
                {
                    Console.WriteLine("\n=== [TEST-CS] 1. Kiểm thử Phân hệ Đọc số siêu nhẹ (Light OCR C#) ===");
                    
                    int wImg = testImg.Width;
                    int hImg = testImg.Height;

                    Rect goldRoi = new Rect(Math.Max(0, 55 - 60), Math.Max(0, 117 - 5), Math.Min(wImg, 55 + 196 + 15) - Math.Max(0, 55 - 60), Math.Min(hImg, 117 + 44 + 5) - Math.Max(0, 117 - 5));
                    Rect elixirRoi = new Rect(Math.Max(0, 60 - 15), Math.Max(0, 167 - 5), Math.Min(wImg, 60 + 201 + 15) - Math.Max(0, 60 - 15), Math.Min(hImg, 167 + 41 + 5) - Math.Max(0, 167 - 5));
                    Rect deRoi = new Rect(Math.Max(0, 73 - 15), Math.Max(0, 214 - 5), Math.Min(wImg, 73 + 110 + 15) - Math.Max(0, 73 - 15), Math.Min(hImg, 214 + 34 + 5) - Math.Max(0, 214 - 5));

                    int gold = vision.ExtractNumericalMetrics(testImg, goldRoi, isOffline: true);
                    int elixir = vision.ExtractNumericalMetrics(testImg, elixirRoi, isOffline: true);
                    int de = vision.ExtractNumericalMetrics(testImg, deRoi, isOffline: true);

                    Console.ForegroundColor = ConsoleColor.Yellow;
                    Console.WriteLine($"-> Vàng đọc được: {gold:N0} (Kỳ vọng: 353,139 - Chuẩn hóa OCR mới)");
                    Console.WriteLine($"-> Dầu hồng đọc được: {elixir:N0} (Kỳ vọng: 664,536 - Chuẩn hóa OCR mới)");
                    Console.WriteLine($"-> Dầu đen đọc được: {de:N0} (Kỳ vọng: 5,859 - Chuẩn hóa OCR mới)");
                    Console.ResetColor();

                    string homeImagePath = Path.Combine(AppContext.BaseDirectory, "Templates", "ui", "home.png");
                    if (File.Exists(homeImagePath))
                    {
                        Console.WriteLine("\n=== [TEST-CS] 1.2. Kiểm thử Phân hệ Đọc số Làng chính (Home Base OCR) ===");
                        using Mat homeImg = Cv2.ImRead(homeImagePath, ImreadModes.Color);
                        if (!homeImg.Empty())
                        {
                            int goldHome = vision.ExtractNumericalMetrics(homeImg, new Rect(1310, 30, 200, 36), isOffline: false, useRgbThresh: true);
                            int elixirHome = vision.ExtractNumericalMetrics(homeImg, new Rect(1310, 115, 200, 36), isOffline: false, useRgbThresh: true);
                            int deHome = vision.ExtractNumericalMetrics(homeImg, new Rect(1310, 200, 200, 32), isOffline: false, useRgbThresh: true);

                            Console.ForegroundColor = ConsoleColor.Yellow;
                            Console.WriteLine($"-> Làng chính - Vàng đọc được:     {goldHome:N0} (Kỳ vọng: 12,519,983)");
                            Console.WriteLine($"-> Làng chính - Dầu hồng đọc được: {elixirHome:N0} (Kỳ vọng: 12,813,630)");
                            Console.WriteLine($"-> Làng chính - Dầu đen đọc được:  {deHome:N0} (Kỳ vọng: 240,000)");
                            Console.ResetColor();
                        }
                    }

                    Console.WriteLine("\n=== [TEST-CS] 2. Kiểm thử Phân hệ Thả quân chống phát hiện (Attacks C#) ===");
                    
                    ADBHelper adb = new ADBHelper("127.0.0.1", 5556);
                    Attacks attack = new Attacks(adb, vision);
                    attack.Run("Dragon_Attack");
                }
                else
                {
                    Console.WriteLine("[ERROR] Không thể đọc hoặc giải mã tệp ảnh test offline.");
                }
            }
            else
            {
                Console.WriteLine($"[ERROR] Không tìm thấy ảnh test offline tại: {offlineImagePath}");
            }



            Console.WriteLine("\n==============================================");
            Console.WriteLine(" THỬ NGHIỆM PHÂN HỆ C# HOÀN TẤT. NHẤN PHÍM BẤT KỲ ĐỂ QUAY LẠI MENU.");
            Console.WriteLine("==============================================");
            try { Console.ReadKey(); } catch {}
        }

        private static void RunLiveFSMLoop(string configPath)
        {
            PrintBanner("FSM LIVE LOOP", "Chạy liên tục cho tới khi bấm S");

            RunSessionStats stats = new RunSessionStats();
            TextWriter originalOut = Console.Out;
            using LiveStatsPanel livePanel = new LiveStatsPanel(stats);
            using StatsTrackingTextWriter statsWriter = new StatsTrackingTextWriter(originalOut, stats, livePanel);
            Console.SetOut(statsWriter);

            CVAutomationFramework? framework = null;

            try
            {
                framework = new CVAutomationFramework(configPath);
                livePanel.Render();
                livePanel.Start();
                framework.Start();

                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine("\nBot đang chạy ngầm...");
                Console.WriteLine("  P  Tạm dừng");
                Console.WriteLine("  R  Tiếp tục");
                Console.WriteLine("  S  Dừng và quay về Menu");
                Console.ResetColor();

                while (true)
                {
                    if (Console.KeyAvailable)
                    {
                        var key = Console.ReadKey(true).Key;
                        if (key == ConsoleKey.P)
                        {
                            framework.Pause();
                        }
                        else if (key == ConsoleKey.R)
                        {
                            framework.Resume();
                        }
                        else if (key == ConsoleKey.S)
                        {
                            framework.Stop();
                            break;
                        }
                    }

                    Thread.Sleep(200);
                }
            }
            finally
            {
                framework?.Stop();
                livePanel.Stop();
                Console.SetOut(originalOut);
            }

            PrintSessionSummary(stats);
            PauseForMenu();
        }

        private static void RunLiveScoutingTest(string templatesPath)
        {
            try { Console.Clear(); } catch {}
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine("==================================================================");
            Console.WriteLine("   QUÉT TÀI NGUYÊN TRỰC TIẾP TỪ GIẢ LẬP BLUESTACKS ĐANG HOẠT ĐỘNG  ");
            Console.WriteLine("==================================================================");
            Console.ResetColor();

            Console.WriteLine("[LIVE-SCOUT] Đang khởi tạo kết nối ADB và tự động phát hiện thiết bị...");
            ADBHelper adb = new ADBHelper("127.0.0.1", 5556);
            VisionEngine vision = new VisionEngine(templatesPath);

            Console.WriteLine("[LIVE-SCOUT] Đang tiến hành chụp màn hình giả lập trực tiếp...");
            using Mat? screenshot = adb.TakeScreenshot();
            
            if (screenshot == null || screenshot.Empty())
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine("[LIVE-SCOUT ERROR] Không thể chụp ảnh màn hình giả lập! Vui lòng đảm bảo:");
                Console.WriteLine("  1. Giả lập BlueStacks / MEmu đã được bật.");
                Console.WriteLine("  2. Tính năng 'Android Debug Bridge (ADB)' đã được kích hoạt trong cài đặt giả lập.");
                Console.ResetColor();
            }
            else
            {
                Console.WriteLine($"[LIVE-SCOUT] Chụp ảnh thành công! Kích thước ảnh: {screenshot.Width}x{screenshot.Height}");
                Console.WriteLine("[LIVE-SCOUT] Đang nhận diện tài nguyên hiển thị trên màn hình...");

                // Gọi hàm quét IsTarget để đọc số
                var res = IsTarget.ExtractResources(adb, vision);

                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine("\n================ KẾT QUẢ QUÉT LIVE ================");
                Console.WriteLine($"👉 Vàng (Gold) quét được:       {res.Gold:N0}");
                Console.WriteLine($"👉 Dầu hồng (Elixir) quét được:  {res.Elixir:N0}");
                Console.WriteLine($"👉 Dầu đen (Dark Elixir) quét được: {res.DarkElixir:N0}");
                Console.WriteLine("===================================================");
                Console.ResetColor();
                
                // Lưu lại ảnh chụp màn hình để debug
                string debugPath = Path.Combine(AppContext.BaseDirectory, "live_screenshot_debug.png");
                Cv2.ImWrite(debugPath, screenshot);
                Console.WriteLine($"[LIVE-SCOUT] Đã lưu ảnh chụp debug thực tế tại: {debugPath}");
            }

            Console.WriteLine("\n==============================================");
            Console.WriteLine(" QUÉT TÀI NGUYÊN LIVE HOÀN TẤT. NHẤN PHÍM BẤT KỲ ĐỂ QUAY LẠI MENU.");
            Console.WriteLine("==============================================");
            try { Console.ReadKey(); } catch {}
        }

        private static void RunLiveHomeBaseTest(string templatesPath)
        {
            try { Console.Clear(); } catch {}
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine("==================================================================");
            Console.WriteLine("  QUÉT TÀI NGUYÊN LÀNG CHÍNH TRỰC TIẾP TỪ GIẢ LẬP BLUESTACKS ĐANG MỞ ");
            Console.WriteLine("==================================================================");
            Console.ResetColor();

            Console.WriteLine("[LIVE-HOME] Đang khởi tạo kết nối ADB và tự động phát hiện thiết bị...");
            ADBHelper adb = new ADBHelper("127.0.0.1", 5556);
            VisionEngine vision = new VisionEngine(templatesPath);

            Console.WriteLine("[LIVE-HOME] Đang tiến hành chụp màn hình giả lập trực tiếp...");
            using Mat? screenshot = adb.TakeScreenshot();
            
            if (screenshot == null || screenshot.Empty())
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine("[LIVE-HOME ERROR] Không thể chụp ảnh màn hình giả lập! Vui lòng đảm bảo:");
                Console.WriteLine("  1. Giả lập BlueStacks / MEmu đã được bật.");
                Console.WriteLine("  2. Tính năng 'Android Debug Bridge (ADB)' đã được kích hoạt trong cài đặt giả lập.");
                Console.ResetColor();
            }
            else
            {
                Console.WriteLine($"[LIVE-HOME] Chụp ảnh thành công! Kích thước ảnh: {screenshot.Width}x{screenshot.Height}");
                Console.WriteLine("[LIVE-HOME] Đang nhận diện tài nguyên LÀNG CHÍNH hiển thị ở góc trên bên phải...");

                // Gọi hàm quét IsTarget để đọc số làng chính
                var res = IsTarget.ExtractHomeResources(adb, vision);

                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine("\n============= KẾT QUẢ QUÉT LÀNG CHÍNH LIVE =============");
                Console.WriteLine($"👉 Vàng (Gold) quét được:       {res.Gold:N0}");
                Console.WriteLine($"👉 Dầu hồng (Elixir) quét được:  {res.Elixir:N0}");
                Console.WriteLine($"👉 Dầu đen (Dark Elixir) quét được: {res.DarkElixir:N0}");
                Console.WriteLine("========================================================");
                Console.ResetColor();
                
                // Lưu lại ảnh chụp màn hình để debug
                string debugPath = Path.Combine(AppContext.BaseDirectory, "live_home_screenshot_debug.png");
                Cv2.ImWrite(debugPath, screenshot);
                Console.WriteLine($"[LIVE-HOME] Đã lưu ảnh chụp debug thực tế tại: {debugPath}");
            }

            Console.WriteLine("\n==============================================");
            Console.WriteLine(" QUÉT TÀI NGUYÊN LÀNG CHÍNH LIVE HOÀN TẤT. NHẤN PHÍM BẤT KỲ ĐỂ QUAY LẠI MENU.");
            Console.WriteLine("==============================================");
            try { Console.ReadKey(); } catch {}
        }

        private static void RunLiveZoomOutTest(string configPath)
        {
            try { Console.Clear(); } catch {}
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine("==================================================================");
            Console.WriteLine("      THỬ NGHIỆM THU NHỎ BẢN ĐỒ (ZOOM OUT) LIVE TRÊN GIẢ LẬP       ");
            Console.WriteLine("==================================================================");
            Console.ResetColor();

            Console.WriteLine("[LIVE-ZOOM] Đang khởi chạy Máy trạng thái hỗ trợ...");
            try
            {
                CVAutomationFramework framework = new CVAutomationFramework(configPath);
                framework.ZoomOut();
                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine("\n[LIVE-ZOOM] Đã thực hiện xong lệnh Zoom Out. Hãy kiểm tra màn hình giả lập!");
                Console.ResetColor();
            }
            catch (Exception ex)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"[LIVE-ZOOM ERROR] Lỗi: {ex.Message}");
                Console.ResetColor();
            }

            Console.WriteLine("\n==============================================");
            Console.WriteLine(" THỬ NGHIỆM ZOOM OUT HOÀN TẤT. NHẤN PHÍM BẤT KỲ ĐỂ QUAY LẠI MENU.");
            Console.WriteLine("==============================================");
            try { Console.ReadKey(); } catch {}
        }

        private static void RunLiveSmartTrainTest(string configPath, string templatesPath)
        {
            try { Console.Clear(); } catch {}
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine("==================================================================");
            Console.WriteLine("         CHẠY SMART TRAIN MỘT LẦN ĐỂ KIỂM TRA COUNT OCR LIVE       ");
            Console.WriteLine("==================================================================");
            Console.ResetColor();

            try
            {
                using JsonDocument doc = JsonDocument.Parse(File.ReadAllText(configPath));
                JsonElement cfg = doc.RootElement.Clone();

                JsonElement devConfig = cfg.GetProperty("device_connection");
                string host = devConfig.GetProperty("host").GetString() ?? "127.0.0.1";
                int port = devConfig.GetProperty("port").GetInt32();

                ADBHelper adb = new ADBHelper(host, port);
                VisionEngine vision = new VisionEngine(templatesPath);
                Training training = new Training(adb, templatesPath, vision);

                training.SmartTrain(cfg);
            }
            catch (Exception ex)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"[LIVE-SMART-TRAIN ERROR] Lỗi: {ex.Message}");
                Console.ResetColor();
            }

            Console.WriteLine("\n==============================================");
            Console.WriteLine(" SMART TRAIN LIVE TEST HOÀN TẤT. NHẤN PHÍM BẤT KỲ ĐỂ QUAY LẠI MENU.");
            Console.WriteLine("==============================================");
            try { Console.ReadKey(); } catch {}
        }

        private static void RunLiveBootRecoveryTest(string configPath)
        {
            try { Console.Clear(); } catch {}
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine("==================================================================");
            Console.WriteLine("        CHẠY BOOT RECOVERY: FORCE-STOP RỒI MỞ LẠI CLASH OF CLANS  ");
            Console.WriteLine("==================================================================");
            Console.ResetColor();

            try
            {
                CVAutomationFramework framework = new CVAutomationFramework(configPath);
                framework.BootRecovery();
                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine("\n[BOOT-RECOVERY] Đã gửi force-stop, launch và tap dismiss popup.");
                Console.ResetColor();
            }
            catch (Exception ex)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"[BOOT-RECOVERY ERROR] Lỗi: {ex.Message}");
                Console.ResetColor();
            }

            Console.WriteLine("\n==============================================");
            Console.WriteLine(" BOOT RECOVERY LIVE TEST HOÀN TẤT. NHẤN PHÍM BẤT KỲ ĐỂ QUAY LẠI MENU.");
            Console.WriteLine("==============================================");
            try { Console.ReadKey(); } catch {}
        }

        private static void RunLiveWorkflowTemplateTest(string configPath)
        {
            PrintBanner("WORKFLOW TEMPLATE", "Chạy 5 chu kỳ live và lưu log chi tiết");

            Directory.CreateDirectory("logs");
            string logPath = Path.Combine("logs", $"workflow_template_{DateTime.Now:yyyyMMdd_HHmmss}.log");
            RunSessionStats stats = new RunSessionStats();

            TextWriter originalOut = Console.Out;
            using StreamWriter fileWriter = new StreamWriter(logPath, append: false, Encoding.UTF8) { AutoFlush = true };
            using TeeTextWriter teeWriter = new TeeTextWriter(originalOut, fileWriter, stats);
            Console.SetOut(teeWriter);

            try
            {
                Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] [TEST] Workflow template run started.");
                Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] [TEST] Config: {Path.GetFullPath(configPath)}");
                Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] [TEST] Reference log: {Path.GetFullPath("logtemplate.md")}");
                const int workflowCycleCount = 5;
                Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] [TEST] Mode: {workflowCycleCount} cycles, detailed console/file logging.");

                CVAutomationFramework framework = new CVAutomationFramework(configPath);
                using CancellationTokenSource cts = new CancellationTokenSource();
                framework.RunCyclesForTest(workflowCycleCount, cts.Token);

                Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] [TEST] Workflow template run finished.");
                PrintSessionSummary(stats);
            }
            catch (Exception ex)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] [WORKFLOW-TEST ERROR] {ex.GetType().Name}: {ex.Message}");
                Console.WriteLine(ex.StackTrace);
                Console.ResetColor();
            }
            finally
            {
                Console.SetOut(originalOut);
            }

            Console.WriteLine($"\n[WORKFLOW-TEST] Đã lưu log debug tại: {Path.GetFullPath(logPath)}");
            PauseForMenu();
        }

        private static void PrintSessionSummary(RunSessionStats stats)
        {
            Console.WriteLine();
            WriteSummaryRule(ConsoleColor.DarkCyan);
            WriteSummaryHeader("THONG KE LOOT SESSION");
            WriteSummaryRule(ConsoleColor.DarkCyan);

            if (stats.Attacks == 0)
            {
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine("  Chưa ghi nhận trận đánh nào trong session này.");
                Console.ResetColor();
                WriteSummaryRule(ConsoleColor.DarkGray);
                return;
            }

            TimeSpan elapsed = stats.Elapsed;
            double hours = Math.Max(elapsed.TotalHours, 1.0 / 3600.0);

            Console.WriteLine($"  {"Thời gian chạy",-18}: {FormatDuration(elapsed)}");
            Console.WriteLine($"  {"Số trận",-18}: {stats.Attacks:N0}");
            Console.WriteLine($"  {"Sao trung bình",-18}: {stats.AverageStars:F2}");
            Console.WriteLine();
            Console.ForegroundColor = ConsoleColor.Gray;
            Console.WriteLine($"  {"Tài nguyên",-12} {"Tổng",-16} {"TB/trận",-16} {"Ước tính/giờ",-16}");
            Console.ResetColor();
            Console.WriteLine("  " + new string('-', SummaryWidth - 4));
            PrintResourceStat("Gold", stats.Gold, stats.Attacks, hours);
            PrintResourceStat("Elixir", stats.Elixir, stats.Attacks, hours);
            PrintResourceStat("Dark", stats.DarkElixir, stats.Attacks, hours);
            WriteSummaryRule(ConsoleColor.DarkGray);
        }

        private static void PrintResourceStat(string name, long total, int attacks, double hours)
        {
            Console.WriteLine($"  {name,-12} {total,16:N0} {total / Math.Max(attacks, 1),16:N0} {total / hours,16:N0}");
        }

        private static void WriteSummaryRule(ConsoleColor color)
        {
            Console.ForegroundColor = color;
            Console.WriteLine(new string('=', SummaryWidth));
            Console.ResetColor();
        }

        private static void WriteSummaryHeader(string title)
        {
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine(Center(title, SummaryWidth));
            Console.ResetColor();
        }

        private static string FormatCompactNumber(long value)
        {
            long abs = Math.Abs(value);
            if (abs >= 1_000_000_000)
            {
                return $"{value / 1_000_000_000.0:F2}B";
            }

            if (abs >= 1_000_000)
            {
                return $"{value / 1_000_000.0:F2}M";
            }

            if (abs >= 1_000)
            {
                return $"{value / 1_000.0:F1}K";
            }

            return value.ToString("N0");
        }

        private static string FormatDuration(TimeSpan elapsed)
        {
            if (elapsed.TotalHours >= 1)
            {
                return $"{(int)elapsed.TotalHours:D2}:{elapsed.Minutes:D2}:{elapsed.Seconds:D2}";
            }

            return $"{elapsed.Minutes:D2}:{elapsed.Seconds:D2}";
        }

        private static void PauseForMenu()
        {
            Console.WriteLine("\nNhấn phím bất kỳ để quay lại menu.");
            try { Console.ReadKey(intercept: true); } catch {}
        }

        private sealed record MenuItem(
            string Key,
            string Title,
            string Description,
            string Category,
            Action<string, string>? Action);

        private sealed class RunSessionStats
        {
            private static readonly Regex BattleStatsRegex = new(
                @"\[STATS\]\s+Battle result:\s+(?<stars>\d+)\s+star\(s\),\s+gained\s+Gold=(?<gold>[\d,]+)\s+Elixir=(?<elixir>[\d,]+)\s+Dark=(?<dark>[\d,]+)",
                RegexOptions.Compiled | RegexOptions.CultureInvariant);

            private readonly DateTime _startedAt = DateTime.Now;
            private readonly object _sync = new();

            private int _attacks;
            private int _stars;
            private long _gold;
            private long _elixir;
            private long _darkElixir;

            public int Attacks { get { lock (_sync) return _attacks; } }
            public int Stars { get { lock (_sync) return _stars; } }
            public long Gold { get { lock (_sync) return _gold; } }
            public long Elixir { get { lock (_sync) return _elixir; } }
            public long DarkElixir { get { lock (_sync) return _darkElixir; } }
            public TimeSpan Elapsed => DateTime.Now - _startedAt;
            public double AverageStars => Attacks == 0 ? 0 : (double)Stars / Attacks;
            public double Hours => Math.Max(Elapsed.TotalHours, 1.0 / 3600.0);

            public bool ObserveLine(string? line)
            {
                if (string.IsNullOrWhiteSpace(line))
                {
                    return false;
                }

                Match match = BattleStatsRegex.Match(line);
                if (!match.Success)
                {
                    return false;
                }

                lock (_sync)
                {
                    _attacks++;
                    _stars += ParseInt(match.Groups["stars"].Value);
                    _gold += ParseLong(match.Groups["gold"].Value);
                    _elixir += ParseLong(match.Groups["elixir"].Value);
                    _darkElixir += ParseLong(match.Groups["dark"].Value);
                }

                return true;
            }

            private static int ParseInt(string value)
            {
                return int.TryParse(value.Replace(",", ""), out int parsed) ? parsed : 0;
            }

            private static long ParseLong(string value)
            {
                return long.TryParse(value.Replace(",", ""), out long parsed) ? parsed : 0;
            }
        }

        private sealed class LiveStatsPanel : IDisposable
        {
            private const int Width = 42;
            private const int Top = 1;
            private readonly RunSessionStats _stats;
            private readonly object _renderLock = new();
            private readonly Timer _timer;
            private bool _disposed;

            public LiveStatsPanel(RunSessionStats stats)
            {
                _stats = stats;
                _timer = new Timer(_ => Render(), null, Timeout.Infinite, Timeout.Infinite);
            }

            public void Render()
            {
                lock (_renderLock)
                {
                    if (_disposed || Console.IsOutputRedirected)
                    {
                        return;
                    }

                    int left = Math.Max(0, Console.WindowWidth - Width - 1);
                    int originalLeft = Console.CursorLeft;
                    int originalTop = Console.CursorTop;
                    ConsoleColor oldForeground = Console.ForegroundColor;

                    try
                    {
                        string[] lines = BuildLines();
                        for (int i = 0; i < lines.Length; i++)
                        {
                            Console.SetCursorPosition(left, Top + i);
                            Console.ForegroundColor = i == 0 ? ConsoleColor.Cyan : ConsoleColor.Gray;
                            Console.Write(lines[i].PadRight(Width));
                        }

                        int safeTop = Math.Min(originalTop, Console.BufferHeight - 1);
                        int safeLeft = Math.Min(originalLeft, Math.Max(0, Console.WindowWidth - 1));
                        Console.SetCursorPosition(safeLeft, safeTop);
                    }
                    catch
                    {
                        // Console resize can race with rendering; skip this tick.
                    }
                    finally
                    {
                        Console.ForegroundColor = oldForeground;
                    }
                }
            }

            public void Start()
            {
                _timer.Change(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1));
            }

            public void Stop()
            {
                _timer.Change(Timeout.Infinite, Timeout.Infinite);
            }

            private string[] BuildLines()
            {
                double hours = _stats.Hours;
                return new[]
                {
                    "+----------------------------------------+",
                    "|              LOOT SESSION              |",
                    "+----------------------------------------+",
                    PanelLine("Elapsed", FormatDuration(_stats.Elapsed)),
                    PanelLine("Attacks", _stats.Attacks.ToString("N0")),
                    PanelLine("Avg stars", _stats.AverageStars.ToString("F2")),
                    "+----------------------------------------+",
                    PanelLine("Gold", FormatCompactNumber(_stats.Gold)),
                    PanelLine("Elixir", FormatCompactNumber(_stats.Elixir)),
                    PanelLine("Dark", FormatCompactNumber(_stats.DarkElixir)),
                    PanelLine("Gold/hr", FormatCompactNumber((long)(_stats.Gold / hours))),
                    PanelLine("Elixir/hr", FormatCompactNumber((long)(_stats.Elixir / hours))),
                    PanelLine("Dark/hr", FormatCompactNumber((long)(_stats.DarkElixir / hours))),
                    "+----------------------------------------+"
                };
            }

            private static string PanelLine(string label, string value)
            {
                return $"| {label,-12} {value,25} |";
            }

            public void Dispose()
            {
                _disposed = true;
                _timer.Dispose();
            }
        }

        private sealed class StatsTrackingTextWriter : TextWriter
        {
            private readonly TextWriter _inner;
            private readonly RunSessionStats _stats;
            private readonly LiveStatsPanel? _panel;

            public StatsTrackingTextWriter(TextWriter inner, RunSessionStats stats, LiveStatsPanel? panel = null)
            {
                _inner = inner;
                _stats = stats;
                _panel = panel;
            }

            public override Encoding Encoding => _inner.Encoding;

            public override void Write(char value)
            {
                _inner.Write(value);
            }

            public override void Write(string? value)
            {
                _inner.Write(value);
            }

            public override void WriteLine(string? value)
            {
                bool changed = _stats.ObserveLine(value);
                _inner.WriteLine(value);
                if (changed)
                {
                    _panel?.Render();
                }
            }
        }

        private sealed class TeeTextWriter : TextWriter
        {
            private readonly TextWriter _console;
            private readonly TextWriter _file;
            private readonly RunSessionStats? _stats;

            public TeeTextWriter(TextWriter console, TextWriter file, RunSessionStats? stats = null)
            {
                _console = console;
                _file = file;
                _stats = stats;
            }

            public override Encoding Encoding => _console.Encoding;

            public override void Write(char value)
            {
                _console.Write(value);
                _file.Write(value);
            }

            public override void Write(string? value)
            {
                _console.Write(value);
                _file.Write(value);
            }

            public override void WriteLine(string? value)
            {
                _stats?.ObserveLine(value);
                _console.WriteLine(value);
                _file.WriteLine(value);
            }

            protected override void Dispose(bool disposing)
            {
                if (disposing)
                {
                    _file.Flush();
                }

                base.Dispose(disposing);
            }
        }
    }
}
