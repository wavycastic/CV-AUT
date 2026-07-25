using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using OpenCvSharp;
using Point = OpenCvSharp.Point;
using Size = OpenCvSharp.Size;

namespace CvAut
{
    /// <summary>Một ứng viên tường tìm được trong Builder menu.</summary>
    internal sealed record WallCandidate(Point Point, double Confidence, string TemplateName);

    /// <summary>
    /// Quét tìm các đoạn tường trong Builder menu bằng so khớp mẫu, lọc trùng lặp và sắp xếp ứng viên.
    /// </summary>
    internal sealed class WallCandidateScanner
    {
        private readonly IADBHelper _adb;
        private readonly string _templatesPath;
        private readonly WallPanelInspector _inspector;
        private readonly WallMenuNavigator _navigator;

        public WallCandidateScanner(IADBHelper adb, string templatesPath, WallPanelInspector inspector, WallMenuNavigator navigator)
        {
            _adb = adb;
            _templatesPath = templatesPath;
            _inspector = inspector;
            _navigator = navigator;
        }

        /// <summary>Danh sách 4 generic Wall template thực sự tồn tại trong thư mục Templates.</summary>
        public string[] GetWallTemplateNames()
        {
            return new[]
            {
                @"walls\wall.png",
                @"walls\wall_2.png",
                @"walls\wall_3.png",
                @"walls\wall_4.png"
            }.Where(name => TemplateAssetLoader.Exists(_templatesPath, name)).ToArray();
        }

        /// <summary>Mở Builder menu rồi quét tối đa 7 lần (có cuộn lại) cho tới khi tìm được ứng viên.</summary>
        public List<WallCandidate> FindAllWallCandidates(CancellationToken token = default)
        {
            if (token.IsCancellationRequested) return new List<WallCandidate>();
            if (!PrepareWallSearch(token))
            {
                return new List<WallCandidate>();
            }

            string[] templateNames = GetWallTemplateNames();
            if (templateNames.Length == 0)
            {
                Console.WriteLine("[WALL WARN] No wall templates found in Templates directory.");
                return new List<WallCandidate>();
            }

            Console.WriteLine($"[WALL] phase=search_templates count={templateNames.Length} status=ok reason=loaded");

            for (int attempt = 0; attempt < 7; attempt++)
            {
                if (token.IsCancellationRequested) return new List<WallCandidate>();
                if (attempt > 0)
                {
                    _adb.Swipe(WallUiLayout.RetrySwipeEnd.X, WallUiLayout.RetrySwipeEnd.Y, WallUiLayout.RetrySwipeStart.X, WallUiLayout.RetrySwipeStart.Y, WallUiLayout.SwipeDurationMs);
                    if (ThreadingUtil.InterruptibleSleep(800, token)) return new List<WallCandidate>();
                }

                using Mat? screenshot = _adb.TakeScreenshot();
                if (screenshot == null || screenshot.Empty())
                {
                    Console.WriteLine("[WALL WARN] Screenshot failed while searching walls.");
                    continue;
                }

                Rect roi = ImageUtils.ClampRect(WallUiLayout.BuilderUpgradeMenuRoi, screenshot.Width, screenshot.Height);
                if (roi.Width <= 0 || roi.Height <= 0)
                {
                    Console.WriteLine("[WALL WARN] Builder Menu ROI is empty; check screenshot size.");
                    return new List<WallCandidate>();
                }

                using Mat roiBgr = new Mat(screenshot, roi);
                using Mat roiGray = new Mat();
                Cv2.CvtColor(roiBgr, roiGray, ColorConversionCodes.BGR2GRAY);

                var merged = new List<WallCandidate>();
                foreach (string templateName in templateNames)
                {
                    merged.AddRange(MatchWallTemplateInRoi(roiGray, templateName, WallUiLayout.BuilderUpgradeMenuRoi));
                }

                List<WallCandidate> candidates = DedupeCandidates(merged, 10)
                    .OrderBy(candidate => candidate.Point.Y)
                    .ThenBy(candidate => candidate.Point.X)
                    .ToList();

                if (candidates.Count > 0)
                {
                    Console.WriteLine($"[WALL] phase=search_candidates count={candidates.Count} status=ok reason=matched");
                    return candidates;
                }
            }

            return new List<WallCandidate>();
        }

        /// <summary>Quét tường trên một ảnh có sẵn, không chạm vào giao diện.</summary>
        public List<Point> ScanWallLocations(Mat screenshot)
        {
            var locations = new List<Point>();
            if (screenshot == null || screenshot.Empty()) return locations;

            string[] templates = GetWallTemplateNames();
            if (templates.Length == 0) return locations;

            Rect roi = ImageUtils.ClampRect(WallUiLayout.BuilderUpgradeMenuRoi, screenshot.Width, screenshot.Height);
            if (roi.Width <= 0 || roi.Height <= 0) return locations;

            using Mat roiBgr = new Mat(screenshot, roi);
            using Mat roiGray = new Mat();
            Cv2.CvtColor(roiBgr, roiGray, ColorConversionCodes.BGR2GRAY);

            var merged = new List<WallCandidate>();
            foreach (string t in templates)
            {
                merged.AddRange(MatchWallTemplateInRoi(roiGray, t, WallUiLayout.BuilderUpgradeMenuRoi));
            }

            locations.AddRange(DedupeCandidates(merged, 10).Select(c => c.Point));
            return locations;
        }

        private bool PrepareWallSearch(CancellationToken token = default)
        {
            Console.WriteLine("[WALL] phase=preflight status=start reason=open_builder_menu");
            if (token.IsCancellationRequested) return false;

            _navigator.BestEffortDismiss();
            if (ThreadingUtil.InterruptibleSleep(300, token)) return false;

            _adb.Tap(WallUiLayout.BuilderMenuPoint.X, WallUiLayout.BuilderMenuPoint.Y);
            if (ThreadingUtil.InterruptibleSleep(1000, token)) return false;

            if (!_inspector.IsBuilderMenuOpen())
            {
                Console.WriteLine("[WALL] phase=preflight status=fail reason=builder_menu_not_visible");
                return false;
            }

            for (int i = 0; i < 3; i++)
            {
                if (token.IsCancellationRequested) return false;
                _adb.Swipe(WallUiLayout.RetrySwipeStart.X, WallUiLayout.RetrySwipeStart.Y, WallUiLayout.RetrySwipeEnd.X, WallUiLayout.RetrySwipeEnd.Y, WallUiLayout.SwipeDurationMs);
                if (ThreadingUtil.InterruptibleSleep(250, token)) return false;
            }

            bool menuOpen = _inspector.IsBuilderMenuOpen();
            Console.WriteLine($"[WALL] phase=preflight status={(menuOpen ? "ok" : "fail")} reason={(menuOpen ? "builder_menu_visible" : "builder_menu_not_visible")}");
            return menuOpen;
        }

        private IEnumerable<WallCandidate> MatchWallTemplateInRoi(Mat grayRoi, string templateName, Rect sourceRoi, double threshold = WallUiLayout.WallSearchThreshold)
        {
            using Mat raw = TemplateAssetLoader.Load(_templatesPath, templateName, ImreadModes.Unchanged);
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
                    if (value >= threshold && Math.Abs(value - dilated.At<float>(y, x)) < 0.0001)
                    {
                        yield return new WallCandidate(
                            new Point(
                                sourceRoi.X + x + templateGray.Width / 2,
                                sourceRoi.Y + y + templateGray.Height / 2),
                            value,
                            templateName);
                    }
                }
            }
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

        private static List<WallCandidate> DedupeCandidates(IEnumerable<WallCandidate> candidates, int tolerance)
        {
            var result = new List<WallCandidate>();
            foreach (WallCandidate candidate in candidates.OrderByDescending(candidate => candidate.Confidence))
            {
                if (result.Any(existing =>
                        Math.Abs(candidate.Point.X - existing.Point.X) <= tolerance &&
                        Math.Abs(candidate.Point.Y - existing.Point.Y) <= tolerance))
                {
                    continue;
                }
                result.Add(candidate);
            }
            return result;
        }
    }
}
