using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using OpenCvSharp;
using Point = OpenCvSharp.Point;

namespace CvAut
{
    internal sealed class BuilderBaseMaintenanceUi
    {
        private readonly IADBHelper _adb;
        private readonly IVisionEngine _vision;
        private readonly string _templatesPath;

        public BuilderBaseMaintenanceUi(IADBHelper adb, IVisionEngine vision, string templatesPath)
        {
            _adb = adb;
            _vision = vision;
            _templatesPath = templatesPath;
        }

        public bool OpenBuilderMenu(CancellationToken token)
        {
            if (TapFirstExisting(BuilderBaseMaintenanceLayout.BuilderHeadTemplates, BuilderBaseMaintenanceLayout.ButtonThreshold, Rect.FromLTRB(600, 0, 900, 110), token, "open_builder_menu"))
                return !Sleep(900, token);
            _adb.Tap(738, 36);
            Sleep(900, token);
            return true;
        }

        public void SafeDismiss(CancellationToken token)
        {
            if (!token.IsCancellationRequested)
            {
                _adb.Tap(140, 606);
                Sleep(350, token);
            }
        }

        public static bool Sleep(int milliseconds, CancellationToken token) => token.WaitHandle.WaitOne(milliseconds);

        public bool TapFirstExisting(string[] templates, double threshold, Rect? roi, CancellationToken token, string phase)
        {
            using Mat? screenshot = _adb.TakeScreenshot();
            if (screenshot == null || screenshot.Empty()) return false;

            if (!TemplateSearch.TryFindFirst(screenshot, FindElementWithExistenceCheck, templates, threshold, roi, out string matched, out double score, out Point center))
                return false;

            Console.WriteLine($"[BB-MAINT] phase={phase} status=found template=\"{matched}\" score={score:F2} x={center.X} y={center.Y}");
            _adb.Tap(center.X, center.Y);
            return true;
        }

        public Point? FindElementWithExistenceCheck(Mat screenshot, string template, double threshold, Rect? roi, out double score)
        {
            score = 0;
            if (!TemplateAssetLoader.Exists(_templatesPath, template)) return null;
            return _vision.FindElement(screenshot, template, threshold, roi, out score);
        }

        public int ReadNumberFromCurrentScreen(Rect roi, int maxPlausible)
        {
            using Mat? screenshot = _adb.TakeScreenshot();
            if (screenshot == null || screenshot.Empty()) return 0;
            Rect safe = ImageUtils.ClampRect(roi, screenshot.Width, screenshot.Height);
            if (safe.Width <= 0 || safe.Height <= 0) return 0;
            if (_vision.TryExtractNumericalMetrics(screenshot, safe, out int value, out _, useRgbThresh: true)
                || _vision.TryExtractNumericalMetrics(screenshot, safe, out value, out _))
            {
                return value >= 0 && value <= maxPlausible ? value : 0;
            }

            return 0;
        }

        public IEnumerable<string> EnumerateTemplateNames(params string[] subdirs)
        {
            foreach (string subdir in subdirs)
                foreach (string name in TemplateAssetLoader.EnumerateNames(_templatesPath, subdir))
                    yield return Path.Combine(subdir, name);
        }

        public string[] GetConfirmTemplates(BuilderBaseUpgradeTarget target, BuilderBaseReportSnapshot report)
        {
            bool canGold = target.AllowGold && report.Gold > 0;
            bool canElixir = target.AllowElixir && report.Elixir > 0;
            if (canGold && canElixir) return BuilderBaseMaintenanceLayout.UpgradeConfirmGold.Concat(BuilderBaseMaintenanceLayout.UpgradeConfirmElixir).ToArray();
            if (canGold) return BuilderBaseMaintenanceLayout.UpgradeConfirmGold;
            if (canElixir) return BuilderBaseMaintenanceLayout.UpgradeConfirmElixir;
            return Array.Empty<string>();
        }

        public static bool IsGoldTemplate(string template) => template.IndexOf("gold", StringComparison.OrdinalIgnoreCase) >= 0;
        public static bool IsElixirTemplate(string template) => template.IndexOf("elixir", StringComparison.OrdinalIgnoreCase) >= 0;

        public static bool IsNearExisting(IEnumerable<Point> points, Point candidate)
        {
            foreach (Point point in points)
            {
                int dx = point.X - candidate.X;
                int dy = point.Y - candidate.Y;
                if (dx * dx + dy * dy <= 55 * 55) return true;
            }

            return false;
        }

        public static bool PixelNear(Mat screenshot, int x, int y, int rgb, int tolerance)
        {
            if (x < 0 || y < 0 || x >= screenshot.Width || y >= screenshot.Height) return false;
            Vec3b pixel = screenshot.At<Vec3b>(y, x);
            int r = (rgb >> 16) & 0xFF;
            int g = (rgb >> 8) & 0xFF;
            int b = rgb & 0xFF;
            return Math.Abs(pixel.Item2 - r) <= tolerance
                && Math.Abs(pixel.Item1 - g) <= tolerance
                && Math.Abs(pixel.Item0 - b) <= tolerance;
        }
    }
}
