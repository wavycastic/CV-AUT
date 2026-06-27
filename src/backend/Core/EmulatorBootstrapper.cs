using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;

namespace CvAut
{
    /// <summary>
    /// Lớp tiện ích chịu trách nhiệm kiểm tra trạng thái giả lập BlueStacks,
    /// tự động khởi chạy giả lập nếu chưa mở, chờ giả lập online qua ADB,
    /// kiểm tra xem game Clash of Clans đã cài đặt chưa và khởi chạy game.
    /// </summary>
    internal static class EmulatorBootstrapper
    {
        private const string ClashPackageName = "com.supercell.clashofclans";
        private const string DefaultInstancePrefix = "bst.instance.Pie64.";

        private static readonly string[] BlueStacksCandidates =
        {
            @"C:\Program Files\BlueStacks_nxt\HD-Player.exe",
            @"C:\Program Files\BlueStacks\HD-Player.exe",
            @"C:\Program Files (x86)\BlueStacks_nxt\HD-Player.exe",
            @"C:\Program Files (x86)\BlueStacks\HD-Player.exe"
        };

        private static readonly string[] MEmuCandidates =
        {
            @"C:\Program Files\Microvirt\MEmu\MEmu.exe",
            @"D:\Program Files\Microvirt\MEmu\MEmu.exe",
            @"C:\Program Files (x86)\Microvirt\MEmu\MEmu.exe"
        };

        private static readonly string[] NoxCandidates =
        {
            @"C:\Program Files\Nox\bin\Nox.exe",
            @"C:\Program Files (x86)\Nox\bin\Nox.exe",
            @"D:\Program Files\Nox\bin\Nox.exe"
        };

        private static readonly string[] LDPlayerCandidates =
        {
            @"C:\LDPlayer\LDPlayer9\dnplayer.exe",
            @"C:\LDPlayer\LDPlayer4\dnplayer.exe",
            @"C:\LDPlayer\LDPlayer\dnplayer.exe",
            @"D:\LDPlayer\LDPlayer9\dnplayer.exe"
        };

        private static readonly string[] MuMuCandidates =
        {
            @"C:\Program Files\MuMuPlayer-12.0\shell\MuMuPlayer.exe",
            @"C:\Program Files (x86)\MuMuPlayer-12.0\shell\MuMuPlayer.exe",
            @"D:\Program Files\MuMuPlayer-12.0\shell\MuMuPlayer.exe",
            @"C:\Program Files\MuMuPlayer-12.0\shell\NemuPlayer.exe"
        };

        private static string[] GetCandidatesForType(string type)
        {
            switch (type?.ToLowerInvariant())
            {
                case "memu": return MEmuCandidates;
                case "nox": return NoxCandidates;
                case "ldplayer": return LDPlayerCandidates;
                case "mumu": return MuMuCandidates;
                case "bluestacks":
                default:
                    return BlueStacksCandidates;
            }
        }

        public static bool EnsureReady(ADBHelper adb, string host, int port, string emulatorType, string emulatorPath, CancellationToken token)
        {
            Console.WriteLine($"[ADB] phase=boot status=start host={host} port={port} type={emulatorType}");

            string? playerPath = null;
            if (!string.IsNullOrWhiteSpace(emulatorPath) && File.Exists(emulatorPath))
            {
                playerPath = emulatorPath;
            }
            else
            {
                playerPath = FindExistingFile(GetCandidatesForType(emulatorType));
            }

            if (playerPath != null)
            {
                Console.WriteLine($"[ADB] phase=boot status=pending action=locate_player details=\"{playerPath}\"");
            }
            else
            {
                Console.WriteLine("[ADB WARNING] phase=boot status=pending action=locate_player reason=not_found");
            }

            bool displaySettingsChanged = false;
            if (string.Equals(emulatorType, "BlueStacks", StringComparison.OrdinalIgnoreCase))
            {
                string confPath = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                    @"BlueStacks_nxt\bluestacks.conf");
                if (File.Exists(confPath))
                {
                    Console.WriteLine($"[ADB] phase=boot status=pending action=locate_conf details=\"{confPath}\"");
                    Console.WriteLine($"[ADB] phase=boot status=pending action=set_instance prefix=\"{DefaultInstancePrefix}\"");
                    displaySettingsChanged = EnsureBlueStacksDisplaySettings(confPath, DefaultInstancePrefix);
                }
                else
                {
                    Console.WriteLine($"[ADB WARNING] phase=boot status=pending action=locate_conf reason=not_found details=\"{confPath}\"");
                }
            }

            if (displaySettingsChanged && IsEmulatorRunning(emulatorType))
            {
                Console.WriteLine("[ADB] phase=boot status=pending action=restart_emulator reason=settings_changed");
                KillEmulatorProcesses(emulatorType);
                Thread.Sleep(1500);
            }

            if (!IsEmulatorRunning(emulatorType))
            {
                if (!ResetAdbAndStartEmulator(playerPath, emulatorType, $"{emulatorType} is not running"))
                {
                    return false;
                }
            }
            else
            {
                Console.WriteLine("[ADB] phase=boot status=pending action=check_emulator details=\"running\"");
            }

            if (!WaitForOnline(adb, token))
            {
                Console.WriteLine("[ADB WARNING] phase=boot status=pending action=wait_online reason=timeout_retrying");
                if (!ResetAdbAndStartEmulator(playerPath, emulatorType, "ADB connection is not online"))
                {
                    return false;
                }

                if (!WaitForOnline(adb, token))
                {
                    Console.WriteLine("[ADB ERROR] phase=boot status=fail action=wait_online reason=timeout");
                    return false;
                }
            }

            Console.WriteLine("[ADB] phase=boot status=success details=\"connected\"");
            Console.WriteLine("[ADB] phase=check_app status=start package=\"com.supercell.clashofclans\"");

            string packageInfo = adb.ExecuteShell($"pm path {ClashPackageName}");
            if (packageInfo.StartsWith("Error:", StringComparison.OrdinalIgnoreCase)
                || string.IsNullOrWhiteSpace(packageInfo))
            {
                Console.WriteLine("[ADB ERROR] phase=check_app status=fail reason=not_installed");
                return false;
            }

            if (IsClashOfClansForeground(adb))
            {
                Console.WriteLine("[ADB] phase=launch_app status=success details=\"already_foreground\"");
                return true;
            }

            if (LaunchClashAndWaitForeground(adb, token, timeoutSeconds: 12))
            {
                Console.WriteLine("[ADB] phase=launch_app status=success");
                return true;
            }

            Console.WriteLine("[ADB WARNING] phase=launch_app status=retry reason=timeout");
            if (!ResetAdbAndStartEmulator(playerPath, emulatorType, "Clash of Clans failed to launch"))
            {
                return false;
            }

            if (!WaitForOnline(adb, token))
            {
                Console.WriteLine("[ADB ERROR] phase=boot status=fail action=reconnect reason=timeout");
                return false;
            }

            if (!LaunchClashAndWaitForeground(adb, token, timeoutSeconds: 15))
            {
                Console.WriteLine("[ADB ERROR] phase=launch_app status=fail reason=timeout");
                return false;
            }

            Console.WriteLine("[ADB] phase=launch_app status=success");
            return true;
        }

        /// <summary>
        /// Vòng lặp chờ giả lập online thông qua việc ping kết nối ADB.
        /// </summary>
        private static bool WaitForOnline(ADBHelper adb, CancellationToken token)
        {
            DateTime deadline = DateTime.Now.AddSeconds(90);
            while (DateTime.Now < deadline && !token.IsCancellationRequested)
            {
                // Thử kết nối trong tối đa 3 giây
                if (adb.EnsureConnectedOnline(timeoutSeconds: 3))
                {
                    return true;
                }

                Thread.Sleep(1000);
            }

            return false;
        }

        /// <summary>
        /// Tìm tệp tồn tại đầu tiên từ danh sách ứng viên đường dẫn tệp.
        /// </summary>
        private static string? FindExistingFile(string[] candidates)
        {
            return candidates.FirstOrDefault(File.Exists);
        }

        /// <summary>
        /// Đảm bảo BlueStacks dùng đúng độ phân giải 1600x900 và DPI 300 cho các vùng ROI cố định.
        /// </summary>
        private static bool EnsureBlueStacksDisplaySettings(string confPath, string instancePrefix)
        {
            if (!File.Exists(confPath))
            {
                Console.WriteLine("[ADB WARNING] phase=configure status=skip reason=missing_conf");
                return false;
            }

            try
            {
                string[] lines = File.ReadAllLines(confPath);
                bool changed = false;
                changed |= SetConfigValue(lines, $"{instancePrefix}fb_width", "1600");
                changed |= SetConfigValue(lines, $"{instancePrefix}fb_height", "900");
                changed |= SetConfigValue(lines, $"{instancePrefix}dpi", "300");
                changed |= SetConfigValue(lines, $"{instancePrefix}custom_resolution_selected", "1");

                if (!changed)
                {
                    Console.WriteLine("[ADB] phase=configure status=skip reason=already_set");
                    return false;
                }

                File.Copy(confPath, confPath + ".bak", overwrite: true);
                File.WriteAllLines(confPath, lines);
                Console.WriteLine("[ADB] phase=configure status=success");
                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ADB ERROR] phase=configure status=fail reason=\"{ex.Message}\"");
                return false;
            }
        }

        /// <summary>
        /// Cập nhật một dòng key="value" trong bluestacks.conf nếu key tồn tại.
        /// </summary>
        private static bool SetConfigValue(string[] lines, string key, string value)
        {
            string prefix = key + "=";
            for (int i = 0; i < lines.Length; i++)
            {
                if (!lines[i].StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                string desiredLine = $"{key}=\"{value}\"";
                if (string.Equals(lines[i], desiredLine, StringComparison.Ordinal))
                {
                    return false;
                }

                lines[i] = desiredLine;
                return true;
            }

            return false;
        }

        private static bool ResetAdbAndStartEmulator(string? playerPath, string emulatorType, string reason)
        {
            if (playerPath == null)
            {
                Console.WriteLine($"[ADB ERROR] phase=boot status=fail action=start_emulator reason=player_missing details=\"{reason}\"");
                return false;
            }

            Console.WriteLine($"[ADB] phase=boot status=pending action=kill_adb reason=\"{reason}\"");
            KillAdbProcesses();
            StartEmulator(playerPath);
            return true;
        }

        private static string GetProcessNameForType(string type)
        {
            switch (type?.ToLowerInvariant())
            {
                case "memu": return "MEmu";
                case "nox": return "Nox";
                case "ldplayer": return "dnplayer";
                case "mumu": return "MuMuPlayer";
                case "bluestacks":
                default:
                    return "HD-Player";
            }
        }

        private static bool IsEmulatorRunning(string type)
        {
            string pName = GetProcessNameForType(type);
            if (Process.GetProcessesByName(pName).Any()) return true;
            if (type?.ToLowerInvariant() == "mumu" && Process.GetProcessesByName("NemuPlayer").Any()) return true;
            return false;
        }

        private static void KillEmulatorProcesses(string type)
        {
            string pName = GetProcessNameForType(type);
            KillProcesses(pName);
            if (type?.ToLowerInvariant() == "mumu")
            {
                KillProcesses("NemuPlayer");
            }
        }

        private static void KillProcesses(string processName)
        {
            foreach (Process process in Process.GetProcessesByName(processName))
            {
                try
                {
                    process.Kill(entireProcessTree: true);
                    process.WaitForExit(5000);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[ADB WARNING] Action=kill_emulator process={processName} pid={process.Id} reason=\"{ex.Message}\"");
                }
                finally
                {
                    process.Dispose();
                }
            }
        }

        private static void KillAdbProcesses()
        {
            foreach (Process process in Process.GetProcessesByName("adb"))
            {
                try
                {
                    process.Kill(entireProcessTree: true);
                    process.WaitForExit(3000);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[ADB WARNING] phase=boot status=pending action=kill_adb pid={process.Id} reason=\"{ex.Message}\"");
                }
                finally
                {
                    process.Dispose();
                }
            }
        }

        private static bool LaunchClashAndWaitForeground(ADBHelper adb, CancellationToken token, int timeoutSeconds)
        {
            Console.WriteLine("[ADB] phase=launch_app status=start");
            adb.ExecuteShell($"monkey -p {ClashPackageName} -c android.intent.category.LAUNCHER 1");

            DateTime deadline = DateTime.Now.AddSeconds(timeoutSeconds);
            while (DateTime.Now < deadline && !token.IsCancellationRequested)
            {
                if (IsClashOfClansForeground(adb))
                {
                    return true;
                }

                Thread.Sleep(1000);
            }

            return false;
        }

        private static bool IsClashOfClansForeground(ADBHelper adb)
        {
            string windowInfo = adb.ExecuteShell("dumpsys window windows | grep -E 'mCurrentFocus|mFocusedApp'");
            string activityInfo = adb.ExecuteShell("dumpsys activity activities | grep -E 'mResumedActivity|topResumedActivity'");

            return windowInfo.Contains(ClashPackageName, StringComparison.OrdinalIgnoreCase)
                || activityInfo.Contains(ClashPackageName, StringComparison.OrdinalIgnoreCase);
        }

        private static void StartEmulator(string playerPath)
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
