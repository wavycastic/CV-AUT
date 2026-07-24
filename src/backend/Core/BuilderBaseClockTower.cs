using System;
using System.Threading;
using OpenCvSharp;
using Point = OpenCvSharp.Point;

namespace CvAut
{
    /// <summary>
    /// Clock Tower Boost cho Builder Base. Bản tối giản: tìm Clock Tower available,
    /// bấm vào tower, bấm Boost, xác nhận boost nếu có.
    /// </summary>
    internal sealed class BuilderBaseClockTower
    {
        private readonly ADBHelper _adb;
        private readonly VisionEngine _vision;
        private readonly BuilderBaseNavigator _navigator;

        private const double ClockThreshold = 0.62;
        private const double BoostThreshold = 0.62;

        private static readonly Rect MapRoi = Rect.FromLTRB(160, 80, 1440, 790);
        private static readonly Rect ActionButtonRoi = Rect.FromLTRB(520, 560, 1120, 850);
        private static readonly Rect ClockLevelRoi = Rect.FromLTRB(250, 450, 560, 545);

        private static readonly string[] ClockTemplates =
        {
            @"ui\clock_available",
            @"ui\clock",
            @"ui\clock1",
            @"ui\clock2",
            @"clan_games\destroy_clock_tower",
            @"clan_games\destroy_clock_tower1"
        };

        private static readonly string[] BoostTemplates =
        {
            @"ui\free_boost",
            @"ui\boost"
        };

        public BuilderBaseClockTower(ADBHelper adb, VisionEngine vision, BuilderBaseNavigator navigator)
        {
            _adb = adb;
            _vision = vision;
            _navigator = navigator;
        }

        public bool TryBoost(CancellationToken token)
        {
            Console.WriteLine("[BB-CLOCK] phase=boost status=start");

            if (!_navigator.IsOnBuilderBase())
            {
                Console.WriteLine("[BB-CLOCK] phase=boost status=skip reason=not_on_builder_base");
                return false;
            }

            if (!TapFirstVisible(ClockTemplates, ClockThreshold, MapRoi, token, out string clockTemplate))
            {
                Console.WriteLine("[BB-CLOCK] phase=boost status=skip reason=clock_tower_not_found_or_unavailable");
                return false;
            }

            Console.WriteLine($"[BB-CLOCK] phase=boost status=pending step=open_clock template=\"{clockTemplate}\"");
            if (Sleep(900, token)) return false;

            int clockLevel = ReadClockTowerLevel();
            int timeGainedMinutes = CalculateTimeGainedMinutes(clockLevel);
            Console.WriteLine($"[BB-CLOCK] phase=time_gained status={(clockLevel > 0 ? "success" : "fallback")} level={clockLevel} minutes={timeGainedMinutes}");

            if (!TapFirstVisible(BoostTemplates, BoostThreshold, ActionButtonRoi, token, out string boostTemplate))
            {
                Console.WriteLine("[BB-CLOCK] phase=boost status=skip reason=boost_button_not_found");
                SafeDismiss(token);
                return false;
            }

            Console.WriteLine($"[BB-CLOCK] phase=boost status=pending step=boost_clicked template=\"{boostTemplate}\"");
            if (Sleep(900, token)) return false;

            // Một số phiên bản hiện thêm nút xác nhận Boost lần hai.
            if (TapFirstVisible(BoostTemplates, BoostThreshold, ActionButtonRoi, token, out string confirmTemplate))
            {
                Console.WriteLine($"[BB-CLOCK] phase=boost status=pending step=confirm template=\"{confirmTemplate}\"");
                Sleep(1200, token);
            }

            SafeDismiss(token);
            Console.WriteLine("[BB-CLOCK] phase=boost status=success");
            return true;
        }

        private int ReadClockTowerLevel()
        {
            using Mat? screenshot = _adb.TakeScreenshot();
            if (screenshot == null || screenshot.Empty()) return 0;

            Rect safe = ImageUtils.ClampRect(ClockLevelRoi, screenshot.Width, screenshot.Height);
            if (safe.Width <= 0 || safe.Height <= 0) return 0;

            if (_vision.TryExtractNumericalMetrics(screenshot, safe, out int value, out double confidence, useRgbThresh: true)
                || _vision.TryExtractNumericalMetrics(screenshot, safe, out value, out confidence))
            {
                int level = NormalizeClockTowerLevel(value);
                if (level > 0)
                {
                    Console.WriteLine($"[BB-CLOCK] phase=ocr status=success item=clock_tower_level raw={value} level={level} confidence={confidence:F2}");
                    return level;
                }

                Console.WriteLine($"[BB-CLOCK] phase=ocr status=skip item=clock_tower_level raw={value} confidence={confidence:F2} reason=implausible");
            }

            Console.WriteLine("[BB-CLOCK] phase=ocr status=fail item=clock_tower_level");
            return 0;
        }

        private static int NormalizeClockTowerLevel(int raw)
        {
            if (raw >= 1 && raw <= 10) return raw;

            // Light OCR đôi khi ghép chữ số từ chuỗi "Level 10" thành 110/1010.
            string digits = Math.Abs(raw).ToString();
            for (int len = Math.Min(2, digits.Length); len >= 1; len--)
            {
                string suffix = digits.Substring(digits.Length - len, len);
                if (int.TryParse(suffix, out int suffixValue) && suffixValue >= 1 && suffixValue <= 10)
                {
                    return suffixValue;
                }
            }

            return 0;
        }

        private static int CalculateTimeGainedMinutes(int clockTowerLevel)
        {
            // Port từ MBR ClockTimeGained(): boost length * (10 - 1), theo phút.
            return clockTowerLevel switch
            {
                1 => 126,
                2 => 144,
                3 => 162,
                4 => 180,
                5 => 198,
                6 => 216,
                7 => 236,
                8 => 252,
                9 => 270,
                10 => 288,
                _ => 270
            };
        }

        private bool TapFirstVisible(string[] templates, double threshold, Rect? roi, CancellationToken token, out string matchedTemplate)
        {
            matchedTemplate = string.Empty;
            if (token.IsCancellationRequested) return false;

            using Mat? screenshot = _adb.TakeScreenshot();
            if (screenshot == null || screenshot.Empty()) return false;

            foreach (string template in templates)
            {
                Point? center = _vision.FindElement(screenshot, template, threshold, roi, out double score);
                if (center == null) continue;

                matchedTemplate = template;
                Console.WriteLine($"[BB-CLOCK] phase=template status=success template=\"{template}\" score={score:F2} center=({center.Value.X},{center.Value.Y})");
                _adb.Tap(center.Value.X, center.Value.Y);
                return true;
            }

            return false;
        }

        private void SafeDismiss(CancellationToken token)
        {
            if (token.IsCancellationRequested) return;
            _adb.Tap(140, 606);
            Sleep(400, token);
        }

        private static bool Sleep(int milliseconds, CancellationToken token)
        {
            return token.WaitHandle.WaitOne(milliseconds);
        }
    }
}
