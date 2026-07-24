using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using OpenCvSharp;
using SharpAdbClient;

namespace CvAut
{
    public static class BackendDiagnostics
    {
        public static byte[] LoadTemplatePngBytes(string templatesRoot, string relativePath)
        {
            return TemplateAssetLoader.LoadPngBytes(templatesRoot, relativePath);
        }

        /// <summary>
        /// Returns the serials of all ADB devices the local ADB server can see
        /// (e.g. "127.0.0.1:5556", "emulator-5554"). Starts the bundled ADB server
        /// first. Used by the UI device picker; never throws.
        /// </summary>
        public static IReadOnlyList<string> ListAdbDevices()
        {
            var serials = new List<string>();
            try
            {
                var server = new AdbServer();
                try
                {
                    server.StartServer(Path.Combine(AppContext.BaseDirectory, "adb", "adb.exe"), restartServerIfNewer: false);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[UI] phase=list_devices status=pending action=start_adb_server reason=\"{ex.Message}\"");
                }

                IEnumerable<DeviceData>? devices = AdbClient.Instance.GetDevices();
                if (devices != null)
                {
                    foreach (DeviceData device in devices)
                    {
                        if (!string.IsNullOrWhiteSpace(device.Serial))
                        {
                            serials.Add(device.Serial);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[UI] phase=list_devices status=fail reason=\"{ex.Message}\"");
            }

            return serials;
        }

        /// <summary>
        /// Returns ADB devices with their ADB state string (e.g. "Device", "Offline",
        /// "Unauthorized"). Maps <see cref="DeviceData.State"/>. Used by scanners so the
        /// orchestrator can show unauthorized/offline devices distinctly from ready ones.
        /// Starts the bundled ADB server first. Never throws.
        /// </summary>
        public static IReadOnlyList<(string Serial, string State)> ListAdbDevicesWithStatus()
        {
            var result = new List<(string, string)>();
            try
            {
                var server = new AdbServer();
                try
                {
                    server.StartServer(Path.Combine(AppContext.BaseDirectory, "adb", "adb.exe"), restartServerIfNewer: false);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[UI] phase=list_devices status=pending action=start_adb_server reason=\"{ex.Message}\"");
                }

                IEnumerable<DeviceData>? devices = AdbClient.Instance.GetDevices();
                if (devices != null)
                {
                    foreach (DeviceData device in devices)
                    {
                        if (!string.IsNullOrWhiteSpace(device.Serial))
                        {
                            result.Add((device.Serial, device.State.ToString()));
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[UI] phase=list_devices_status status=fail reason=\"{ex.Message}\"");
            }

            return result;
        }

        public static (int Width, int Height, int DensityDpi, string Raw) GetEmulatorDisplayInfo(string host, int port, string? serial = null)
        {
            try
            {
                using var adb = new ADBHelper(host, port, serial);
                string size = adb.ExecuteShell("wm size");
                string density = adb.ExecuteShell("wm density");

                if (IsAdbErrorString(size)) size = string.Empty;
                if (IsAdbErrorString(density)) density = string.Empty;

                int width = 0;
                int height = 0;
                int dpi = 0;

                Match sizeMatch = MatchPreferredDisplayValue(size, @"(\d+)x(\d+)");
                if (sizeMatch.Success)
                {
                    int.TryParse(sizeMatch.Groups[1].Value, out width);
                    int.TryParse(sizeMatch.Groups[2].Value, out height);
                }

                Match densityMatch = MatchPreferredDisplayValue(density, @"Physical density:\s*(\d+)|Override density:\s*(\d+)|(\d+)");
                if (densityMatch.Success)
                {
                    for (int g = 1; g < densityMatch.Groups.Count; g++)
                    {
                        if (densityMatch.Groups[g].Success && int.TryParse(densityMatch.Groups[g].Value, out int parsedDpi))
                        {
                            dpi = parsedDpi;
                            break;
                        }
                    }
                }

                if (width <= 0 || height <= 0)
                {
                    using Mat? shot = adb.TakeScreenshot();
                    if (shot != null && !shot.Empty())
                    {
                        width = shot.Width;
                        height = shot.Height;
                    }
                }

                if (dpi > 1000) dpi = 0;

                string raw = $"{size?.Trim()} | {density?.Trim()}";
                return (width, height, dpi, raw);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[UI] phase=display_probe status=fail reason=\"{ex.Message}\"");
                return (0, 0, 0, ex.Message);
            }
        }

        private static bool IsAdbErrorString(string? raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return true;
            return raw.Contains("error:", StringComparison.OrdinalIgnoreCase) ||
                   raw.Contains("device offline", StringComparison.OrdinalIgnoreCase) ||
                   raw.Contains("not found", StringComparison.OrdinalIgnoreCase);
        }

        private static Match MatchPreferredDisplayValue(string raw, string pattern)
        {
            if (string.IsNullOrWhiteSpace(raw) || IsAdbErrorString(raw))
            {
                return Match.Empty;
            }

            foreach (string line in raw.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries).Reverse())
            {
                if (line.Contains("Override", StringComparison.OrdinalIgnoreCase))
                {
                    Match overrideMatch = Regex.Match(line, pattern);
                    if (overrideMatch.Success)
                    {
                        return overrideMatch;
                    }
                }
            }

            MatchCollection matches = Regex.Matches(raw, pattern);
            for (int i = matches.Count - 1; i >= 0; i--)
            {
                Match match = matches[i];
                if (match.Success && !string.Equals(match.Value, "0x0", StringComparison.OrdinalIgnoreCase))
                {
                    return match;
                }
            }

            return Match.Empty;
        }

        public static void DiagnoseSavedArmyWindow(string outputPath, string templatesPath)
        {
            Training.DiagnoseSavedArmyWindow(outputPath, templatesPath);
        }

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

        public static void ZoomOut(string configPath)
        {
            new CVAutomationFramework(configPath).ZoomOut();
        }

        public static void BootRecovery(string configPath)
        {
            new CVAutomationFramework(configPath).BootRecovery();
        }

        public static void RunWorkflowTemplate(string configPath, int cycleCount, CancellationToken token)
        {
            new CVAutomationFramework(configPath).RunCyclesForTest(cycleCount, token);
        }
    }
}
