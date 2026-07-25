using System;
using System.IO;
using OpenCvSharp;

namespace CvAut
{
    /// <summary>
    /// Writes navigation debug screenshots to the local application data folder.
    /// </summary>
    internal static class NavigationDebugRecorder
    {
        internal static void SaveDebugScreenshot(Mat screenshot, string phase)
        {
            try
            {
                string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SimpliMixi", "logs", "BuilderBaseNavigation");
                Directory.CreateDirectory(dir);

                string safePhase = SafeFileName(phase);
                string path = Path.Combine(dir, $"{safePhase}_{DateTime.UtcNow:yyyyMMdd_HHmmss_fff}.png");
                Cv2.ImWrite(path, screenshot);

                BuilderBaseNavigationLog.Write("debug_screenshot", "saved", "builder_base_navigation", null, $"phase={safePhase} path=\"{path}\"");
            }
            catch (Exception ex)
            {
                BuilderBaseNavigationLog.Write("debug_screenshot", "fail", "builder_base_navigation", null, $"reason=\"{ex.Message}\"");
            }
        }

        internal static string SafeFileName(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return "unknown";

            char[] invalid = Path.GetInvalidFileNameChars();
            char[] chars = value.Trim().ToCharArray();
            for (int i = 0; i < chars.Length; i++)
            {
                char ch = chars[i];
                if (Array.IndexOf(invalid, ch) >= 0 || char.IsWhiteSpace(ch))
                {
                    chars[i] = '_';
                }
            }

            return new string(chars);
        }
    }
}
