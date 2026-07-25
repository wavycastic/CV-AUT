using System;
using System.Threading;

namespace CvAut
{
    /// <summary>
    /// Chịu trách nhiệm duy nhất: mọi tương tác cấp ứng dụng với Clash of Clans qua ADB.
    /// Kiểm tra đã cài đặt, kiểm tra đang ở foreground và khởi chạy game.
    /// </summary>
    internal static class ClashAppLauncher
    {
        public const string PackageName = "com.supercell.clashofclans";

        /// <summary>
        /// Kiểm tra game đã được cài đặt trên giả lập hay chưa.
        /// </summary>
        public static bool IsInstalled(IADBHelper adb)
        {
            string packageInfo = adb.ExecuteShell($"pm path {PackageName}");
            return !packageInfo.StartsWith("Error:", StringComparison.OrdinalIgnoreCase)
                && !string.IsNullOrWhiteSpace(packageInfo);
        }

        /// <summary>
        /// Khởi chạy game rồi chờ cho tới khi nó chiếm foreground.
        /// </summary>
        public static bool LaunchAndWaitForeground(IADBHelper adb, CancellationToken token, int timeoutSeconds)
        {
            Console.WriteLine("[ADB] phase=launch_app status=start");
            adb.ExecuteShell($"monkey -p {PackageName} -c android.intent.category.LAUNCHER 1");

            DateTime deadline = DateTime.Now.AddSeconds(timeoutSeconds);
            while (DateTime.Now < deadline && !token.IsCancellationRequested)
            {
                if (IsForeground(adb))
                {
                    return true;
                }

                if (token.WaitHandle.WaitOne(1000)) return false;
            }

            return false;
        }

        /// <summary>
        /// Xác định game có đang là ứng dụng foreground hay không.
        /// </summary>
        public static bool IsForeground(IADBHelper adb)
        {
            string windowInfo = adb.ExecuteShell("dumpsys window windows | grep -E 'mCurrentFocus|mFocusedApp'");
            string activityInfo = adb.ExecuteShell("dumpsys activity activities | grep -E 'mResumedActivity|topResumedActivity'");

            return windowInfo.Contains(PackageName, StringComparison.OrdinalIgnoreCase)
                || activityInfo.Contains(PackageName, StringComparison.OrdinalIgnoreCase);
        }
    }
}
