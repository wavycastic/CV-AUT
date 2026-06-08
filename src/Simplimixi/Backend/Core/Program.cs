using System;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using Timer = System.Threading.Timer;

namespace CvAut
{
    /// <summary>
    /// Điểm khởi đầu chính (Entry Point) cho chương trình điều khiển console hoặc khởi chạy giao diện WPF.
    /// Cung cấp menu console tương tác để chạy các kiểm thử trực tiếp (live test) hoặc ngoại tuyến (offline test),
    /// và hiển thị panel thống kê loot thời gian thực trên màn hình console.
    /// </summary>
    class Program
    {
        // Chiều rộng cố định của khung Menu chính
        private const int MenuWidth = 92;
        private static readonly string UserDataDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "SimpliMixi");
        private static readonly string WritableLogsDirectory = Path.Combine(UserDataDirectory, "logs");
        private static readonly string UserConfigPath = Path.Combine(UserDataDirectory, "Config", "test_config.json");

        // Chiều rộng cố định của khung Thống kê kết thúc phiên chơi
        private const int SummaryWidth = 78;

        // Định nghĩa danh sách các chức năng trong Menu điều khiển console
        private static readonly MenuItem[] MainMenu =
        {
            new("1", "Offline test", "Run OCR and attack checks from sample images", "Test", RunOfflineTestFromMenu),
            new("2", "FSM live loop", "Run the bot with Pause/Resume/Stop controls", "Bot", RunLiveFSMLoopFromMenu),
            new("3", "Scout target", "Capture the emulator and read target loot", "Live OCR", RunLiveScoutingTestFromMenu),
            new("4", "Home resources", "Read home-base resources from the top-right panel", "Live OCR", RunLiveHomeBaseTestFromMenu),
            new("5", "Zoom Out live", "Send a zoom-out gesture to the emulator", "Device", RunLiveZoomOutTestFromMenu),
            new("6", "Smart Train", "Check troop OCR and train logic once", "Army", RunLiveSmartTrainTestFromMenu),
            new("7", "Boot Recovery", "Force-stop and relaunch Clash of Clans", "Device", RunLiveBootRecoveryTestFromMenu),
            new("8", "Run bot forever", "Run continuously: P pause, R resume, S stop", "Bot", RunInfiniteBotFromMenu),
            new("9", "Exit", "Close the application", "System", null)
        };

        /// <summary>
        /// Phương thức Main bắt đầu vòng đời ứng dụng.
        /// Mặc định sẽ khởi động giao diện WPF. Nếu có tham số "--console", ứng dụng sẽ chạy trên Console CLI.
        /// </summary>
        /// <param name="args">Các đối số dòng lệnh truyền vào.</param>
        [STAThread]
        static void Main(string[] args)
        {
            Console.WriteLine($"[BUILD-ID] SimpliMixi v0.6.2 binary loaded at {DateTime.Now:yyyy-MM-dd HH:mm:ss} | base={AppContext.BaseDirectory}");

            // Mặc định khởi chạy giao diện WPF nếu không truyền cờ --console
            if (!args.Any(a => string.Equals(a, "--console", StringComparison.OrdinalIgnoreCase)))
            {
                RunWpfApp();
                return;
            }

            try { Console.Clear(); } catch { }

            string configPath = UserConfigPath;
            string templatesPath = Path.Combine(AppContext.BaseDirectory, "assets", "Templates");

            // Xử lý cờ chẩn đoán giao diện quân đội riêng biệt
            if (args.Any(a => string.Equals(a, "--diagnose-saved-army-window", StringComparison.OrdinalIgnoreCase)))
            {
                BackendDiagnostics.DiagnoseSavedArmyWindow("live_army_window_debug.png", templatesPath);
                return;
            }

            // Vòng lặp hiển thị Menu chính cho console
            while (true)
            {
                MenuItem selected = ReadMainMenuChoice();
                if (selected.Action == null)
                {
                    WriteInfoLine("Closing application. Goodbye.");
                    break;
                }

                selected.Action(configPath, templatesPath);
            }
        }

        /// <summary>
        /// Khởi chạy ứng dụng đồ họa WPF bằng lớp App.
        /// </summary>
        [STAThread]
        private static void RunWpfApp()
        {
            var app = new CvAut.WpfApp.App();
            app.InitializeComponent();
            app.Run();
        }

        // Các hàm chuyển hướng gọi chức năng tương ứng của Menu
        private static void RunOfflineTestFromMenu(string _, string templatesPath) => RunOfflineTest(templatesPath);
        private static void RunLiveFSMLoopFromMenu(string configPath, string _) => RunLiveFSMLoop(configPath);
        private static void RunLiveScoutingTestFromMenu(string _, string templatesPath) => RunLiveScoutingTest(templatesPath);
        private static void RunLiveHomeBaseTestFromMenu(string _, string templatesPath) => RunLiveHomeBaseTest(templatesPath);
        private static void RunLiveZoomOutTestFromMenu(string configPath, string _) => RunLiveZoomOutTest(configPath);
        private static void RunLiveSmartTrainTestFromMenu(string configPath, string templatesPath) => RunLiveSmartTrainTest(configPath, templatesPath);
        private static void RunLiveBootRecoveryTestFromMenu(string configPath, string _) => RunLiveBootRecoveryTest(configPath);
        private static void RunLiveWorkflowTemplateTestFromMenu(string configPath, string _) => RunLiveWorkflowTemplateTest(configPath);
        private static void RunInfiniteBotFromMenu(string configPath, string _) => RunLiveFSMLoop(configPath);

        /// <summary>
        /// Đọc lựa chọn của người dùng trên Menu bằng phím mũi tên hoặc phím nóng từ 1 đến 9.
        /// </summary>
        private static MenuItem ReadMainMenuChoice()
        {
            int selectedIndex = 7; // Mặc định trỏ đến lựa chọn chạy bot liên tục

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

        /// <summary>
        /// Vẽ toàn bộ Menu chính của console CLI với phong cách hộp thoại đẹp mắt.
        /// </summary>
        private static void DrawMainMenu(int selectedIndex)
        {
            try { Console.Clear(); } catch { }

            Console.Title = "SimpliMixi v0.6.2 Control Console";
            WriteRule(ConsoleColor.DarkCyan);
            WriteCentered("SIMPLIMIXI v0.6.2 CONTROL CONSOLE", ConsoleColor.Cyan);
            WriteCentered("Clash of Clans automation | FSM + OCR + ADB", ConsoleColor.Gray);
            WriteRule(ConsoleColor.DarkCyan);
            Console.WriteLine();
            WriteStatusBar("UP/DOWN select", "ENTER run", "1-9 shortcuts", "P/R/S while bot runs");
            Console.WriteLine();

            for (int i = 0; i < MainMenu.Length; i++)
            {
                MenuItem item = MainMenu[i];
                bool selected = i == selectedIndex;
                DrawMenuRow(item, selected);
            }

            Console.WriteLine();
            WriteRule(ConsoleColor.DarkGray);
            WriteMuted("By default, the app opens the WPF UI. Use --console to show this screen.");
            WriteMuted("Bot shortcuts: P = Pause, R = Resume, S = Stop.");
        }

        /// <summary>
        /// In tiêu đề của một chức năng kiểm thử khi được thực thi.
        /// </summary>
        private static void PrintBanner(string title, string? subtitle = null)
        {
            try { Console.Clear(); } catch { }
            WriteRule(ConsoleColor.DarkCyan);
            WriteCentered(title, ConsoleColor.Cyan);
            if (!string.IsNullOrWhiteSpace(subtitle))
            {
                WriteCentered(subtitle, ConsoleColor.Gray);
            }
            WriteRule(ConsoleColor.DarkCyan);
            Console.WriteLine();
        }

        /// <summary>
        /// Căn giữa một chuỗi văn bản theo chiều rộng quy định.
        /// </summary>
        private static string Center(string text, int width)
        {
            if (text.Length >= width) return text;
            int left = (width - text.Length) / 2;
            return new string(' ', left) + text;
        }

        /// <summary>
        /// Vẽ một dòng chức năng trong Menu chính, đổi màu sắc nổi bật dòng đang trỏ chuột.
        /// </summary>
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

        /// <summary>
        /// Vẽ một đường kẻ ngang toàn màn hình.
        /// </summary>
        private static void WriteRule(ConsoleColor color)
        {
            Console.ForegroundColor = color;
            Console.WriteLine(new string('=', MenuWidth));
            Console.ResetColor();
        }

        /// <summary>
        /// Ghi một dòng chữ căn giữa.
        /// </summary>
        private static void WriteCentered(string text, ConsoleColor color)
        {
            Console.ForegroundColor = color;
            Console.WriteLine(Center(text, MenuWidth));
            Console.ResetColor();
        }

        /// <summary>
        /// Vẽ thanh trạng thái nền màu xanh đậm dưới tiêu đề.
        /// </summary>
        private static void WriteStatusBar(params string[] segments)
        {
            Console.ForegroundColor = ConsoleColor.Black;
            Console.BackgroundColor = ConsoleColor.DarkCyan;
            Console.Write("  ");
            Console.Write(string.Join("  |  ", segments).PadRight(MenuWidth - 4));
            Console.WriteLine("  ");
            Console.ResetColor();
        }

        /// <summary>
        /// Ghi chữ màu xám tối.
        /// </summary>
        private static void WriteMuted(string text)
        {
            Console.ForegroundColor = ConsoleColor.DarkGray;
            Console.WriteLine(text);
            Console.ResetColor();
        }

        /// <summary>
        /// Ghi chữ màu xanh nhạt.
        /// </summary>
        private static void WriteInfoLine(string text)
        {
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine(text);
            Console.ResetColor();
        }

        /// <summary>
        /// Cắt ngắn chuỗi văn bản nếu độ dài vượt quá độ rộng quy định và thêm dấu ba chấm.
        /// </summary>
        private static string TrimToWidth(string text, int width)
        {
            if (text.Length <= width)
            {
                return text;
            }

            return text[..Math.Max(0, width - 3)] + "...";
        }

        /// <summary>
        /// Thực thi bài kiểm thử ngoại tuyến (Offline Mock Test).
        /// Đọc hình ảnh mẫu sẵn có trong Templates để kiểm nghiệm hệ thống OCR nhị phân và mô phỏng logic thả quân.
        /// </summary>
        private static void RunOfflineTest(string templatesPath)
        {
            try { Console.Clear(); } catch { }
            Console.WriteLine("=== OFFLINE MOCK TEST ===");

            BackendDiagnostics.RunOfflineMockTest(templatesPath);

            Console.WriteLine("\n==============================================");
            Console.WriteLine(" C# MODULE TEST COMPLETE. PRESS ANY KEY TO RETURN TO THE MENU.");
            Console.WriteLine("\n==============================================");
            try { Console.ReadKey(); } catch { }
        }

        /// <summary>
        /// Khởi chạy chu kỳ chạy FSM Live của bot trực tiếp trên cửa sổ console.
        /// Chuyển hướng đầu ra console thông qua StatsTrackingTextWriter và hiển thị LiveStatsPanel định kỳ ở góc bên phải.
        /// </summary>
        private static void RunLiveFSMLoop(string configPath)
        {
            PrintBanner("FSM LIVE LOOP", "Runs until you press S");

            RunSessionStats stats = new RunSessionStats();
            TextWriter originalOut = Console.Out;
            using LiveStatsPanel livePanel = new LiveStatsPanel(stats);
            using StatsTrackingTextWriter statsWriter = new StatsTrackingTextWriter(originalOut, stats, livePanel);
            Console.SetOut(statsWriter);

            IAutomationRunner? framework = null;

            try
            {
                framework = new AutomationRunner(configPath);
                livePanel.Render();
                livePanel.Start();
                framework.Start();

                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine("\nBot is running in the background...");
                Console.WriteLine("  P  Pause");
                Console.WriteLine("  R  Resume");
                Console.WriteLine("  S  Stop and return to menu");
                Console.ResetColor();

                // Lắng nghe phím nhấn trực tiếp từ console để kiểm soát bot
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

        /// <summary>
        /// Thực hiện chụp màn hình giả lập trực tiếp qua ADB và quét đọc tài nguyên nhà đối thủ (khi tìm trận).
        /// </summary>
        private static void RunLiveScoutingTest(string templatesPath)
        {
            try { Console.Clear(); } catch { }
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine("==================================================================");
            Console.WriteLine("             LIVE TARGET RESOURCE SCAN FROM THE EMULATOR           ");
            Console.WriteLine("==================================================================");
            Console.ResetColor();

            string debugPath = Path.Combine(WritableLogsDirectory, "live_screenshot_debug.png");
            BackendDiagnostics.RunLiveScoutingTest(templatesPath, debugPath);

            Console.WriteLine("\n==============================================");
            Console.WriteLine(" LIVE RESOURCE SCAN COMPLETE. PRESS ANY KEY TO RETURN TO THE MENU.");
            Console.WriteLine("\n==============================================");
            try { Console.ReadKey(); } catch { }
        }

        /// <summary>
        /// Thực hiện chụp màn hình giả lập trực tiếp qua ADB và quét đọc tài nguyên trong Làng chính (Home Base) của người chơi.
        /// </summary>
        private static void RunLiveHomeBaseTest(string templatesPath)
        {
            try { Console.Clear(); } catch { }
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine("==================================================================");
            Console.WriteLine("            LIVE HOME-BASE RESOURCE SCAN FROM THE EMULATOR          ");
            Console.WriteLine("==================================================================");
            Console.ResetColor();

            string debugPath = Path.Combine(WritableLogsDirectory, "live_home_screenshot_debug.png");
            BackendDiagnostics.RunLiveHomeBaseTest(templatesPath, debugPath);

            Console.WriteLine("\n==============================================");
            Console.WriteLine(" LIVE HOME RESOURCE SCAN COMPLETE. PRESS ANY KEY TO RETURN TO THE MENU.");
            Console.WriteLine("\n==============================================");
            try { Console.ReadKey(); } catch { }
        }

        /// <summary>
        /// Gửi cử chỉ thu nhỏ camera (Zoom Out) trực tiếp tới giả lập thông qua CVAutomationFramework.
        /// </summary>
        private static void RunLiveZoomOutTest(string configPath)
        {
            try { Console.Clear(); } catch { }
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine("==================================================================");
            Console.WriteLine("                    LIVE ZOOM OUT TEST ON THE EMULATOR             ");
            Console.WriteLine("==================================================================");
            Console.ResetColor();

            Console.WriteLine("[LIVE-ZOOM] Initializing automation framework...");
            try
            {
                BackendDiagnostics.ZoomOut(configPath);
                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine("\n[LIVE-ZOOM] Zoom-out command completed. Check the emulator screen.");
                Console.ResetColor();
            }
            catch (Exception ex)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"[LIVE-ZOOM ERROR] {ex.Message}");
                Console.ResetColor();
            }

            Console.WriteLine("\n==============================================");
            Console.WriteLine(" ZOOM OUT TEST COMPLETE. PRESS ANY KEY TO RETURN TO THE MENU.");
            Console.WriteLine("\n==============================================");
            try { Console.ReadKey(); } catch { }
        }

        /// <summary>
        /// Chạy tính năng luyện quân thông minh một lần duy nhất từ cấu hình chính để kiểm định OCR doanh trại.
        /// </summary>
        private static void RunLiveSmartTrainTest(string configPath, string templatesPath)
        {
            try { Console.Clear(); } catch { }
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine("==================================================================");
            Console.WriteLine("                     SMART TRAIN LIVE OCR CHECK                    ");
            Console.WriteLine("==================================================================");
            Console.ResetColor();

            try
            {
                BackendDiagnostics.RunSmartTrainTest(configPath, templatesPath);
            }
            catch (Exception ex)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"[LIVE-SMART-TRAIN ERROR] {ex.Message}");
                Console.ResetColor();
            }

            Console.WriteLine("\n==============================================");
            Console.WriteLine(" SMART TRAIN LIVE TEST COMPLETE. PRESS ANY KEY TO RETURN TO THE MENU.");
            Console.WriteLine("\n==============================================");
            try { Console.ReadKey(); } catch { }
        }

        /// <summary>
        /// Khởi chạy chuỗi hồi phục giả lập: Force-Stop game CoC rồi mở lại và dismiss popup ban đầu.
        /// </summary>
        private static void RunLiveBootRecoveryTest(string configPath)
        {
            try { Console.Clear(); } catch { }
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine("==================================================================");
            Console.WriteLine("              BOOT RECOVERY: FORCE-STOP AND RELAUNCH COC          ");
            Console.WriteLine("==================================================================");
            Console.ResetColor();

            try
            {
                BackendDiagnostics.BootRecovery(configPath);
                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine("\n[BOOT-RECOVERY] Force-stop, launch, and dismiss steps completed.");
                Console.ResetColor();
            }
            catch (Exception ex)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"[BOOT-RECOVERY ERROR] {ex.Message}");
                Console.ResetColor();
            }

            Console.WriteLine("\n==============================================");
            Console.WriteLine(" BOOT RECOVERY LIVE TEST COMPLETE. PRESS ANY KEY TO RETURN TO THE MENU.");
            Console.WriteLine("\n==============================================");
            try { Console.ReadKey(); } catch { }
        }

        /// <summary>
        /// Chạy thử nghiệm kịch bản 5 chu kỳ tự động hóa, đồng thời lưu nhật ký chi tiết ra tệp tin logs/.
        /// </summary>
        private static void RunLiveWorkflowTemplateTest(string configPath)
        {
            PrintBanner("WORKFLOW TEMPLATE", "Runs 5 live cycles and saves a detailed log");

            Directory.CreateDirectory(WritableLogsDirectory);
            string logPath = Path.Combine(WritableLogsDirectory, $"workflow_template_{DateTime.Now:yyyyMMdd_HHmmss}.log");
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

                using CancellationTokenSource cts = new CancellationTokenSource();
                BackendDiagnostics.RunWorkflowTemplate(configPath, workflowCycleCount, cts.Token);

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

            Console.WriteLine($"\n[WORKFLOW-TEST] Debug log saved: {Path.GetFullPath(logPath)}");
            PauseForMenu();
        }

        /// <summary>
        /// In tóm tắt số liệu thu được trong phiên hoạt động hiện tại (Attacks, Stars, Gold, Elixir, DE, Rates).
        /// </summary>
        private static void PrintSessionSummary(RunSessionStats stats)
        {
            Console.WriteLine();
            WriteSummaryRule(ConsoleColor.DarkCyan);
            WriteSummaryHeader("LOOT SESSION SUMMARY");
            WriteSummaryRule(ConsoleColor.DarkCyan);

            if (stats.Attacks == 0)
            {
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine("  No attacks recorded in this session.");
                Console.ResetColor();
                WriteSummaryRule(ConsoleColor.DarkGray);
                return;
            }

            TimeSpan elapsed = stats.Elapsed;
            double hours = Math.Max(elapsed.TotalHours, 1.0 / 3600.0);

            Console.WriteLine($"  {"Elapsed",-18}: {FormatDuration(elapsed)}");
            Console.WriteLine($"  {"Attacks",-18}: {stats.Attacks:N0}");
            Console.WriteLine($"  {"Average stars",-18}: {stats.AverageStars:F2}");
            Console.WriteLine();
            Console.ForegroundColor = ConsoleColor.Gray;
            Console.WriteLine($"  {"Resource",-12} {"Total",-16} {"Avg/attack",-16} {"Est/hour",-16}");
            Console.ResetColor();
            Console.WriteLine("  " + new string('-', SummaryWidth - 4));
            PrintResourceStat("Gold", stats.Gold, stats.Attacks, hours);
            PrintResourceStat("Elixir", stats.Elixir, stats.Attacks, hours);
            PrintResourceStat("Dark", stats.DarkElixir, stats.Attacks, hours);
            WriteSummaryRule(ConsoleColor.DarkGray);
        }

        /// <summary>
        /// In dòng thống kê cho một loại tài nguyên cụ thể.
        /// </summary>
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

        /// <summary>
        /// Hỗ trợ định dạng rút gọn số lượng lớn (M, K, B).
        /// </summary>
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

        /// <summary>
        /// Định dạng khoảng thời gian Elapsed dạng HH:mm:ss.
        /// </summary>
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
            Console.WriteLine("\nPress any key to return to the menu.");
            try { Console.ReadKey(intercept: true); } catch { }
        }

        /// <summary>
        /// Khai báo cấu trúc một phần tử menu trong console.
        /// </summary>
        private sealed record MenuItem(
            string Key,
            string Title,
            string Description,
            string Category,
            Action<string, string>? Action);

        /// <summary>
        /// Lớp nội bộ theo dõi và phân tích thống kê loot trong phiên hoạt động hiện tại của console bot.
        /// Sử dụng Regular Expression để bắt các dòng ghi nhận kết quả trận đấu dạng "[STATS]".
        /// </summary>
        private sealed class RunSessionStats
        {
            // Biểu thức chính quy phát hiện và bóc tách thông tin trận đấu dạng: "[STATS] Battle result: 3 star(s), gained Gold=450,000 Elixir=320,000 Dark=1,200"
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

            /// <summary>
            /// Lắng nghe các dòng log console viết ra. Nếu dòng log khớp với kết quả trận đấu, tự động cập nhật số liệu tích lũy.
            /// </summary>
            /// <param name="line">Dòng log cần phân tích.</param>
            /// <returns>True nếu dòng log khớp mẫu kết quả trận; ngược lại là False.</returns>
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

        /// <summary>
        /// Bảng hiển thị thông số loot thời gian thực (được render ở góc bên phải của màn hình console).
        /// Sử dụng timer để tự động vẽ lại thông tin định kỳ mỗi giây một lần.
        /// </summary>
        private sealed class LiveStatsPanel : IDisposable
        {
            private const int Width = 42; // Chiều rộng cố định của bảng điều khiển thống kê bên phải
            private const int Top = 1;
            private readonly RunSessionStats _stats;
            private readonly object _renderLock = new();
            private readonly Timer _timer;
            private bool _disposed;

            /// <summary>
            /// Khởi tạo LiveStatsPanel nhận số liệu stats làm nguồn dữ liệu.
            /// </summary>
            public LiveStatsPanel(RunSessionStats stats)
            {
                _stats = stats;
                _timer = new Timer(_ => Render(), null, Timeout.Infinite, Timeout.Infinite);
            }

            /// <summary>
            /// Render giao diện khung bảng thống kê loot sang góc phải màn hình console.
            /// </summary>
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

                        // Phục hồi lại con trỏ chuột về vị trí cũ trên console
                        int safeTop = Math.Min(originalTop, Console.BufferHeight - 1);
                        int safeLeft = Math.Min(originalLeft, Math.Max(0, Console.WindowWidth - 1));
                        Console.SetCursorPosition(safeLeft, safeTop);
                    }
                    catch
                    {
                        // Thao tác đổi kích thước console có thể xảy ra bất đồng bộ, bỏ qua ngoại lệ nếu có
                    }
                    finally
                    {
                        Console.ForegroundColor = oldForeground;
                    }
                }
            }

            /// <summary>
            /// Khởi chạy chu kỳ tự động vẽ lại bảng mỗi giây.
            /// </summary>
            public void Start()
            {
                _timer.Change(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1));
            }

            /// <summary>
            /// Dừng chu kỳ cập nhật tự động.
            /// </summary>
            public void Stop()
            {
                _timer.Change(Timeout.Infinite, Timeout.Infinite);
            }

            /// <summary>
            /// Xây dựng các chuỗi dòng văn bản vẽ khung panel.
            /// </summary>
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

            /// <summary>
            /// Giải phóng timer.
            /// </summary>
            public void Dispose()
            {
                _disposed = true;
                _timer.Dispose();
            }
        }

        /// <summary>
        /// Bộ chuyển hướng viết nhật ký để bóc tách thông tin thống kê loot theo thời gian thực
        /// từ các dòng log console viết ra mà không ảnh hưởng luồng xuất chuẩn gốc.
        /// </summary>
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

        /// <summary>
        /// Bộ phân chia TextWriter (Tee) ghi nhận log console đồng thời ra cả standard output và tệp nhật ký trên đĩa.
        /// </summary>
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
