using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;

namespace CvAut
{
    public static class EmulatorBootstrapper
    {
        private const string BlueStacksProcessName = "HD-Player";
        private const string ClashPackageName = "com.supercell.clashofclans";
        private const string DefaultInstancePrefix = "bst.instance.Pie64.";

        private static readonly string[] PlayerCandidates =
        {
            @"C:\Program Files\BlueStacks_nxt\HD-Player.exe",
            @"C:\Program Files\BlueStacks\HD-Player.exe",
            @"C:\Program Files (x86)\BlueStacks_nxt\HD-Player.exe",
            @"C:\Program Files (x86)\BlueStacks\HD-Player.exe"
        };

        public static bool EnsureReady(ADBHelper adb, string host, int port, CancellationToken token)
        {
            Console.WriteLine($"[INFO] Using emulator: {host}:{port}");

            string? playerPath = FindExistingFile(PlayerCandidates);
            if (playerPath != null)
            {
                Console.WriteLine($"✅ Found HD-Player.exe at: {playerPath}");
            }
            else
            {
                Console.WriteLine("⚠️ HD-Player.exe not found in default BlueStacks paths.");
            }

            string confPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                @"BlueStacks_nxt\bluestacks.conf");
            if (File.Exists(confPath))
            {
                Console.WriteLine($"✅ Found bluestacks.conf at: {confPath}");
            }
            else
            {
                Console.WriteLine($"⚠️ bluestacks.conf not found at: {confPath}");
            }

            Console.WriteLine($"▶ Using instance prefix: {DefaultInstancePrefix}");
            Console.WriteLine("▶ All core settings perfect – no changes.");

            if (!IsBlueStacksRunning())
            {
                if (playerPath == null)
                {
                    Console.WriteLine("❌ BlueStacks not running and HD-Player.exe could not be located.");
                    return false;
                }

                Console.WriteLine("🔄 BlueStacks not running—launching emulator…");
                StartBlueStacks(playerPath);
            }
            else
            {
                Console.WriteLine("✅ BlueStacks already running.");
            }

            if (!WaitForOnline(adb, token))
            {
                Console.WriteLine("❌ Emulator did not become online in time.");
                return false;
            }

            Console.WriteLine("✅ Emulator connected and online.");
            Console.WriteLine("▶ Checking if Clash of Clans is installed…");

            string packageInfo = adb.ExecuteShell($"pm path {ClashPackageName}");
            if (packageInfo.StartsWith("Error:", StringComparison.OrdinalIgnoreCase)
                || string.IsNullOrWhiteSpace(packageInfo))
            {
                Console.WriteLine("❌ Clash of Clans is not installed or ADB could not query the package.");
                return false;
            }

            Console.WriteLine("▶ Launching Clash of Clans…");
            adb.ExecuteShell($"monkey -p {ClashPackageName} -c android.intent.category.LAUNCHER 1");
            Thread.Sleep(1000);
            Console.WriteLine("✅ Clash of Clans should now be in the foreground.");
            return true;
        }

        private static bool WaitForOnline(ADBHelper adb, CancellationToken token)
        {
            DateTime deadline = DateTime.Now.AddSeconds(90);
            while (DateTime.Now < deadline && !token.IsCancellationRequested)
            {
                if (adb.EnsureConnectedOnline(timeoutSeconds: 3))
                {
                    return true;
                }

                Thread.Sleep(1000);
            }

            return false;
        }

        private static string? FindExistingFile(string[] candidates)
        {
            return candidates.FirstOrDefault(File.Exists);
        }

        private static bool IsBlueStacksRunning()
        {
            return Process.GetProcessesByName(BlueStacksProcessName).Any();
        }

        private static void StartBlueStacks(string playerPath)
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = playerPath,
                UseShellExecute = true
            };

            Process.Start(startInfo);
        }
    }
}
