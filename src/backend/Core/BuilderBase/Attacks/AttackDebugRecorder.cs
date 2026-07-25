using System;
using System.IO;
using System.Linq;
using OpenCvSharp;

namespace CvAut
{
    internal static class AttackDebugRecorder
    {
        public static void CaptureDebugSnapshot(IADBHelper adb, string reason)
        {
            try
            {
                using Mat? screenshot = adb.TakeScreenshot();
                if (screenshot == null || screenshot.Empty()) return;
                string dir = Path.Combine(AppContext.BaseDirectory, "debug", "bb");
                Directory.CreateDirectory(dir);
                string safeReason = string.Concat(reason.Select(ch => char.IsLetterOrDigit(ch) || ch == '_' || ch == '-' ? ch : '_'));
                string file = Path.Combine(dir, $"{DateTime.Now:yyyyMMdd_HHmmss_fff}_{safeReason}.png");
                Cv2.ImWrite(file, screenshot);
                Console.WriteLine($"[BB-ATTACK] phase=debug_snapshot status=saved reason={safeReason} file=\"{file}\"");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[BB-ATTACK] phase=debug_snapshot status=fail reason=exception message=\"{ex.Message}\"");
            }
        }
    }
}
