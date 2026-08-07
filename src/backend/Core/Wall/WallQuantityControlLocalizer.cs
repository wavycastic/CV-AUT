using System;
using System.Collections.Generic;
using System.Linq;
using OpenCvSharp;
using Point = OpenCvSharp.Point;

namespace CvAut
{
    internal enum WallQuantityControlRole { UpgradeMore, RemoveOne, AddOne, AddTen }

    internal sealed record WallQuantityControlInfo(bool Found, WallQuantityControlRole Role, int Delta, bool Available, Rect ButtonRect, Point TapPoint, double Score, string Method, string Reason);

    internal sealed class WallQuantityPanelInfo
    {
        public WallHeaderInfo Header { get; init; } = new(false, 0, WallSelectionMode.Unknown, 0, 0, default, "not_read");
        public IReadOnlyList<WallQuantityControlInfo> Controls { get; init; } = Array.Empty<WallQuantityControlInfo>();
        public WallPanelLocalizationResult Panel { get; init; } = new();
    }

    internal static class WallQuantityControlLocalizer
    {
        public static WallQuantityPanelInfo Localize(
            IVisionEngine vision,
            Mat screenshot,
            WallSelectionMode? modeOverride = null,
            int selectedCountOverride = 0)
        {
            WallPanelLocalizationResult panel = WallDynamicLocalizer.LocalizePanelAndButtons(vision, screenshot);
            WallHeaderInfo header = WallHeaderInspector.Inspect(vision, screenshot);
            if (modeOverride.HasValue)
            {
                header = header with
                {
                    Found = true,
                    Mode = modeOverride.Value,
                    SelectedCount = selectedCountOverride > 0 ? selectedCountOverride : header.SelectedCount,
                    Reason = $"runtime_expected_mode:{modeOverride.Value}; source={header.Reason}"
                };
            }
            int resourceX = panel.GoldInfo.Found ? panel.GoldInfo.ButtonRect.X : int.MaxValue;
            List<Rect> candidates = panel.DetectedButtons.Where(r => r.X < resourceX).OrderBy(r => r.X).ToList();
            var controls = new List<WallQuantityControlInfo>();

            if (header.Mode == WallSelectionMode.Single && candidates.Count > 0)
            {
                Rect button = candidates[^1];
                controls.Add(Build(screenshot, button, WallQuantityControlRole.UpgradeMore, 0, "single_rightmost_non_resource"));
            }
            else if (header.Mode == WallSelectionMode.Multi)
            {
                if (candidates.Count >= 1) controls.Add(Build(screenshot, candidates[0], WallQuantityControlRole.RemoveOne, -1, "multi_leftmost"));
                if (candidates.Count >= 3)
                {
                    controls.Add(Build(screenshot, candidates[^2], WallQuantityControlRole.AddTen, 10, "multi_penultimate"));
                    controls.Add(Build(screenshot, candidates[^1], WallQuantityControlRole.AddOne, 1, "multi_rightmost"));
                }
                else if (candidates.Count >= 2)
                {
                    controls.Add(Build(screenshot, candidates[^1], WallQuantityControlRole.AddOne, 1, "multi_rightmost"));
                }
            }
            return new WallQuantityPanelInfo { Header = header, Controls = controls, Panel = panel };
        }

        private static WallQuantityControlInfo Build(Mat screenshot, Rect rect, WallQuantityControlRole role, int delta, string method)
        {
            Rect safe = ImageUtils.ClampRect(rect, screenshot.Width, screenshot.Height);
            Point tap = new(safe.X + safe.Width / 2, safe.Y + safe.Height / 2);
            using Mat crop = new(screenshot, safe);
            using Mat hsv = new();
            Cv2.CvtColor(crop, hsv, ColorConversionCodes.BGR2HSV);
            Scalar mean = Cv2.Mean(hsv);
            bool available = mean.Val1 >= 35 && mean.Val2 >= 45;
            double score = Math.Min(1.0, (mean.Val1 / 255.0 + mean.Val2 / 255.0) / 2.0);
            return new(true, role, delta, available, safe, tap, score, method, available ? "ok" : "control_disabled");
        }
    }
}
