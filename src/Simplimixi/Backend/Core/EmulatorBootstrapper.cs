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
        // Tên tiến trình (Process) của BlueStacks App Player
        private const string BlueStacksProcessName = "HD-Player";

        // Tên gói package Android của game Clash of Clans
        private const string ClashPackageName = "com.supercell.clashofclans";

        // Tiền tố instance mặc định của BlueStacks (thường là bản Pie 64-bit)
        private const string DefaultInstancePrefix = "bst.instance.Pie64.";

        // Danh sách các đường dẫn mặc định thường gặp của file chạy BlueStacks HD-Player.exe
        private static readonly string[] PlayerCandidates =
        {
            @"C:\Program Files\BlueStacks_nxt\HD-Player.exe",
            @"C:\Program Files\BlueStacks\HD-Player.exe",
            @"C:\Program Files (x86)\BlueStacks_nxt\HD-Player.exe",
            @"C:\Program Files (x86)\BlueStacks\HD-Player.exe"
        };

        /// <summary>
        /// Đảm bảo giả lập đã sẵn sàng và game Clash of Clans đã được bật lên hàng trước (foreground).
        /// </summary>
        /// <param name="adb">Đối tượng ADBHelper để giao tiếp.</param>
        /// <param name="host">Địa chỉ IP của giả lập (mặc định 127.0.0.1).</param>
        /// <param name="port">Cổng ADB của giả lập (mặc định 5556).</param>
        /// <param name="token">Token dùng để hủy bỏ tiến trình chờ nếu người dùng yêu cầu dừng bot.</param>
        /// <returns>True nếu giả lập và game sẵn sàng, ngược lại False.</returns>
        public static bool EnsureReady(ADBHelper adb, string host, int port, CancellationToken token)
        {
            Console.WriteLine($"[ADB] phase=boot status=start host={host} port={port}");

            // Tìm file thực thi của BlueStacks trong máy tính
            string? playerPath = FindExistingFile(PlayerCandidates);
            if (playerPath != null)
            {
                Console.WriteLine($"[ADB] phase=boot status=pending action=locate_player details=\"{playerPath}\"");
            }
            else
            {
                Console.WriteLine("[ADB WARNING] phase=boot status=pending action=locate_player reason=not_found");
            }

            // Đường dẫn tệp cấu hình của BlueStacks để kiểm tra thông tin cài đặt
            string confPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                @"BlueStacks_nxt\bluestacks.conf");
            if (File.Exists(confPath))
            {
                Console.WriteLine($"[ADB] phase=boot status=pending action=locate_conf details=\"{confPath}\"");
            }
            else
            {
                Console.WriteLine($"[ADB WARNING] phase=boot status=pending action=locate_conf reason=not_found details=\"{confPath}\"");
            }

            Console.WriteLine($"[ADB] phase=boot status=pending action=set_instance prefix=\"{DefaultInstancePrefix}\"");
            bool displaySettingsChanged = EnsureBlueStacksDisplaySettings(confPath, DefaultInstancePrefix);

            // Nếu BlueStacks đang chạy với display config vừa đổi, restart để 1600x900 DPI 300 có hiệu lực.
            if (displaySettingsChanged && IsBlueStacksRunning())
            {
                Console.WriteLine("[ADB] phase=boot status=pending action=restart_emulator reason=settings_changed");
                KillBlueStacksProcesses();
                Thread.Sleep(1500);
            }

            // Nếu BlueStacks chưa chạy, reset ADB trước rồi mới mở lại giả lập để tránh kẹt kết nối cũ.
            if (!IsBlueStacksRunning())
            {
                if (!ResetAdbAndStartBlueStacks(playerPath, "BlueStacks is not running"))
                {
                    return false;
                }
            }
            else
            {
                Console.WriteLine("[ADB] phase=boot status=pending action=check_emulator details=\"running\"");
            }

            // Đợi ADB online. Nếu bị kẹt, reset ADB và gọi lại BlueStacks một lần rồi thử lại.
            if (!WaitForOnline(adb, token))
            {
                Console.WriteLine("[ADB WARNING] phase=boot status=pending action=wait_online reason=timeout_retrying");
                if (!ResetAdbAndStartBlueStacks(playerPath, "ADB connection is not online"))
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

            // Kiểm tra xem Clash of Clans đã được cài đặt trên giả lập chưa
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

            // Thử nhẹ trước: launch game bằng ADB, chỉ reset ADB nếu game vẫn không lên foreground.
            if (LaunchClashAndWaitForeground(adb, token, timeoutSeconds: 12))
            {
                Console.WriteLine("[ADB] phase=launch_app status=success");
                return true;
            }

            Console.WriteLine("[ADB WARNING] phase=launch_app status=retry reason=timeout");
            if (!ResetAdbAndStartBlueStacks(playerPath, "Clash of Clans failed to launch"))
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

        /// <summary>
        /// Reset tiến trình ADB bị kẹt rồi gọi lại BlueStacks để nó tự mở/kéo instance lên.
        /// </summary>
        private static bool ResetAdbAndStartBlueStacks(string? playerPath, string reason)
        {
            if (playerPath == null)
            {
                Console.WriteLine($"[ADB ERROR] phase=boot status=fail action=start_emulator reason=player_missing details=\"{reason}\"");
                return false;
            }

            Console.WriteLine($"[ADB] phase=boot status=pending action=kill_adb reason=\"{reason}\"");
            KillAdbProcesses();
            StartBlueStacks(playerPath);
            return true;
        }

        /// <summary>
        /// Kết thúc BlueStacks để cấu hình display trong bluestacks.conf được nạp lại khi mở tiếp theo.
        /// </summary>
        private static void KillBlueStacksProcesses()
        {
            foreach (Process process in Process.GetProcessesByName(BlueStacksProcessName))
            {
                try
                {
                    process.Kill(entireProcessTree: true);
                    process.WaitForExit(5000);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[ADB WARNING] phase=boot status=pending action=kill_emulator pid={process.Id} reason=\"{ex.Message}\"");
                }
                finally
                {
                    process.Dispose();
                }
            }
        }

        /// <summary>
        /// Kết thúc toàn bộ adb.exe để xoá trạng thái server/connection cũ trước khi mở BlueStacks.
        /// </summary>
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

        /// <summary>
        /// Gửi lệnh mở Clash of Clans và chờ đến khi game lên foreground.
        /// </summary>
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

        /// <summary>
        /// Kiểm tra Clash of Clans có đang là activity foreground/top resumed không.
        /// </summary>
        private static bool IsClashOfClansForeground(ADBHelper adb)
        {
            string windowInfo = adb.ExecuteShell("dumpsys window windows | grep -E 'mCurrentFocus|mFocusedApp'");
            string activityInfo = adb.ExecuteShell("dumpsys activity activities | grep -E 'mResumedActivity|topResumedActivity'");

            return windowInfo.Contains(ClashPackageName, StringComparison.OrdinalIgnoreCase)
                || activityInfo.Contains(ClashPackageName, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Kiểm tra xem BlueStacks (HD-Player) có đang chạy trong danh sách Task Manager của Windows không.
        /// </summary>
        private static bool IsBlueStacksRunning()
        {
            return Process.GetProcessesByName(BlueStacksProcessName).Any();
        }

        /// <summary>
        /// Khởi chạy file thực thi của BlueStacks.
        /// </summary>
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
