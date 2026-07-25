using System;
using System.Diagnostics;

namespace CvAut
{
    /// <summary>
    /// Chịu trách nhiệm duy nhất: vòng đời tiến trình. Khởi chạy trình phát giả lập với
    /// đúng tham số instance của từng vendor, và kết thúc tiến trình giả lập hoặc adb.
    /// </summary>
    internal static class EmulatorProcessLauncher
    {
        /// <summary>
        /// Khởi động lại chuỗi ADB: diệt adb server cũ rồi bật lại giả lập.
        /// </summary>
        public static bool ResetAdbAndStartEmulator(string? playerPath, string emulatorType, string reason, string instanceName = "")
        {
            if (playerPath == null)
            {
                Console.WriteLine($"[ADB ERROR] phase=boot status=fail action=start_emulator reason=player_missing details=\"{reason}\"");
                return false;
            }

            Console.WriteLine($"[ADB] phase=boot status=pending action=kill_adb reason=\"{reason}\"");
            KillAdbProcesses();
            StartEmulator(playerPath, emulatorType, instanceName);
            return true;
        }

        /// <summary>
        /// Khởi chạy trình phát giả lập, truyền cờ chọn instance theo cú pháp riêng của từng vendor.
        /// </summary>
        public static void StartEmulator(string playerPath, string emulatorType = "", string instanceName = "")
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = playerPath,
                UseShellExecute = true
            };

            if (!string.IsNullOrWhiteSpace(instanceName))
            {
                if (string.Equals(emulatorType, "BlueStacks", StringComparison.OrdinalIgnoreCase))
                {
                    startInfo.ArgumentList.Add("--instance");
                    startInfo.ArgumentList.Add(instanceName);
                    Console.WriteLine($"[ADB] phase=boot status=pending action=start_emulator type=BlueStacks details=\"instance={instanceName}\"");
                }
                else if (string.Equals(emulatorType, "LDPlayer", StringComparison.OrdinalIgnoreCase))
                {
                    startInfo.ArgumentList.Add(int.TryParse(instanceName, out int idx) ? $"index={idx}" : $"name={instanceName}");
                    Console.WriteLine($"[ADB] phase=boot status=pending action=start_emulator type=LDPlayer details=\"instance={instanceName}\"");
                }
                else if (string.Equals(emulatorType, "MEmu", StringComparison.OrdinalIgnoreCase))
                {
                    startInfo.ArgumentList.Add(int.TryParse(instanceName, out int idx) ? $"index={idx}" : $"name={instanceName}");
                    Console.WriteLine($"[ADB] phase=boot status=pending action=start_emulator type=MEmu details=\"instance={instanceName}\"");
                }
            }

            Process.Start(startInfo);
        }

        /// <summary>
        /// Kết thúc toàn bộ tiến trình mang tên chỉ định kèm cây tiến trình con.
        /// </summary>
        public static void KillProcesses(string processName)
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

        /// <summary>
        /// Kết thúc adb server đang treo để buộc bắt tay lại từ đầu.
        /// </summary>
        public static void KillAdbProcesses()
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
    }
}
