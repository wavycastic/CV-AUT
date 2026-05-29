using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using OpenCvSharp;
using Point = OpenCvSharp.Point;
using Size = OpenCvSharp.Size;

namespace CvAut
{
    public sealed class WallUpdater
    {
        private static readonly Rect WallSearchRoi = Rect.FromLTRB(270, 100, 1339, 785);
        private static readonly Rect ValidateRoi = Rect.FromLTRB(235, 561, 1415, 867);
        private static readonly Point HomeMenuPoint = new(738, 36);
        private static readonly Point SwipeStart = new(809, 648);
        private static readonly Point SwipeEnd = new(809, 115);
        private static readonly Point RetrySwipeStart = new(977, 157);
        private static readonly Point RetrySwipeEnd = new(999, 432);
        private static readonly Point DismissPoint = new(1143, 209);
        private static readonly Point ConfirmUpgradePoint = new(1115, 782);
        private static readonly Point SafeClosePoint = new(1229, 25);

        private const double WallSearchThreshold = 0.90;
        private const double ValidateThreshold = 0.88;
        private const int SwipeDurationMs = 600;

        private readonly ADBHelper _adb;
        private readonly VisionEngine _vision;
        private readonly string _templatesPath;
        private int? _savedWallOffset;

        public WallUpdater(ADBHelper adb, VisionEngine vision, string templatesPath)
        {
            _adb = adb;
            _vision = vision;
            _templatesPath = templatesPath;
        }

        public void HandleHomeResources(int wallLevel, int wallGoldThreshold, int wallElixirThreshold)
        {
            var (gold, elixir, _) = IsTarget.ExtractHomeResources(_adb, _vision);
            Console.WriteLine($"[WALL] Home resources: Gold={gold:N0}, Elixir={elixir:N0}.");

            if (gold >= wallGoldThreshold)
            {
                UpgradeWall("gold", wallLevel);
            }

            if (elixir >= wallElixirThreshold)
            {
                UpgradeWall("elixir", wallLevel);
            }
        }

        private bool UpgradeWall(string resource, int wallLevel)
        {
            Console.WriteLine($"[WALL] Trying wall upgrade to level {wallLevel} using {resource}...");

            var triedCoords = new List<Point>();
            Point? validCoord = null;

            for (int attempt = 0; attempt < 3; attempt++)
            {
                List<Point> coords = FindAllWallCoords()
                    .Where(point => !triedCoords.Any(tried => Math.Abs(point.Y - tried.Y) <= 20))
                    .ToList();

                if (coords.Count == 0)
                {
                    Console.WriteLine($"[WALL WARN] Tried all nearby positions but none validated. Skipping {resource}.");
                    _adb.Tap(422, 68);
                    return false;
                }

                Point candidate;
                if (_savedWallOffset.HasValue && _savedWallOffset.Value >= -coords.Count && _savedWallOffset.Value < coords.Count)
                {
                    candidate = coords[IndexFromEnd(coords, _savedWallOffset.Value)];
                }
                else
                {
                    int index = Math.Max(0, coords.Count - 1 - attempt);
                    candidate = coords[index];
                }

                triedCoords.Add(candidate);
                _adb.Tap(candidate.X, candidate.Y);
                Thread.Sleep(1000);
                _adb.Tap(HomeMenuPoint.X, HomeMenuPoint.Y);
                Thread.Sleep(1000);

                if (ValidateWallTap(wallLevel))
                {
                    validCoord = candidate;
                    _savedWallOffset ??= -1 - attempt;
                    break;
                }

                _adb.Tap(DismissPoint.X, DismissPoint.Y);
                Thread.Sleep(500);
            }

            if (!validCoord.HasValue)
            {
                Console.WriteLine("[WALL WARN] No valid wall after 3 attempts. Skipping upgrade.");
                return false;
            }

            Point upgradePoint = GetUpgradePoint(resource);
            _adb.Tap(upgradePoint.X, upgradePoint.Y);
            Thread.Sleep(1000);
            _adb.Tap(ConfirmUpgradePoint.X, ConfirmUpgradePoint.Y);
            Thread.Sleep(500);
            _adb.Tap(SafeClosePoint.X, SafeClosePoint.Y);

            Console.WriteLine($"[WALL] Wall upgraded using {resource}.");
            Thread.Sleep(1000);
            return true;
        }

        private List<Point> FindAllWallCoords()
        {
            PrepareWallSearch();

            string[] templateNames = { "wall.png", "wall_2.png", "wall_3.png", "wall_4.png" };
            string[] templatePaths = templateNames
                .Select(name => Path.Combine(_templatesPath, "walls", name))
                .Where(File.Exists)
                .ToArray();

            if (templatePaths.Length == 0)
            {
                Console.WriteLine($"[WALL WARN] No wall templates found in {Path.Combine(_templatesPath, "walls")}.");
                return new List<Point>();
            }

            for (int attempt = 0; attempt < 7; attempt++)
            {
                if (attempt > 0)
                {
                    _adb.Swipe(RetrySwipeStart.X, RetrySwipeStart.Y, RetrySwipeEnd.X, RetrySwipeEnd.Y, SwipeDurationMs);
                    Thread.Sleep(800);
                }

                using Mat? screenshot = _adb.TakeScreenshot();
                if (screenshot == null || screenshot.Empty())
                {
                    Console.WriteLine("[WALL WARN] Screenshot failed while searching walls.");
                    continue;
                }

                Rect roi = ImageUtils.ClampRect(WallSearchRoi, screenshot.Width, screenshot.Height);
                if (roi.Width <= 0 || roi.Height <= 0)
                {
                    Console.WriteLine("[WALL WARN] Wall ROI is empty; check screenshot size.");
                    return new List<Point>();
                }

                using Mat roiBgr = new Mat(screenshot, roi);
                using Mat roiGray = new Mat();
                Cv2.CvtColor(roiBgr, roiGray, ColorConversionCodes.BGR2GRAY);

                var merged = new List<Point>();
                foreach (string templatePath in templatePaths)
                {
                    merged.AddRange(MatchWallTemplate(roiGray, templatePath));
                }

                List<Point> coords = DedupeCoords(merged, 10)
                    .OrderBy(point => point.Y)
                    .ThenBy(point => point.X)
                    .ToList();

                if (coords.Count > 0)
                {
                    Console.WriteLine($"[WALL] Found {coords.Count} candidate wall coords.");
                    return coords;
                }
            }

            return new List<Point>();
        }

        private void PrepareWallSearch()
        {
            Thread.Sleep(500);
            _adb.Tap(HomeMenuPoint.X, HomeMenuPoint.Y);
            Thread.Sleep(1000);

            for (int i = 0; i < 6; i++)
            {
                _adb.Swipe(SwipeStart.X, SwipeStart.Y, SwipeEnd.X, SwipeEnd.Y, SwipeDurationMs);
            }

            Thread.Sleep(500);
        }

        private IEnumerable<Point> MatchWallTemplate(Mat grayRoi, string templatePath)
        {
            using Mat raw = Cv2.ImRead(templatePath, ImreadModes.Unchanged);
            if (raw.Empty())
            {
                yield break;
            }

            using Mat templateGray = new Mat();
            using Mat? mask = BuildTemplateGrayAndMask(raw, templateGray);
            if (grayRoi.Width < templateGray.Width || grayRoi.Height < templateGray.Height)
            {
                yield break;
            }

            using Mat result = new Mat();
            if (mask != null && !mask.Empty())
            {
                Cv2.MatchTemplate(grayRoi, templateGray, result, TemplateMatchModes.CCoeffNormed, mask);
            }
            else
            {
                Cv2.MatchTemplate(grayRoi, templateGray, result, TemplateMatchModes.CCoeffNormed);
            }

            using Mat dilated = new Mat();
            using Mat kernel = Cv2.GetStructuringElement(MorphShapes.Rect, new Size(3, 3));
            Cv2.Dilate(result, dilated, kernel);

            for (int y = 0; y < result.Rows; y++)
            {
                for (int x = 0; x < result.Cols; x++)
                {
                    float value = result.At<float>(y, x);
                    if (value >= WallSearchThreshold && Math.Abs(value - dilated.At<float>(y, x)) < 0.0001)
                    {
                        yield return new Point(
                            WallSearchRoi.X + x + templateGray.Width / 2,
                            WallSearchRoi.Y + y + templateGray.Height / 2);
                    }
                }
            }
        }

        private bool ValidateWallTap(int wallLevel)
        {
            using Mat? screenshot = _adb.TakeScreenshot();
            if (screenshot == null || screenshot.Empty())
            {
                return false;
            }

            string templatePath = Path.Combine(_templatesPath, "walls", wallLevel.ToString(), "Validate_Upgrade", "verify_wall_level.png");
            if (!File.Exists(templatePath))
            {
                Console.WriteLine($"[WALL WARN] Missing validation template: {templatePath}");
                return false;
            }

            using Mat template = Cv2.ImRead(templatePath, ImreadModes.Color);
            if (template.Empty())
            {
                return false;
            }

            Rect roi = ImageUtils.ClampRect(ValidateRoi, screenshot.Width, screenshot.Height);
            if (roi.Width < template.Width || roi.Height < template.Height)
            {
                return false;
            }

            using Mat searchArea = new Mat(screenshot, roi);
            using Mat result = new Mat();
            Cv2.MatchTemplate(searchArea, template, result, TemplateMatchModes.CCoeffNormed);
            Cv2.MinMaxLoc(result, out _, out double maxVal, out _, out Point maxLoc);

            int centerX = roi.X + maxLoc.X + template.Width / 2;
            if (maxVal >= ValidateThreshold)
            {
                GoldUpgradePoint = new Point(centerX + 175, GoldUpgradePoint.Y);
                ElixirUpgradePoint = new Point(centerX + 350, ElixirUpgradePoint.Y);
                Console.WriteLine($"[WALL] Validation passed score={maxVal:F3}.");
                return true;
            }

            Console.WriteLine($"[WALL] Validation score {maxVal:F3} < {ValidateThreshold:F2}; trying next wall.");
            return false;
        }

        private Point GoldUpgradePoint { get; set; } = new(0, 707);
        private Point ElixirUpgradePoint { get; set; } = new(0, 702);

        private Point GetUpgradePoint(string resource)
        {
            return resource.Equals("gold", StringComparison.OrdinalIgnoreCase)
                ? GoldUpgradePoint
                : ElixirUpgradePoint;
        }

        private static Mat? BuildTemplateGrayAndMask(Mat raw, Mat templateGray)
        {
            if (raw.Channels() == 4)
            {
                Mat[] channels = Cv2.Split(raw);
                try
                {
                    using Mat bgr = new Mat();
                    Cv2.Merge(channels.Take(3).ToArray(), bgr);
                    Cv2.CvtColor(bgr, templateGray, ColorConversionCodes.BGR2GRAY);
                    Mat mask = new Mat();
                    Cv2.Threshold(channels[3], mask, 0, 255, ThresholdTypes.Binary);
                    return mask;
                }
                finally
                {
                    foreach (Mat ch in channels) ch.Dispose();
                }
            }

            Cv2.CvtColor(raw, templateGray, ColorConversionCodes.BGR2GRAY);
            return null;
        }

        private static List<Point> DedupeCoords(IEnumerable<Point> coords, int tolerance)
        {
            var result = new List<Point>();
            foreach (Point point in coords)
            {
                if (result.Any(existing =>
                        Math.Abs(point.X - existing.X) <= tolerance &&
                        Math.Abs(point.Y - existing.Y) <= tolerance))
                {
                    continue;
                }

                result.Add(point);
            }

            return result;
        }

        private static int IndexFromEnd<T>(IReadOnlyList<T> list, int negativeIndex)
        {
            return negativeIndex < 0 ? list.Count + negativeIndex : negativeIndex;
        }


    }
}
