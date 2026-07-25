using OpenCvSharp;
using System;
using Point = OpenCvSharp.Point;

namespace CvAut
{
    /// <summary>
    /// Decides whether the current screenshot shows the Main Village or the Builder Base.
    /// The tier order (map marker, return-home marker, fallback icons, night palette) and all
    /// log strings are preserved exactly as in the original BuilderBaseNavigator.
    /// </summary>
    internal sealed class VillagePresenceDetector
    {
        private readonly IVillageSwitchIO _io;

        internal VillagePresenceDetector(IVillageSwitchIO io)
        {
            _io = io;
        }

        internal bool IsOnBuilderBase(Mat screenshot, bool log)
        {
            if (screenshot.Empty()) return false;

            if (TryFindAny(screenshot, BuilderBaseNavigationLayout.BuilderBaseMarkerTemplates, BuilderBaseNavigationLayout.BuilderBaseThreshold, BuilderBaseNavigationLayout.BuilderBaseMarkerRoi, out string matched, out double score, out Point center))
            {
                if (log)
                {
                    BuilderBaseNavigationLog.Write("detect", "success", "builder_base", null, $"layer=map_marker template=\"{matched}\" score={score:F2} center=({center.X},{center.Y}) roi=mbr_marker");
                }

                return true;
            }

            if (log)
            {
                BuilderBaseNavigationLog.WriteDebug("detect", "retry", "builder_base", null, "layer=map_marker reason=no_match");
            }

            if (TryFindAny(screenshot, BuilderBaseNavigationLayout.SwitchToMainTemplates, BuilderBaseNavigationLayout.SwitchButtonThreshold, BuilderBaseNavigationLayout.SwitchToMainButtonRoi, out matched, out score, out center))
            {
                if (log)
                {
                    BuilderBaseNavigationLog.Write("detect", "success", "builder_base", null, $"layer=return_home_marker template=\"{matched}\" score={score:F2} center=({center.X},{center.Y}) roi=right_home_button");
                }

                return true;
            }

            if (log)
            {
                BuilderBaseNavigationLog.WriteDebug("detect", "retry", "builder_base", null, "layer=return_home_marker reason=no_match");
            }

            if (TryFindAny(screenshot, BuilderBaseNavigationLayout.BuilderBaseFallbackTemplates, BuilderBaseNavigationLayout.BuilderBaseThreshold, BuilderBaseNavigationLayout.BuilderBaseMarkerRoi, out matched, out score, out center))
            {
                if (log)
                {
                    BuilderBaseNavigationLog.Write("detect", "success", "builder_base", null, $"layer=fallback_marker template=\"{matched}\" score={score:F2} center=({center.X},{center.Y}) roi=legacy_builder_marker");
                }

                return true;
            }

            if (log)
            {
                BuilderBaseNavigationLog.WriteDebug("detect", "retry", "builder_base", null, "layer=fallback_marker reason=no_match");
            }

            if (TryDetectBuilderBaseNightPalette(screenshot, out double nightScore, out double darkScore))
            {
                if (log)
                {
                    BuilderBaseNavigationLog.Write("detect", "success", "builder_base", null, $"layer=night_palette night_score={nightScore:F2} dark_score={darkScore:F2} roi=village_terrain reason=template_less");
                }

                return true;
            }

            if (log)
            {
                BuilderBaseNavigationLog.WriteDebug("detect", "retry", "builder_base", null, $"layer=night_palette reason=no_match night_score={nightScore:F2} dark_score={darkScore:F2}");
                BuilderBaseNavigationLog.Write("detect", "fail", "builder_base", null, "reason=no_tier_matched tiers=map_marker,return_home_marker,fallback_marker,night_palette");
            }

            return false;
        }

        internal bool IsOnMainVillage(Mat screenshot, bool log)
        {
            if (screenshot.Empty()) return false;

            if (TryFindAny(screenshot, BuilderBaseNavigationLayout.MainVillageMarkerTemplates, BuilderBaseNavigationLayout.MainVillageThreshold, BuilderBaseNavigationLayout.MainVillageMarkerRoi, out string matched, out double score, out Point center))
            {
                if (log)
                {
                    BuilderBaseNavigationLog.Write("detect", "success", "main_village", null, $"layer=map_marker template=\"{matched}\" score={score:F2} center=({center.X},{center.Y}) roi=mbr_marker");
                }

                return true;
            }

            if (log)
            {
                BuilderBaseNavigationLog.Write("detect", "retry", "main_village", null, "layer=map_marker reason=no_match");
            }

            if (TryDetectBuilderBaseNightPalette(screenshot, out double nightScore, out double darkScore))
            {
                if (log)
                {
                    BuilderBaseNavigationLog.Write("detect", "retry", "main_village", null, $"layer=night_palette_guard reason=builder_base_like night_score={nightScore:F2} dark_score={darkScore:F2}");
                    BuilderBaseNavigationLog.Write("detect", "fail", "main_village", null, "reason=builder_base_palette_guard");
                }

                return false;
            }

            if (TryFindAny(screenshot, BuilderBaseNavigationLayout.MainVillageUiTemplates, BuilderBaseNavigationLayout.MainVillageThreshold, BuilderBaseNavigationLayout.MainVillageMarkerRoi, out matched, out score, out center))
            {
                if (log)
                {
                    BuilderBaseNavigationLog.Write("detect", "success", "main_village", null, $"layer=primary_ui template=\"{matched}\" score={score:F2} center=({center.X},{center.Y}) roi=left_ui");
                }

                return true;
            }

            if (log)
            {
                BuilderBaseNavigationLog.Write("detect", "retry", "main_village", null, "layer=primary_ui reason=no_match");
            }

            if (TryFindAny(screenshot, BuilderBaseNavigationLayout.MainVillageTemplates, BuilderBaseNavigationLayout.MainVillageThreshold, null, out matched, out score, out center))
            {
                if (log)
                {
                    BuilderBaseNavigationLog.Write("detect", "success", "main_village", null, $"layer=fallback_ui template=\"{matched}\" score={score:F2} center=({center.X},{center.Y}) roi=full_screen");
                }

                return true;
            }

            if (log)
            {
                BuilderBaseNavigationLog.Write("detect", "fail", "main_village", null, "reason=no_tier_matched tiers=map_marker,primary_ui,fallback_ui");
            }

            return false;
        }

        internal bool TryTapFirst(Mat screenshot, string[] templates, double threshold, Rect? roi, out string matchedTemplate, out double matchedScore, out Point tapPoint)
        {
            return TemplateSearch.TryTapFirst(screenshot, _io.FindElement, templates, threshold, roi, _io.Tap, out matchedTemplate, out matchedScore, out tapPoint);
        }

        internal bool TryFindAny(Mat screenshot, string[] templates, double threshold, Rect? roi, out string matchedTemplate, out double matchedScore, out Point center)
        {
            return TemplateSearch.TryFindFirst(screenshot, _io.FindElement, templates, threshold, roi, out matchedTemplate, out matchedScore, out center);
        }

        private static bool TryDetectBuilderBaseNightPalette(Mat screenshot, out double nightScore, out double darkScore)
        {
            nightScore = 0;
            darkScore = 0;

            if (screenshot.Empty()) return false;

            Rect roi = ClampRect(BuilderBaseNavigationLayout.VillageTerrainRoi, screenshot.Width, screenshot.Height);
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

        private static Rect ClampRect(Rect rect, int width, int height)
        {
            int left = Math.Clamp(rect.Left, 0, width);
            int top = Math.Clamp(rect.Top, 0, height);
            int right = Math.Clamp(rect.Right, left, width);
            int bottom = Math.Clamp(rect.Bottom, top, height);
            return Rect.FromLTRB(left, top, right, bottom);
        }
    }
}
