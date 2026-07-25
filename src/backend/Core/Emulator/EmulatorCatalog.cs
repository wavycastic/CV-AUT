using System;
using System.Diagnostics;
using System.IO;
using System.Linq;

namespace CvAut
{
    /// <summary>
    /// Chịu trách nhiệm duy nhất: hiểu biết về từng loại giả lập (vendor knowledge).
    /// Nơi duy nhất biết đường dẫn cài đặt ứng viên, tên tiến trình và cách nhận biết
    /// một giả lập đang chạy hay không. Thêm một vendor mới chỉ cần sửa file này.
    /// </summary>
    internal static class EmulatorCatalog
    {
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

        private static readonly string[] LDPlayerCandidates =
        {
            // Direct drive roots (LDPlayer9 / LDPlayer4 / LDPlayer variants).
            @"C:\LDPlayer\LDPlayer9\dnplayer.exe",
            @"C:\LDPlayer\LDPlayer4\dnplayer.exe",
            @"C:\LDPlayer\LDPlayer\dnplayer.exe",
            @"D:\LDPlayer\LDPlayer9\dnplayer.exe",
            @"D:\LDPlayer\LDPlayer4\dnplayer.exe",
            @"D:\LDPlayer\LDPlayer\dnplayer.exe",
            @"E:\LDPlayer\LDPlayer9\dnplayer.exe",
            @"E:\LDPlayer\LDPlayer4\dnplayer.exe",
            @"E:\LDPlayer\LDPlayer\dnplayer.exe",
            @"F:\LDPlayer\LDPlayer9\dnplayer.exe",
            @"F:\LDPlayer\LDPlayer4\dnplayer.exe",
            @"F:\LDPlayer\LDPlayer\dnplayer.exe",
            // Download subfolders (e.g. user machine installs into E:\Download\LDPlayer\LDPlayer9).
            @"C:\Download\LDPlayer\LDPlayer9\dnplayer.exe",
            @"D:\Download\LDPlayer\LDPlayer9\dnplayer.exe",
            @"E:\Download\LDPlayer\LDPlayer9\dnplayer.exe",
            @"F:\Download\LDPlayer\LDPlayer9\dnplayer.exe",
            @"C:\Download\LDPlayer\LDPlayer4\dnplayer.exe",
            @"D:\Download\LDPlayer\LDPlayer4\dnplayer.exe",
            @"E:\Download\LDPlayer\LDPlayer4\dnplayer.exe",
            @"F:\Download\LDPlayer\LDPlayer4\dnplayer.exe",
            // Program Files variants.
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "LDPlayer", "LDPlayer9", "dnplayer.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "LDPlayer", "LDPlayer9", "dnplayer.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "LDPlayer", "LDPlayer4", "dnplayer.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "LDPlayer", "LDPlayer4", "dnplayer.exe"),
        };

        private static readonly string[] MuMuCandidates =
        {
            @"C:\Program Files\MuMuPlayer-12.0\shell\MuMuPlayer.exe",
            @"C:\Program Files (x86)\MuMuPlayer-12.0\shell\MuMuPlayer.exe",
            @"D:\Program Files\MuMuPlayer-12.0\shell\MuMuPlayer.exe",
            @"C:\Program Files\MuMuPlayer-12.0\shell\NemuPlayer.exe"
        };

        /// <summary>
        /// Danh sách đường dẫn cài đặt ứng viên cho một loại giả lập.
        /// </summary>
        public static string[] GetCandidatesForType(string type)
        {
            switch (type?.ToLowerInvariant())
            {
                case "memu": return MEmuCandidates;
                case "ldplayer": return LDPlayerCandidates;
                case "mumu": return MuMuCandidates;
                case "bluestacks":
                default:
                    return BlueStacksCandidates;
            }
        }

        /// <summary>
        /// Xác định đường dẫn tới trình phát giả lập: ưu tiên đường dẫn người dùng cấu hình,
        /// sau đó mới dò theo danh sách ứng viên của vendor.
        /// </summary>
        public static string? ResolvePlayerPath(string emulatorType, string emulatorPath)
        {
            if (!string.IsNullOrWhiteSpace(emulatorPath) && File.Exists(emulatorPath))
            {
                return emulatorPath;
            }

            return FindExistingFile(GetCandidatesForType(emulatorType));
        }

        /// <summary>
        /// Tìm tệp tồn tại đầu tiên từ danh sách ứng viên đường dẫn tệp.
        /// </summary>
        private static string? FindExistingFile(string[] candidates)
        {
            return candidates.FirstOrDefault(File.Exists);
        }

        /// <summary>
        /// Tên tiến trình Windows tương ứng với từng loại giả lập.
        /// </summary>
        public static string GetProcessNameForType(string type)
        {
            switch (type?.ToLowerInvariant())
            {
                case "memu": return "MEmu";
                case "ldplayer": return "dnplayer";
                case "mumu": return "MuMuPlayer";
                case "bluestacks":
                default:
                    return "HD-Player";
            }
        }

        /// <summary>
        /// Kiểm tra giả lập thuộc loại chỉ định có đang chạy hay không.
        /// </summary>
        public static bool IsRunning(string type)
        {
            string pName = GetProcessNameForType(type);
            if (Process.GetProcessesByName(pName).Any()) return true;
            if (type?.ToLowerInvariant() == "mumu" && Process.GetProcessesByName("NemuPlayer").Any()) return true;
            return false;
        }

        /// <summary>
        /// Kết thúc toàn bộ tiến trình của loại giả lập chỉ định.
        /// </summary>
        public static void KillEmulatorProcesses(string type)
        {
            string pName = GetProcessNameForType(type);
            EmulatorProcessLauncher.KillProcesses(pName);
            if (type?.ToLowerInvariant() == "mumu")
            {
                EmulatorProcessLauncher.KillProcesses("NemuPlayer");
            }
        }
    }
}
