using System;
using System.Linq;
using System.Text.RegularExpressions;
using OpenCvSharp;

namespace CvAut
{
    /// <summary>
    /// Chịu trách nhiệm duy nhất: đọc độ phân giải và DPI thực tế của giả lập qua
    /// "wm size" / "wm density", kèm toàn bộ logic phân tích chuỗi trả về của ADB.
    /// Dùng để đối chiếu với cấu hình 1600x900 @ 300dpi mà bộ ROI yêu cầu.
    /// </summary>
    internal static class EmulatorDisplayProbe
    {
        public static (int Width, int Height, int DensityDpi, string Raw) GetDisplayInfo(string host, int port, string? serial = null)
        {
            try
            {
                using var adb = new ADBHelper(host, port, serial);
                string size = adb.ExecuteShell("wm size");
                string density = adb.ExecuteShell("wm density");

                if (IsAdbErrorString(size)) size = string.Empty;
                if (IsAdbErrorString(density)) density = string.Empty;

                int width = 0;
                int height = 0;
                int dpi = 0;

                Match sizeMatch = MatchPreferredDisplayValue(size, @"(\d+)x(\d+)");
                if (sizeMatch.Success)
                {
                    int.TryParse(sizeMatch.Groups[1].Value, out width);
                    int.TryParse(sizeMatch.Groups[2].Value, out height);
                }

                Match densityMatch = MatchPreferredDisplayValue(density, @"Physical density:\s*(\d+)|Override density:\s*(\d+)|(\d+)");
                if (densityMatch.Success)
                {
                    for (int g = 1; g < densityMatch.Groups.Count; g++)
                    {
                        if (densityMatch.Groups[g].Success && int.TryParse(densityMatch.Groups[g].Value, out int parsedDpi))
                        {
                            dpi = parsedDpi;
                            break;
                        }
                    }
                }

                if (width <= 0 || height <= 0)
                {
                    using Mat? shot = adb.TakeScreenshot();
                    if (shot != null && !shot.Empty())
                    {
                        width = shot.Width;
                        height = shot.Height;
                    }
                }

                if (dpi > 1000) dpi = 0;

                string raw = $"{size?.Trim()} | {density?.Trim()}";
                return (width, height, dpi, raw);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[UI] phase=display_probe status=fail reason=\"{ex.Message}\"");
                return (0, 0, 0, ex.Message);
            }
        }

        /// <summary>
        /// Nhận biết chuỗi trả về của ADB là thông báo lỗi chứ không phải dữ liệu hợp lệ.
        /// </summary>
        private static bool IsAdbErrorString(string? raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return true;
            return raw.Contains("error:", StringComparison.OrdinalIgnoreCase) ||
                   raw.Contains("device offline", StringComparison.OrdinalIgnoreCase) ||
                   raw.Contains("not found", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Ưu tiên lấy giá trị ở dòng "Override" (giá trị đang có hiệu lực) trước,
        /// sau đó mới lấy kết quả khớp cuối cùng khác "0x0".
        /// </summary>
        private static Match MatchPreferredDisplayValue(string raw, string pattern)
        {
            if (string.IsNullOrWhiteSpace(raw) || IsAdbErrorString(raw))
            {
                return Match.Empty;
            }

            foreach (string line in raw.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries).Reverse())
            {
                if (line.Contains("Override", StringComparison.OrdinalIgnoreCase))
                {
                    Match overrideMatch = Regex.Match(line, pattern);
                    if (overrideMatch.Success)
                    {
                        return overrideMatch;
                    }
                }
            }

            MatchCollection matches = Regex.Matches(raw, pattern);
            for (int i = matches.Count - 1; i >= 0; i--)
            {
                Match match = matches[i];
                if (match.Success && !string.Equals(match.Value, "0x0", StringComparison.OrdinalIgnoreCase))
                {
                    return match;
                }
            }

            return Match.Empty;
        }
    }
}
