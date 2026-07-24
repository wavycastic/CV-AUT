using System;
using System.IO;
using System.Threading;
using OpenCvSharp;
using Point = OpenCvSharp.Point;

namespace CvAut
{
    /// <summary>
    /// Điều hướng Làng đêm / Builder Base.
    /// Phase đầu chỉ làm nhiệm vụ nhận diện và chuyển làng, port lại ý tưởng từ MBR
    /// nhưng dùng ADB + VisionEngine của dự án hiện tại.
    /// </summary>
    internal sealed class BuilderBaseNavigator
    {
        private readonly IVillageSwitchIO _io;
        private readonly Func<int, CancellationToken, bool> _sleep;

        private const double MainVillageThreshold = 0.70;
        private const double BuilderBaseThreshold = 0.70;
        private const double SwitchButtonThreshold = 0.62;
        private const int SwitchAttempts = 5;
        private const int SwitchPollIntervalMs = 250;
        private const int SwitchVerifyTimeoutMs = 5500;

        // Same village-marker search band used by MBR (150,600,680,720 on 860x780),
        // scaled to our 1600x900 screenshots.
        private static readonly Rect VillageMarkerRoi = Rect.FromLTRB(279, 692, 1265, 831);
        private static readonly Rect MainVillageMarkerRoi = VillageMarkerRoi;
        private static readonly Rect BuilderBaseMarkerRoi = VillageMarkerRoi;
        private static readonly Rect SwitchButtonRoi = Rect.FromLTRB(0, 360, 520, 850);
        private static readonly Rect SwitchToMainButtonRoi = Rect.FromLTRB(0, 35, 260, 170);
        private static readonly Rect StageTunnelRoi = Rect.FromLTRB(0, 90, 640, 420);
        private static readonly Rect MainVillageUiRoi = Rect.FromLTRB(1180, 420, 1599, 890);
        private static readonly Rect VillageTerrainRoi = Rect.FromLTRB(180, 80, 1280, 850);

        private static readonly string[] MainVillageTemplates =
        {
            @"village\Page\MainVillage\MainVillage_100_90",
            @"village\Page\MainVillage\GobBuilder_100_92",
            @"ui\game_setting",
            @"ui\shop",
            "game_setting",
            "shop"
        };

        private static readonly string[] BuilderBaseTemplates =
        {
            @"village\Page\BuilderBase\BuilderEye_0_90",
            @"village\Page\BuilderBase\MachineEye_0_90",
            @"ui\builder_available",
            @"ui\x_night"
        };

        private static readonly string[] MainVillageMarkerTemplates =
        {
            @"village\Page\MainVillage\MainVillage_100_90",
            @"village\Page\MainVillage\GobBuilder_100_92"
        };

        private static readonly string[] MainVillageUiTemplates =
        {
            @"ui\game_setting",
            @"ui\shop",
            "game_setting",
            "shop"
        };

        private static readonly string[] BuilderBaseMarkerTemplates =
        {
            @"village\Page\BuilderBase\BuilderEye_0_90",
            @"village\Page\BuilderBase\MachineEye_0_90"
        };

        private static readonly string[] BuilderBaseFallbackTemplates =
        {
            @"ui\builder_available",
            @"ui\x_night"
        };

        private static readonly string[] SwitchToBuilderTemplates =
        {
            @"ui\switch_builder",
            @"clan_games\switch_builder"
        };

        private static readonly string[] SwitchToMainTemplates =
        {
            @"ui\home",
            @"ui\return_home",
            @"ui\return_home_n"
        };

        private static readonly string[] StageTunnelTemplates =
        {
            @"ui\otto_tunnel",
            @"ui\builder_tunnel",
            @"ui\tunnel",
            @"builder_base\otto_tunnel",
            @"builder_base\builder_tunnel",
            @"builder_base\tunnel"
        };

        public BuilderBaseNavigator(ADBHelper adb, VisionEngine vision)
            : this(new VillageSwitchIO(adb, vision))
        {
        }

        internal BuilderBaseNavigator(IVillageSwitchIO io)
        {
            _io = io;
            _sleep = Sleep;
        }

        internal BuilderBaseNavigator(IVillageSwitchIO io, Func<int, CancellationToken, bool> sleep)
        {
            _io = io;
            _sleep = sleep;
        }

        public bool IsOnBuilderBase()
        {
            using Mat? screenshot = _io.TakeScreenshot();
            if (screenshot == null || screenshot.Empty())
            {
                Log("detect", "fail", "builder_base", null, "reason=screenshot_empty");
                return false;
            }

            return IsOnBuilderBase(screenshot, log: true);
        }

        private bool IsOnBuilderBase(Mat screenshot, bool log)
        {
            if (screenshot.Empty()) return false;

            if (TryFindAny(screenshot, BuilderBaseMarkerTemplates, BuilderBaseThreshold, BuilderBaseMarkerRoi, out string matched, out double score, out Point center))
            {
                if (log)
                {
                    Log("detect", "success", "builder_base", null, $"layer=map_marker template=\"{matched}\" score={score:F2} center=({center.X},{center.Y}) roi=mbr_marker");
                }

                return true;
            }

            if (log)
            {
                LogDebug("detect", "retry", "builder_base", null, "layer=map_marker reason=no_match");
            }

            if (TryFindAny(screenshot, SwitchToMainTemplates, SwitchButtonThreshold, SwitchToMainButtonRoi, out matched, out score, out center))
            {
                if (log)
                {
                    Log("detect", "success", "builder_base", null, $"layer=return_home_marker template=\"{matched}\" score={score:F2} center=({center.X},{center.Y}) roi=right_home_button");
                }

                return true;
            }

            if (log)
            {
                LogDebug("detect", "retry", "builder_base", null, "layer=return_home_marker reason=no_match");
            }

            if (TryFindAny(screenshot, BuilderBaseFallbackTemplates, BuilderBaseThreshold, BuilderBaseMarkerRoi, out matched, out score, out center))
            {
                if (log)
                {
                    Log("detect", "success", "builder_base", null, $"layer=fallback_marker template=\"{matched}\" score={score:F2} center=({center.X},{center.Y}) roi=legacy_builder_marker");
                }

                return true;
            }

            if (log)
            {
                LogDebug("detect", "retry", "builder_base", null, "layer=fallback_marker reason=no_match");
            }

            if (TryDetectBuilderBaseNightPalette(screenshot, out double nightScore, out double darkScore))
            {
                if (log)
                {
                    Log("detect", "success", "builder_base", null, $"layer=night_palette night_score={nightScore:F2} dark_score={darkScore:F2} roi=village_terrain reason=template_less");
                }

                return true;
            }

            if (log)
            {
                LogDebug("detect", "retry", "builder_base", null, $"layer=night_palette reason=no_match night_score={nightScore:F2} dark_score={darkScore:F2}");
                Log("detect", "fail", "builder_base", null, "reason=no_tier_matched tiers=map_marker,return_home_marker,fallback_marker,night_palette");
            }

            return false;
        }

        public bool IsOnMainVillage()
        {
            using Mat? screenshot = _io.TakeScreenshot();
            if (screenshot == null || screenshot.Empty())
            {
                Log("detect", "fail", "main_village", null, "reason=screenshot_empty");
                return false;
            }

            return IsOnMainVillage(screenshot, log: true);
        }

        private bool IsOnMainVillage(Mat screenshot, bool log)
        {
            if (screenshot.Empty()) return false;

            if (TryFindAny(screenshot, MainVillageMarkerTemplates, MainVillageThreshold, MainVillageMarkerRoi, out string matched, out double score, out Point center))
            {
                if (log)
                {
                    Log("detect", "success", "main_village", null, $"layer=map_marker template=\"{matched}\" score={score:F2} center=({center.X},{center.Y}) roi=mbr_marker");
                }

                return true;
            }

            if (log)
            {
                Log("detect", "retry", "main_village", null, "layer=map_marker reason=no_match");
            }

            if (TryDetectBuilderBaseNightPalette(screenshot, out double nightScore, out double darkScore))
            {
                if (log)
                {
                    Log("detect", "retry", "main_village", null, $"layer=night_palette_guard reason=builder_base_like night_score={nightScore:F2} dark_score={darkScore:F2}");
                    Log("detect", "fail", "main_village", null, "reason=builder_base_palette_guard");
                }

                return false;
            }

            if (TryFindAny(screenshot, MainVillageUiTemplates, MainVillageThreshold, MainVillageMarkerRoi, out matched, out score, out center))
            {
                if (log)
                {
                    Log("detect", "success", "main_village", null, $"layer=primary_ui template=\"{matched}\" score={score:F2} center=({center.X},{center.Y}) roi=left_ui");
                }

                return true;
            }

            if (log)
            {
                Log("detect", "retry", "main_village", null, "layer=primary_ui reason=no_match");
            }

            if (TryFindAny(screenshot, MainVillageTemplates, MainVillageThreshold, null, out matched, out score, out center))
            {
                if (log)
                {
                    Log("detect", "success", "main_village", null, $"layer=fallback_ui template=\"{matched}\" score={score:F2} center=({center.X},{center.Y}) roi=full_screen");
                }

                return true;
            }

            if (log)
            {
                Log("detect", "fail", "main_village", null, "reason=no_tier_matched tiers=map_marker,primary_ui,fallback_ui");
            }

            return false;
        }

        public bool SwitchToBuilderBase(CancellationToken token)
        {
            Log("switch", "start", "builder_base");
            if (IsOnBuilderBase())
            {
                Log("switch", "success", "builder_base", null, "reason=already_there");
                return true;
            }

            for (int attempt = 1; attempt <= SwitchAttempts && !token.IsCancellationRequested; attempt++)
            {
                // MBR luôn ZoomOut trước khi tìm thuyền để đảm bảo boat/tunnel nằm trong viewport.
                Log("switch", "pending", "builder_base", attempt, "action=zoom_out");
                ZoomOutApprox(token);
                if (_sleep(500, token)) return false;

                using Mat? screenshot = _io.TakeScreenshot();
                if (screenshot == null || screenshot.Empty())
                {
                    Log("switch", "fail", "builder_base", attempt, "reason=screenshot_empty");
                    return false;
                }

                if (IsOnBuilderBase(screenshot, log: false))
                {
                    Log("switch", "success", "builder_base", attempt, "reason=detected_after_zoom");
                    return true;
                }

                if (TryTapFirst(screenshot, SwitchToBuilderTemplates, SwitchButtonThreshold, SwitchButtonRoi, out string matched, out double score, out Point tapPoint))
                {
                    Log("switch", "tap_switch", "builder_base", attempt, $"template={matched} score={score:F2} tap=({tapPoint.X},{tapPoint.Y}) roi=switch_button");
                }
                else
                {
                    // Fallback: tap tọa độ cố định thuyền ở bờ trái dưới
                    Log("switch", "fallback_tap", "builder_base", attempt, "reason=boat_template_not_found x=150 y=690");
                    _io.Tap(150, 690);
                }

                if (WaitForVillage("builder_base", SwitchVerifyTimeoutMs, token, attempt))
                {
                    Log("switch", "success", "builder_base", attempt);
                    return true;
                }

                Log("switch", "fail", "builder_base", attempt, "reason=verify_timeout");
            }

            Log("switch", "fail", "builder_base", null, $"reason=not_detected_after_attempts attempts={SwitchAttempts}");
            return false;
        }

        public bool SwitchToMainVillage(CancellationToken token)
        {
            Log("switch", "start", "main_village");
            if (IsOnMainVillage() && !IsOnBuilderBase())
            {
                Log("switch", "success", "main_village", null, "reason=already_there");
                return true;
            }

            for (int attempt = 1; attempt <= SwitchAttempts && !token.IsCancellationRequested; attempt++)
            {
                Log("switch", "pending", "main_village", attempt, "action=zoom_out");
                ZoomOutApprox(token);
                if (_sleep(500, token)) return false;

                using Mat? screenshot = _io.TakeScreenshot();
                if (screenshot == null || screenshot.Empty())
                {
                    Log("switch", "fail", "main_village", attempt, "reason=screenshot_empty");
                    return false;
                }

                if (IsOnMainVillage(screenshot, log: false) && !IsOnBuilderBase(screenshot, log: false))
                {
                    Log("switch", "success", "main_village", attempt, "reason=detected_after_zoom");
                    return true;
                }

                if (TryTapFirst(screenshot, SwitchToMainTemplates, SwitchButtonThreshold, SwitchToMainButtonRoi, out string matched, out double score, out Point tapPoint))
                {
                    Log("switch", "tap_switch", "main_village", attempt, $"template={matched} score={score:F2} tap=({tapPoint.X},{tapPoint.Y}) roi=switch_button");
                }
                else
                {
                    Point fallback = GetMainVillageFallbackTap(attempt);
                    Log("switch", "fallback_tap", "main_village", attempt, $"reason=boat_template_not_found x={fallback.X} y={fallback.Y}");
                    _io.Tap(fallback.X, fallback.Y);
                }

                if (WaitForVillage("main_village", SwitchVerifyTimeoutMs, token, attempt))
                {
                    Log("switch", "success", "main_village", attempt);
                    return true;
                }

                Log("switch", "fail", "main_village", attempt, "reason=verify_timeout");
            }

            Log("switch", "fail", "main_village", null, $"reason=not_detected_after_attempts attempts={SwitchAttempts}");
            return false;
        }

        public bool SwitchToOttoVillage(CancellationToken token)
        {
            return SwitchBuilderBaseStage(
                targetStage: "otto",
                fallbackX: 210,
                fallbackY: 170,
                token);
        }

        public bool SwitchToBuilderBaseStage1(CancellationToken token)
        {
            return SwitchBuilderBaseStage(
                targetStage: "builder_base",
                fallbackX: 210,
                fallbackY: 170,
                token);
        }

        private bool SwitchBuilderBaseStage(string targetStage, int fallbackX, int fallbackY, CancellationToken token)
        {
            Log("switch_stage", "start", targetStage);
            if (!IsOnBuilderBase())
            {
                Log("switch_stage", "fail", targetStage, null, "reason=not_on_builder_base");
                return false;
            }

            for (int attempt = 1; attempt <= 3 && !token.IsCancellationRequested; attempt++)
            {
                ZoomOutApprox(token);

                using Mat? screenshot = _io.TakeScreenshot();
                if (screenshot == null || screenshot.Empty()) return false;

                if (TryTapStageTunnel(screenshot, targetStage, attempt))
                {
                    if (_sleep(2600, token)) return false;
                }
                else
                {
                    // MBR SwitchToBuilderBase clicks BBTunnel/OOTunnel with offsets. Until those PNGs exist,
                    // use the old coordinate fallback but mark it as unverified-template in logs.
                    Log("switch_stage", "fallback_tap", targetStage, attempt, $"reason=tunnel_template_not_found x={fallbackX} y={fallbackY}");
                    _io.Tap(fallbackX, fallbackY);
                    if (_sleep(2600, token)) return false;
                }

                ZoomOutApprox(token);
                if (IsOnBuilderBase())
                {
                    Log("switch_stage", "success", targetStage, attempt);
                    return true;
                }
            }

            Log("switch_stage", "fail", targetStage, null, "reason=not_detected_after_attempts");
            return false;
        }

        private bool TryTapStageTunnel(Mat screenshot, string targetStage, int attempt)
        {
            foreach (string template in StageTunnelTemplates)
            {
                Point? center = _io.FindElement(screenshot, template, SwitchButtonThreshold, StageTunnelRoi, out double score);
                if (center == null) continue;

                int offsetX = template.Contains("otto", StringComparison.OrdinalIgnoreCase) ? -45 : -40;
                int offsetY = template.Contains("otto", StringComparison.OrdinalIgnoreCase) ? 15 : 25;
                int tapX = Math.Clamp(center.Value.X + offsetX, 0, 1599);
                int tapY = Math.Clamp(center.Value.Y + offsetY, 0, 899);
                Log("switch_stage", "tap_tunnel", targetStage, attempt, $"template={template} score={score:F2} center=({center.Value.X},{center.Value.Y}) tap=({tapX},{tapY})");
                _io.Tap(tapX, tapY);
                return true;
            }

            return false;
        }

        private bool TryTapFirst(Mat screenshot, string[] templates, double threshold, Rect? roi, out string matchedTemplate, out double matchedScore, out Point tapPoint)
        {
            return TemplateSearch.TryTapFirst(screenshot, _io.FindElement, templates, threshold, roi, _io.Tap, out matchedTemplate, out matchedScore, out tapPoint);
        }

        private bool TryFindAny(Mat screenshot, string[] templates, double threshold, Rect? roi, out string matchedTemplate, out double matchedScore, out Point center)
        {
            return TemplateSearch.TryFindFirst(screenshot, _io.FindElement, templates, threshold, roi, out matchedTemplate, out matchedScore, out center);
        }

        private bool WaitForVillage(string targetVillage, int timeoutMs, CancellationToken token, int attempt)
        {
            DateTime deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
            Log("switch", "pending", targetVillage, attempt, $"action=verify_switch timeout_ms={timeoutMs} poll_ms={SwitchPollIntervalMs}");

            bool lastOnBuilderBase = false;
            bool lastOnMainVillage = false;

            while (DateTime.UtcNow < deadline && !token.IsCancellationRequested)
            {
                if (_sleep(SwitchPollIntervalMs, token)) return false;

                using Mat? screenshot = _io.TakeScreenshot();
                if (screenshot == null || screenshot.Empty()) continue;

                bool onBuilderBase = IsOnBuilderBase(screenshot, log: false);
                bool onMainVillage = IsOnMainVillage(screenshot, log: false);
                lastOnBuilderBase = onBuilderBase;
                lastOnMainVillage = onMainVillage;

                if (targetVillage == "builder_base" && onBuilderBase)
                {
                    return true;
                }

                if (targetVillage == "main_village" && onMainVillage && !onBuilderBase)
                {
                    return true;
                }
            }

            using (Mat? screenshot = _io.TakeScreenshot())
            {
                if (screenshot != null && !screenshot.Empty())
                {
                    SaveDebugScreenshot(screenshot, $"{targetVillage}_verify_timeout_attempt_{attempt}");
                }
            }

            Log("switch", "fail", targetVillage, attempt, $"reason=verify_timeout last_builder_base={lastOnBuilderBase} last_main_village={lastOnMainVillage}");

            return false;
        }

        private static void SaveDebugScreenshot(Mat screenshot, string phase)
        {
            try
            {
                string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SimpliMixi", "logs", "BuilderBaseNavigation");
                Directory.CreateDirectory(dir);

                string safePhase = SafeFileName(phase);
                string path = Path.Combine(dir, $"{safePhase}_{DateTime.UtcNow:yyyyMMdd_HHmmss_fff}.png");
                Cv2.ImWrite(path, screenshot);

                Log("debug_screenshot", "saved", "builder_base_navigation", null, $"phase={safePhase} path=\"{path}\"");
            }
            catch (Exception ex)
            {
                Log("debug_screenshot", "fail", "builder_base_navigation", null, $"reason=\"{ex.Message}\"");
            }
        }

        private static string SafeFileName(string value)
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

        private static bool TryDetectBuilderBaseNightPalette(Mat screenshot, out double nightScore, out double darkScore)
        {
            nightScore = 0;
            darkScore = 0;

            if (screenshot.Empty()) return false;

            Rect roi = ClampRect(VillageTerrainRoi, screenshot.Width, screenshot.Height);
            if (roi.Width <= 0 || roi.Height <= 0) return false;

            using Mat crop = new Mat(screenshot, roi);
            using Mat hsv = new Mat();
            Cv2.CvtColor(crop, hsv, ColorConversionCodes.BGR2HSV);

            int sampleCount = 0;
            int nightCount = 0;
            int darkCount = 0;

            for (int y = 0; y < hsv.Rows; y += 8)
            {
                for (int x = 0; x < hsv.Cols; x += 8)
                {
                    Vec3b px = hsv.At<Vec3b>(y, x);
                    int hue = px.Item0;
                    int saturation = px.Item1;
                    int value = px.Item2;

                    sampleCount++;
                    if (value < 125)
                    {
                        darkCount++;
                    }

                    // Builder Base home screens are dominated by dark blue/cyan terrain.
                    // This is intentionally a fallback after template checks, not the only signal.
                    if (hue >= 88 && hue <= 132 && saturation >= 24 && value >= 22 && value <= 185)
                    {
                        nightCount++;
                    }
                }
            }

            if (sampleCount == 0) return false;

            nightScore = nightCount / (double)sampleCount;
            darkScore = darkCount / (double)sampleCount;

            return nightScore >= 0.18 && darkScore >= 0.38;
        }

        private static Point GetMainVillageFallbackTap(int attempt)
        {
            return attempt switch
            {
                1 => new Point(1160, 210),
                2 => new Point(1150, 196),
                3 => new Point(1136, 184),
                _ => new Point(1120, 172),
            };
        }

        private static Rect ClampRect(Rect rect, int width, int height)
        {
            int left = Math.Clamp(rect.Left, 0, width);
            int top = Math.Clamp(rect.Top, 0, height);
            int right = Math.Clamp(rect.Right, left, width);
            int bottom = Math.Clamp(rect.Bottom, top, height);
            return Rect.FromLTRB(left, top, right, bottom);
        }

        private static void Log(string phase, string status, string target, int? attempt = null, string? details = null)
        {
            Console.WriteLine(BuilderBaseNavigationLog.Format(phase, status, target, attempt, details));
        }

        private static void LogDebug(string phase, string status, string target, int? attempt = null, string? details = null)
        {
            Console.WriteLine("[DEBUG]" + BuilderBaseNavigationLog.Format(phase, status, target, attempt, details));
        }

        public void ZoomOutApprox(CancellationToken token)
        {
            if (token.IsCancellationRequested) return;
            _io.PinchInZoomOut(count: 2, durationMs: 400, intervalMs: 300);
            _sleep(500, token);
        }

        private static bool Sleep(int milliseconds, CancellationToken token)
        {
            return token.WaitHandle.WaitOne(milliseconds);
        }
    }

    internal interface IVillageSwitchIO
    {
        Mat? TakeScreenshot();
        Point? FindElement(Mat screenshot, string templateName, double threshold, Rect? roi, out double score);
        void Tap(int x, int y);
        void PinchInZoomOut(int count, int durationMs, int intervalMs);
    }

    internal sealed class VillageSwitchIO : IVillageSwitchIO
    {
        private readonly ADBHelper _adb;
        private readonly VisionEngine _vision;

        internal VillageSwitchIO(ADBHelper adb, VisionEngine vision)
        {
            _adb = adb;
            _vision = vision;
        }

        public Mat? TakeScreenshot() => _adb.TakeScreenshot();

        public Point? FindElement(Mat screenshot, string templateName, double threshold, Rect? roi, out double score)
        {
            return _vision.FindElement(screenshot, templateName, threshold, roi, out score);
        }

        public void Tap(int x, int y) => _adb.Tap(x, y);

        public void PinchInZoomOut(int count, int durationMs, int intervalMs)
        {
            _adb.PinchInZoomOut(count, durationMs, intervalMs);
        }
    }

    internal static class BuilderBaseNavigationLog
    {
        internal static string Format(string phase, string status, string target, int? attempt = null, string? details = null)
        {
            string attemptText = attempt.HasValue ? $" attempt={attempt.Value}" : string.Empty;
            string detailsText = string.IsNullOrWhiteSpace(details) ? string.Empty : $" details=\"{Sanitize(details)}\"";
            return $"[BB_NAV] phase={SanitizeToken(phase)} status={SanitizeToken(status)} target={SanitizeToken(target)}{attemptText}{detailsText}";
        }

        private static string SanitizeToken(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return "unknown";
            }

            return Sanitize(value).Replace(' ', '_');
        }

        private static string Sanitize(string value)
        {
            return value
                .Replace("\r", " ", StringComparison.Ordinal)
                .Replace("\n", " ", StringComparison.Ordinal)
                .Replace("\"", "'", StringComparison.Ordinal)
                .Trim();
        }
    }
}
