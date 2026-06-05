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
    public static class EmulatorBootstrapper
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
            Console.WriteLine($"[INFO] Using emulator: {host}:{port}");

            // Tìm file thực thi của BlueStacks trong máy tính
            string? playerPath = FindExistingFile(PlayerCandidates);
            if (playerPath != null)
            {
                Console.WriteLine($"Found HD-Player.exe at: {playerPath}");
            }
            else
            {
                Console.WriteLine("HD-Player.exe not found in default BlueStacks paths.");
            }

            // Đường dẫn tệp cấu hình của BlueStacks để kiểm tra thông tin cài đặt
            string confPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                @"BlueStacks_nxt\bluestacks.conf");
            if (File.Exists(confPath))
            {
                Console.WriteLine($"Found bluestacks.conf at: {confPath}");
            }
            else
            {
                Console.WriteLine($"bluestacks.conf not found at: {confPath}");
            }

            Console.WriteLine($"Using instance prefix: {DefaultInstancePrefix}");
            bool displaySettingsChanged = EnsureBlueStacksDisplaySettings(confPath, DefaultInstancePrefix);

            // Nếu BlueStacks đang chạy với display config vừa đổi, restart để 1600x900 DPI 300 có hiệu lực.
            if (displaySettingsChanged && IsBlueStacksRunning())
            {
                Console.WriteLine("BlueStacks display settings changed; restarting BlueStacks...");
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
                Console.WriteLine("BlueStacks already running.");
            }

            // Đợi ADB online. Nếu bị kẹt, reset ADB và gọi lại BlueStacks một lần rồi thử lại.
            if (!WaitForOnline(adb, token))
            {
                Console.WriteLine("Emulator did not become online in time. Retrying after ADB reset...");
                if (!ResetAdbAndStartBlueStacks(playerPath, "ADB connection is not online"))
                {
                    return false;
                }

                if (!WaitForOnline(adb, token))
                {
                    Console.WriteLine("Emulator did not become online after ADB reset.");
                    return false;
                }
            }

            Console.WriteLine("Emulator connected and online.");
            Console.WriteLine("Checking if Clash of Clans is installed...");

            // Kiểm tra xem Clash of Clans đã được cài đặt trên giả lập chưa
            string packageInfo = adb.ExecuteShell($"pm path {ClashPackageName}");
            if (packageInfo.StartsWith("Error:", StringComparison.OrdinalIgnoreCase)
                || string.IsNullOrWhiteSpace(packageInfo))
            {
                Console.WriteLine("Clash of Clans is not installed or ADB could not query the package.");
                return false;
            }

            if (IsClashOfClansForeground(adb))
            {
                Console.WriteLine("Clash of Clans already in the foreground.");
                return true;
            }

            // Thử nhẹ trước: launch game bằng ADB, chỉ reset ADB nếu game vẫn không lên foreground.
            if (LaunchClashAndWaitForeground(adb, token, timeoutSeconds: 12))
            {
                Console.WriteLine("Clash of Clans is now in the foreground.");
                return true;
            }

            Console.WriteLine("Clash of Clans did not reach foreground. Retrying after ADB reset...");
            if (!ResetAdbAndStartBlueStacks(playerPath, "Clash of Clans failed to launch"))
            {
                return false;
            }

            if (!WaitForOnline(adb, token))
            {
                Console.WriteLine("Emulator did not reconnect after ADB reset.");
                return false;
            }

            if (!LaunchClashAndWaitForeground(adb, token, timeoutSeconds: 15))
            {
                Console.WriteLine("Clash of Clans did not reach foreground after retry.");
                return false;
            }

            Console.WriteLine("Clash of Clans is now in the foreground.");
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
                Console.WriteLine("BlueStacks display settings skipped because bluestacks.conf was not found.");
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
                    Console.WriteLine("BlueStacks display settings already set to 1600x900 DPI 300.");
                    return false;
                }

                File.Copy(confPath, confPath + ".bak", overwrite: true);
                File.WriteAllLines(confPath, lines);
                Console.WriteLine("BlueStacks display settings updated to 1600x900 DPI 300.");
                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Failed to update BlueStacks display settings: {ex.Message}");
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
                Console.WriteLine($"{reason}, but HD-Player.exe could not be located.");
                return false;
            }

            Console.WriteLine($"{reason}; killing adb.exe before launching BlueStacks...");
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
                    Console.WriteLine($"Failed to kill BlueStacks ({process.Id}): {ex.Message}");
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
                    Console.WriteLine($"Failed to kill adb.exe ({process.Id}): {ex.Message}");
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
            Console.WriteLine("Launching Clash of Clans...");
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
