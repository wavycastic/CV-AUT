using System;
using System.IO;
using System.Text.Json;
using OpenCvSharp;

namespace CvAut
{
    /// <summary>
    /// Chịu trách nhiệm duy nhất: các bài kiểm thử thủ công cho phần thị giác máy tính.
    /// Đây là công cụ chẩn đoán dành cho lập trình viên, không phải đường chạy sản xuất:
    /// chúng tự khởi tạo ADBHelper/VisionEngine riêng thay vì nhận qua DI.
    /// </summary>
    internal static class VisionTestHarness
    {
        /// <summary>
        /// Kiểm tra OCR trên ảnh tĩnh có sẵn, không cần giả lập, rồi thử một lượt tấn công.
        /// </summary>
        public static void RunOfflineMockTest(string templatesPath)
        {
            VisionEngine vision = new VisionEngine(templatesPath);
            string offlineImagePath = Path.Combine(AppContext.BaseDirectory, "assets", "Templates", "ui", "enemy_resources.png");

            if (!File.Exists(offlineImagePath))
            {
                Console.WriteLine($"[ERROR] Offline test image not found: {offlineImagePath}");
                return;
            }

            Console.WriteLine("[TEST-CS] Offline test image found.");
            using Mat testImg = Cv2.ImRead(offlineImagePath, ImreadModes.Color);
            if (testImg.Empty())
            {
                Console.WriteLine("[ERROR] Unable to read or decode the offline test image.");
                return;
            }

            Console.WriteLine("\n=== [TEST-CS] 1. Light OCR check ===");

            int wImg = testImg.Width;
            int hImg = testImg.Height;
            Rect goldRoi = new Rect(Math.Max(0, 55 - 60), Math.Max(0, 117 - 5), Math.Min(wImg, 55 + 196 + 15) - Math.Max(0, 55 - 60), Math.Min(hImg, 117 + 44 + 5) - Math.Max(0, 117 - 5));
            Rect elixirRoi = new Rect(Math.Max(0, 60 - 15), Math.Max(0, 167 - 5), Math.Min(wImg, 60 + 201 + 15) - Math.Max(0, 60 - 15), Math.Min(hImg, 167 + 41 + 5) - Math.Max(0, 167 - 5));
            Rect deRoi = new Rect(Math.Max(0, 73 - 15), Math.Max(0, 214 - 5), Math.Min(wImg, 73 + 110 + 15) - Math.Max(0, 73 - 15), Math.Min(hImg, 214 + 34 + 5) - Math.Max(0, 214 - 5));

            int gold = vision.ExtractNumericalMetrics(testImg, goldRoi, isOffline: true);
            int elixir = vision.ExtractNumericalMetrics(testImg, elixirRoi, isOffline: true);
            int de = vision.ExtractNumericalMetrics(testImg, deRoi, isOffline: true);

            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine($"-> Gold: {gold:N0} (expected: 353,139)");
            Console.WriteLine($"-> Elixir: {elixir:N0} (expected: 664,536)");
            Console.WriteLine($"-> Dark Elixir: {de:N0} (expected: 5,859)");
            Console.ResetColor();

            string homeImagePath = Path.Combine(AppContext.BaseDirectory, "assets", "Templates", "ui", "home.png");
            if (File.Exists(homeImagePath))
            {
                Console.WriteLine("\n=== [TEST-CS] 1.2. Home-base OCR check ===");
                using Mat homeImg = Cv2.ImRead(homeImagePath, ImreadModes.Color);
                if (!homeImg.Empty())
                {
                    int goldHome = vision.ExtractNumericalMetrics(homeImg, new Rect(1310, 30, 200, 36), isOffline: false, useRgbThresh: true);
                    int elixirHome = vision.ExtractNumericalMetrics(homeImg, new Rect(1310, 115, 200, 36), isOffline: false, useRgbThresh: true);
                    int deHome = vision.ExtractNumericalMetrics(homeImg, new Rect(1310, 200, 200, 32), isOffline: false, useRgbThresh: true);

                    Console.ForegroundColor = ConsoleColor.Yellow;
                    Console.WriteLine($"-> Home Gold:        {goldHome:N0} (expected: 12,519,983)");
                    Console.WriteLine($"-> Home Elixir:      {elixirHome:N0} (expected: 12,813,630)");
                    Console.WriteLine($"-> Home Dark Elixir: {deHome:N0} (expected: 240,000)");
                    Console.ResetColor();
                }
            }

            Console.WriteLine("\n=== [TEST-CS] 2. Attack deployment check ===");
            ADBHelper adb = new ADBHelper("127.0.0.1", 5556);
            Attacks attack = new Attacks(adb, vision);
            attack.Run("Dragon_Attack");
        }

        /// <summary>
        /// Chụp màn hình giả lập đang chạy và đọc tài nguyên của làng địch đang do thám.
        /// </summary>
        public static void RunLiveScoutingTest(string templatesPath, string debugPath)
        {
            Console.WriteLine("[LIVE-SCOUT] Initializing ADB connection...");
            ADBHelper adb = new ADBHelper("127.0.0.1", 5556);
            VisionEngine vision = new VisionEngine(templatesPath);

            Console.WriteLine("[LIVE-SCOUT] Capturing emulator screen...");
            using Mat? screenshot = adb.TakeScreenshot();
            if (screenshot == null || screenshot.Empty())
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine("[LIVE-SCOUT ERROR] Unable to capture the emulator screen. Please check:");
                Console.WriteLine("  1. BlueStacks / MEmu is running.");
                Console.WriteLine("  2. Android Debug Bridge (ADB) is enabled in emulator settings.");
                Console.ResetColor();
                return;
            }

            Console.WriteLine($"[LIVE-SCOUT] Screenshot captured: {screenshot.Width}x{screenshot.Height}");
            Console.WriteLine("[LIVE-SCOUT] Reading visible resources...");
            var res = IsTarget.ExtractResources(adb, vision);

            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine("\n================ LIVE SCAN RESULT ================");
            Console.WriteLine($"Gold:        {res.Gold:N0}");
            Console.WriteLine($"Elixir:      {res.Elixir:N0}");
            Console.WriteLine($"Dark Elixir: {res.DarkElixir:N0}");
            Console.WriteLine("===================================================");
            Console.ResetColor();

            Directory.CreateDirectory(Path.GetDirectoryName(debugPath) ?? ".");
            Cv2.ImWrite(debugPath, screenshot);
            Console.WriteLine($"[LIVE-SCOUT] Debug screenshot saved: {debugPath}");
        }

        /// <summary>
        /// Chụp màn hình giả lập đang chạy và đọc tài nguyên của làng nhà.
        /// </summary>
        public static void RunLiveHomeBaseTest(string templatesPath, string debugPath)
        {
            Console.WriteLine("[LIVE-HOME] Initializing ADB connection...");
            ADBHelper adb = new ADBHelper("127.0.0.1", 5556);
            VisionEngine vision = new VisionEngine(templatesPath);

            Console.WriteLine("[LIVE-HOME] Capturing emulator screen...");
            using Mat? screenshot = adb.TakeScreenshot();
            if (screenshot == null || screenshot.Empty())
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine("[LIVE-HOME ERROR] Unable to capture the emulator screen. Please check:");
                Console.WriteLine("  1. BlueStacks / MEmu is running.");
                Console.WriteLine("  2. Android Debug Bridge (ADB) is enabled in emulator settings.");
                Console.ResetColor();
                return;
            }

            Console.WriteLine($"[LIVE-HOME] Screenshot captured: {screenshot.Width}x{screenshot.Height}");
            Console.WriteLine("[LIVE-HOME] Reading home-base resources...");
            var res = IsTarget.ExtractHomeResources(adb, vision);

            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine("\n============= LIVE HOME SCAN RESULT =============");
            Console.WriteLine($"Gold:        {res.Gold:N0}");
            Console.WriteLine($"Elixir:      {res.Elixir:N0}");
            Console.WriteLine($"Dark Elixir: {res.DarkElixir:N0}");
            Console.WriteLine("========================================================");
            Console.ResetColor();

            Directory.CreateDirectory(Path.GetDirectoryName(debugPath) ?? ".");
            Cv2.ImWrite(debugPath, screenshot);
            Console.WriteLine($"[LIVE-HOME] Debug screenshot saved: {debugPath}");
        }

        /// <summary>
        /// Chạy thử luồng huấn luyện thông minh với cấu hình đọc trực tiếp từ tệp JSON.
        /// </summary>
        public static void RunSmartTrainTest(string configPath, string templatesPath)
        {
            using JsonDocument doc = JsonDocument.Parse(File.ReadAllText(configPath));
            JsonElement cfg = doc.RootElement.Clone();
            JsonElement devConfig = cfg.ValueKind == JsonValueKind.Object
                && cfg.TryGetProperty("device_connection", out JsonElement configuredDevice)
                && configuredDevice.ValueKind == JsonValueKind.Object
                ? configuredDevice
                : default;
            string host = devConfig.ValueKind == JsonValueKind.Object
                && devConfig.TryGetProperty("host", out JsonElement hostValue)
                && hostValue.ValueKind == JsonValueKind.String
                ? (hostValue.GetString() ?? "127.0.0.1")
                : "127.0.0.1";
            int port = devConfig.ValueKind == JsonValueKind.Object
                && devConfig.TryGetProperty("port", out JsonElement portValue)
                && portValue.ValueKind == JsonValueKind.Number
                && portValue.TryGetInt32(out int parsedPort)
                ? parsedPort
                : 5556;

            ADBHelper adb = new ADBHelper(host, port);
            VisionEngine vision = new VisionEngine(templatesPath);
            Training training = new Training(adb, templatesPath, vision);
            training.SmartTrain(cfg);
        }
    }
}
