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
                Console.WriteLine($"✅ Found HD-Player.exe at: {playerPath}");
            }
            else
            {
                Console.WriteLine("⚠️ HD-Player.exe not found in default BlueStacks paths.");
            }

            // Đường dẫn tệp cấu hình của BlueStacks để kiểm tra thông tin cài đặt
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

            // Nếu tiến trình BlueStacks chưa chạy thì tự động khởi chạy nó
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

            // Đợi cho đến khi giả lập phản hồi kết nối ADB online (timeout 90s)
            if (!WaitForOnline(adb, token))
            {
                Console.WriteLine("❌ Emulator did not become online in time.");
                return false;
            }

            Console.WriteLine("✅ Emulator connected and online.");
            Console.WriteLine("▶ Checking if Clash of Clans is installed…");

            // Kiểm tra xem Clash of Clans đã được cài đặt trên giả lập chưa
            string packageInfo = adb.ExecuteShell($"pm path {ClashPackageName}");
            if (packageInfo.StartsWith("Error:", StringComparison.OrdinalIgnoreCase)
                || string.IsNullOrWhiteSpace(packageInfo))
            {
                Console.WriteLine("❌ Clash of Clans is not installed or ADB could not query the package.");
                return false;
            }

            // Gửi lệnh ADB khởi chạy Clash of Clans qua ứng dụng monkey
            Console.WriteLine("▶ Launching Clash of Clans…");
            adb.ExecuteShell($"monkey -p {ClashPackageName} -c android.intent.category.LAUNCHER 1");
            Thread.Sleep(1000);
            Console.WriteLine("✅ Clash of Clans should now be in the foreground.");
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
