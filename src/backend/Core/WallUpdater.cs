using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using OpenCvSharp;
using Point = OpenCvSharp.Point;
using Size = OpenCvSharp.Size;

namespace CvAut
{
    /// <summary>
    /// Bộ nâng cấp tường (Wall Updater):
    /// - Quét tìm các đoạn tường trên màn hình Làng chính bằng phương pháp so khớp mẫu trong Builder menu.
    /// - Lọc trùng lặp tọa độ và sắp xếp ứng viên.
    /// - Bấm chọn tường, xác thực giao diện nâng cấp, ra quyết định chọn Vàng hoặc Dầu hồng theo ngưỡng tài nguyên và batch limit.
    /// </summary>
    internal sealed partial class WallUpdater
    {
        // Vùng ROI tìm kiếm tường trong Builder menu (port từ legacy/NX-ClashClient rois.json)
        private static readonly Rect BuilderUpgradeMenuRoi = new(646, 107, 347, 474);
        // Tọa độ điểm kiểm tra màu nền xám/trắng nhạt để xác nhận bảng nâng cấp đang mở
        private static readonly Point PanelCheckPoint = new(800, 750);
        // Nút bấm gợi ý Thợ xây ở top-center (độ phân giải 1600x900)
        private static readonly Point BuilderMenuPoint = new(738, 36);
        // Điểm an toàn ngoài rìa bản đồ để bấm giải tỏa các menu/popup
        private static readonly Point HomeMenuPoint = new(140, 606);
        // Tọa độ vuốt cuộn bảng gợi ý Thợ xây
        private static readonly Point RetrySwipeStart = new(977, 157);
        private static readonly Point RetrySwipeEnd = new(999, 432);
        // Các điểm chạm điều hướng giao diện nâng cấp tường
        private static readonly Point DismissPoint = new(1143, 209);
        private static readonly Point FixedGoldUpgradePoint = new(920, 707);
        private static readonly Point FixedElixirUpgradePoint = new(1095, 702);
        private static readonly Point AddWallPlusOneButton = new(660, 650);
        private static readonly Point RemoveWallMinusOneButton = new(330, 650);
        private static readonly Rect GoldUpgradeCostRoi = new(860, 635, 120, 33);
        private static readonly Rect ElixirUpgradeCostRoi = new(1035, 635, 120, 33);
        private const int WallUiAnimationDelayMs = 400;
        private const int RedCostPixelCountThreshold = 120;
        private static readonly Point ConfirmUpgradePoint = new(1115, 782);
        private static readonly Point ConfirmMultiPoint = new(990, 620);
        private static readonly Rect ConfirmDialogRoi = new(820, 540, 430, 300);
        // Ngưỡng so khớp mẫu để tìm tường (cần độ tin cậy cao để tránh nhận diện nhầm các vật thể khác)
        private const double WallSearchThreshold = 0.90;
        private const int SwipeDurationMs = 600;
        private readonly ADBHelper _adb;
        private readonly VisionEngine _vision;
        private readonly string _templatesPath;
        private readonly string _debugDirectory;
        private const int SupportedScreenshotWidth = 1600;
        private const int SupportedScreenshotHeight = 900;
        private const int MaxCandidateAttempts = 3;

        private int? _savedWallOffset;
        private bool _debugScreenshotsEnabled;
        private int _debugCycle;
        private int _sessionWallAttempted = 0;
        private int _sessionWallVerified = 0;
        private int _sessionWallSkipped = 0;
        private int _sessionWallUnknown = 0;

        public WallUpdater(ADBHelper adb, VisionEngine vision, string templatesPath)
        {
            _adb = adb;
            _vision = vision;
            _templatesPath = templatesPath;
            _debugDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SimpliMixi", "debug", "wall");
        }

        private static bool InterruptibleSleep(int milliseconds, CancellationToken token)
            => ThreadingUtil.InterruptibleSleep(milliseconds, token);

        /// <summary>
        /// Xử lý nâng cấp tường không phụ thuộc Wall Level.
        /// Quét Builder menu bằng 4 generic Wall templates, xác định tài nguyên phù hợp và thực hiện nâng cấp.
        /// </summary>
        public int HandleHomeResources(
            int wallGoldThreshold,
            int wallElixirThreshold,
            int wallGoldReserve,
            int wallElixirReserve,
            int batchLimit = 1,
            bool debugScreenshots = false,
            int cycle = 0,
            CancellationToken token = default)
        {
            if (token.IsCancellationRequested) return 0;
            _debugScreenshotsEnabled = debugScreenshots;
            _debugCycle = cycle;

            Console.WriteLine($"[WALL] phase=target_plan cycle={cycle} status=start gold_start={wallGoldThreshold:N0} elixir_start={wallElixirThreshold:N0} batch_limit={batchLimit}");

            string[] templateNames = GetWallTemplateNames();
            if (templateNames.Length == 0)
            {
                Console.WriteLine($"[WALL RESULT] phase=target_plan cycle={cycle} status=skip reason=wall_templates_missing");
                return 0;
            }

            using Mat? initialScreenshot = _adb.TakeScreenshot();
            if (!ValidateSupportedLayout(initialScreenshot, out string layoutReason))
            {
                Console.WriteLine($"[WALL RESULT] phase=target_plan cycle={cycle} status=skip reason={layoutReason}");
                return 0;
            }

            WallTransactionResult result = UpgradeWallBulk(
                wallGoldThreshold,
                wallElixirThreshold,
                wallGoldReserve,
                wallElixirReserve,
                batchLimit,
                token);

            if (result.VerifiedCount > 0)
            {
                _sessionWallVerified += result.VerifiedCount;
                _sessionWallAttempted += result.VerifiedCount;
            }
            else if (string.Equals(result.Reason, "outcome_unknown", StringComparison.OrdinalIgnoreCase))
            {
                _sessionWallUnknown++;
                _sessionWallAttempted++;
            }
            else
            {
                _sessionWallSkipped++;
                _sessionWallAttempted++;
            }

            LogSessionCounters(
                "handle_home_resources",
                result.Resource,
                result.Cost,
                result.CandidateMatchCount,
                result.RequestedCount,
                result.VerifiedCount,
                result.Reason);

            return result.VerifiedCount;
        }

        private sealed record WallCandidate(Point Point, double Confidence, string TemplateName);
        private sealed record WallTransactionResult(int VerifiedCount, string Reason, string Resource = "none", int CandidateMatchCount = 0, int RequestedCount = 0, int Cost = 0)
        {
            public static WallTransactionResult Skip(string reason) => new(0, reason);
            public WallTransactionResult WithCandidateMatchCount(int count) => this with { CandidateMatchCount = count };
            public WallTransactionResult WithCost(int cost) => this with { Cost = cost };
            public static WallTransactionResult Verified(string resource, int count, int cost, int candidateMatchCount, int requestedCount) =>
                new(count, "verified", Resource: resource, CandidateMatchCount: candidateMatchCount, RequestedCount: requestedCount, Cost: cost);
        }

        private WallTransactionResult UpgradeWallBulk(
            int wallGoldThreshold,
            int wallElixirThreshold,
            int wallGoldReserve,
            int wallElixirReserve,
            int batchLimit,
            CancellationToken token = default)
        {
            if (token.IsCancellationRequested) return WallTransactionResult.Skip("cancelled");
            Console.WriteLine($"[WALL] phase=attempt_upgrade status=start batch_limit={batchLimit}");
            return TryUpgradeWallBatch(wallGoldThreshold, wallElixirThreshold, wallGoldReserve, wallElixirReserve, batchLimit, token);
        }

        private WallTransactionResult TryUpgradeWallBatch(
            int wallGoldThreshold,
            int wallElixirThreshold,
            int wallGoldReserve,
            int wallElixirReserve,
            int batchLimit,
            CancellationToken token)
        {
            if (token.IsCancellationRequested) return WallTransactionResult.Skip("cancelled");
            int candidateMatchCount = 0;
            var triedCoords = new List<Point>();
            Point? validCoord = null;

            try
            {
                for (int attempt = 0; attempt < MaxCandidateAttempts; attempt++)
                {
                    if (token.IsCancellationRequested)
                    {
                        BestEffortDismiss();
                        return WallTransactionResult.Skip("cancelled");
                    }

                    List<WallCandidate> candidates = FindAllWallCandidates(token)
                        .Where(candidate => !triedCoords.Any(tried => Math.Abs(candidate.Point.Y - tried.Y) <= 20))
                        .ToList();

                    candidateMatchCount = Math.Max(candidateMatchCount, candidates.Count);
                    if (candidates.Count == 0)
                    {
                        Console.WriteLine($"[WALL RESULT] phase=attempt_upgrade cycle={_debugCycle} candidate_match_count={candidateMatchCount} verified_count=0 status=skip reason=no_candidates");
                        BestEffortDismiss();
                        return WallTransactionResult.Skip("no_candidates").WithCandidateMatchCount(candidateMatchCount);
                    }

                    WallCandidate candidate;
                    if (_savedWallOffset.HasValue && _savedWallOffset.Value >= -candidates.Count && _savedWallOffset.Value < candidates.Count)
                    {
                        candidate = candidates[IndexFromEnd(candidates, _savedWallOffset.Value)];
                    }
                    else
                    {
                        candidate = candidates[candidates.Count - 1];
                    }
                    triedCoords.Add(candidate.Point);

                    Console.WriteLine($"[WALL] phase=select_candidate cycle={_debugCycle} candidate_match_count={candidates.Count} attempt={attempt + 1} x={candidate.Point.X} y={candidate.Point.Y} conf={candidate.Confidence:F3} template=\"{candidate.TemplateName}\" status=start");
                    _adb.Tap(candidate.Point.X, candidate.Point.Y);
                    if (InterruptibleSleep(1000, token)) return WallTransactionResult.Skip("cancelled");
                    SaveDebugScreenshot("candidate_selected");

                    // Tắt bảng Thợ xây để hiện panel nâng cấp
                    _adb.Tap(BuilderMenuPoint.X, BuilderMenuPoint.Y);
                    if (InterruptibleSleep(500, token)) return WallTransactionResult.Skip("cancelled");

                    // Validate xem panel nâng tường có mở không
                    if (ValidateWallPanelOpen(out bool goldAvailable, out bool elixirAvailable))
                    {
                        validCoord = candidate.Point;
                        _savedWallOffset ??= -1 - attempt;
                        break;
                    }

                    _adb.Tap(DismissPoint.X, DismissPoint.Y);
                    if (InterruptibleSleep(500, token)) return WallTransactionResult.Skip("cancelled");
                    _savedWallOffset = null;
                }

                if (!validCoord.HasValue)
                {
                    Console.WriteLine($"[WALL RESULT] phase=attempt_upgrade cycle={_debugCycle} candidate_match_count={candidateMatchCount} verified_count=0 status=skip reason=unvalidated");
                    return WallTransactionResult.Skip("unvalidated").WithCandidateMatchCount(candidateMatchCount);
                }

                // Đánh giá resource eligibility & affordability sau khi mở panel
                using Mat? currentScreenshot = _adb.TakeScreenshot();
                if (currentScreenshot == null || currentScreenshot.Empty())
                {
                    BestEffortDismiss();
                    return WallTransactionResult.Skip("screenshot_failed").WithCandidateMatchCount(candidateMatchCount);
                }

                bool goldRed = IsUpgradeCostRed(currentScreenshot, "gold", out _, out _);
                bool elixirRed = IsUpgradeCostRed(currentScreenshot, "elixir", out _, out _);

                bool goldAvailableBtn = IsResourceUpgradeButtonAvailable(currentScreenshot, "gold") && !goldRed;
                bool elixirAvailableBtn = IsResourceUpgradeButtonAvailable(currentScreenshot, "elixir") && !elixirRed;

                string selectedResource = "none";
                if (goldAvailableBtn && elixirAvailableBtn)
                {
                    selectedResource = "gold"; // Default preference to Gold when both available
                }
                else if (goldAvailableBtn)
                {
                    selectedResource = "gold";
                }
                else if (elixirAvailableBtn)
                {
                    selectedResource = "elixir";
                }

                if (selectedResource == "none")
                {
                    Console.WriteLine($"[WALL RESULT] phase=attempt_upgrade cycle={_debugCycle} candidate_match_count={candidateMatchCount} status=skip reason=resources_below_threshold_or_red");
                    BestEffortDismiss();
                    return WallTransactionResult.Skip("resources_below_threshold_or_red").WithCandidateMatchCount(candidateMatchCount);
                }

                int maxBatch = Math.Max(1, batchLimit);
                int actualSelectedCount = AddWallsSafely(selectedResource, maxBatch, token);
                if (actualSelectedCount <= 0)
                {
                    BestEffortDismiss();
                    return WallTransactionResult.Skip("insufficient_resource_for_cost").WithCandidateMatchCount(candidateMatchCount);
                }

                SaveDebugScreenshot("add_wall_done");

                Point upgradePoint = selectedResource.Equals("gold", StringComparison.OrdinalIgnoreCase)
                    ? FixedGoldUpgradePoint
                    : FixedElixirUpgradePoint;

                _adb.Tap(upgradePoint.X, upgradePoint.Y);
                if (InterruptibleSleep(1000, token)) return WallTransactionResult.Skip("cancelled");

                if (!IsConfirmDialogOpen())
                {
                    Console.WriteLine($"[WALL RESULT] phase=confirm_open cycle={_debugCycle} resource={selectedResource} candidate_match_count={candidateMatchCount} requested_count={actualSelectedCount} verified_count=0 status=skip reason=confirm_dialog_not_verified");
                    BestEffortDismiss();
                    return WallTransactionResult.Skip("confirm_dialog_not_verified").WithCandidateMatchCount(candidateMatchCount);
                }

                SaveDebugScreenshot("confirm_open");

                Point confirmPoint = actualSelectedCount > 1 ? ConfirmMultiPoint : ConfirmUpgradePoint;
                _adb.Tap(confirmPoint.X, confirmPoint.Y);

                if (InterruptibleSleep(1500, token))
                {
                    // Token cancelled post-confirm: verify outcome before deciding
                    if (IsConfirmDialogClosed())
                    {
                        return WallTransactionResult.Verified(selectedResource, actualSelectedCount, 0, candidateMatchCount, actualSelectedCount);
                    }
                    return new WallTransactionResult(0, "outcome_unknown", Resource: selectedResource, CandidateMatchCount: candidateMatchCount, RequestedCount: actualSelectedCount);
                }

                if (!IsConfirmDialogClosed())
                {
                    Console.WriteLine($"[WALL RESULT] phase=confirm_verify cycle={_debugCycle} resource={selectedResource} status=unknown reason=dialog_still_open");
                    BestEffortDismiss();
                    return new WallTransactionResult(0, "outcome_unknown", Resource: selectedResource, CandidateMatchCount: candidateMatchCount, RequestedCount: actualSelectedCount);
                }

                BestEffortDismiss();
                Console.WriteLine($"[WALL RESULT] phase=attempt_upgrade cycle={_debugCycle} resource={selectedResource} candidate_match_count={candidateMatchCount} requested_count={actualSelectedCount} verified_count={actualSelectedCount} status=upgraded reason=confirmed");
                return WallTransactionResult.Verified(selectedResource, actualSelectedCount, 0, candidateMatchCount, actualSelectedCount);
            }
            finally
            {
                BestEffortDismiss();
            }
        }

        private int AddWallsSafely(string resource, int batchLimit, CancellationToken token)
        {
            int selectedCount = 1;
            if (IsUpgradeCostRed(resource, out double initialRatio, out int initialRedPixels))
            {
                Console.WriteLine($"[WALL] phase=add_wall resource={resource} status=skip reason=initial_cost_red selected_count=0 red_ratio={initialRatio:F3} red_pixels={initialRedPixels}");
                return 0;
            }

            int addMoreTaps = Math.Max(0, batchLimit - 1);
            for (int i = 0; i < addMoreTaps; i++)
            {
                if (token.IsCancellationRequested) break;
                _adb.Tap(AddWallPlusOneButton.X, AddWallPlusOneButton.Y);
                if (InterruptibleSleep(WallUiAnimationDelayMs, token)) break;

                if (!IsUpgradeCostRed(resource, out double redRatio, out int redPixels))
                {
                    selectedCount++;
                    Console.WriteLine($"[WALL] phase=add_wall resource={resource} status=ok reason=cost_available selected_count={selectedCount} red_ratio={redRatio:F3} red_pixels={redPixels}");
                    continue;
                }

                // If cost turned red, revert last tap and stop tapping
                _adb.Tap(RemoveWallMinusOneButton.X, RemoveWallMinusOneButton.Y);
                if (InterruptibleSleep(WallUiAnimationDelayMs, token)) break;

                bool stillRed = IsUpgradeCostRed(resource, out double afterRemoveRatio, out int afterRemoveRedPixels);
                Console.WriteLine($"[WALL] phase=add_wall resource={resource} status={(stillRed ? "fail" : "ok")} reason={(stillRed ? "cost_still_red_after_remove" : "red_cost_boundary_found")} selected_count={selectedCount} red_ratio={afterRemoveRatio:F3} red_pixels={afterRemoveRedPixels}");
                return stillRed ? 0 : selectedCount;
            }

            return selectedCount;
        }

        private bool IsUpgradeCostRed(string resource, out double redRatio, out int redPixels)
        {
            redRatio = 0;
            redPixels = 0;
            using Mat? screenshot = _adb.TakeScreenshot();
            if (screenshot == null || screenshot.Empty())
            {
                Console.WriteLine($"[WALL] phase=color_check resource={resource} status=fail reason=screenshot_failed");
                return true;
            }
            bool red = IsUpgradeCostRed(screenshot, resource, out redRatio, out redPixels);
            Console.WriteLine($"[WALL] phase=color_check resource={resource} status=ok reason={(red ? "cost_red" : "cost_available")} red_ratio={redRatio:F3} red_pixels={redPixels}");
            return red;
        }

        internal static bool IsUpgradeCostRed(Mat screenshot, string resource, out double redRatio, out int redPixels)
        {
            redRatio = 0;
            redPixels = 0;
            Rect sourceRoi = resource.Equals("gold", StringComparison.OrdinalIgnoreCase)
                ? GoldUpgradeCostRoi
                : ElixirUpgradeCostRoi;
            Rect roi = ImageUtils.ClampRect(sourceRoi, screenshot.Width, screenshot.Height);
            if (roi.Width <= 0 || roi.Height <= 0)
            {
                return true;
            }
            using Mat cost = new Mat(screenshot, roi);
            for (int y = 0; y < cost.Rows; y++)
            {
                for (int x = 0; x < cost.Cols; x++)
                {
                    Vec3b pixel = cost.At<Vec3b>(y, x);
                    byte b = pixel.Item0;
                    byte g = pixel.Item1;
                    byte r = pixel.Item2;
                    bool isRed = r > 200 && g < 160 && b < 160 && (r - g) > 50 && (r - b) > 50;
                    if (isRed)
                    {
                        redPixels++;
                    }
                }
            }
            redRatio = redPixels / (double)(roi.Width * roi.Height);
            return redPixels >= RedCostPixelCountThreshold;
        }

        private List<WallCandidate> FindAllWallCandidates(CancellationToken token = default)
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
                    _adb.Swipe(RetrySwipeEnd.X, RetrySwipeEnd.Y, RetrySwipeStart.X, RetrySwipeStart.Y, SwipeDurationMs);
                    if (InterruptibleSleep(800, token)) return new List<WallCandidate>();
                }

                using Mat? screenshot = _adb.TakeScreenshot();
                if (screenshot == null || screenshot.Empty())
                {
                    Console.WriteLine("[WALL WARN] Screenshot failed while searching walls.");
                    continue;
                }

                Rect roi = ImageUtils.ClampRect(BuilderUpgradeMenuRoi, screenshot.Width, screenshot.Height);
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
                    merged.AddRange(MatchWallTemplateInRoi(roiGray, templateName, BuilderUpgradeMenuRoi));
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

        private bool PrepareWallSearch(CancellationToken token = default)
        {
            Console.WriteLine("[WALL] phase=preflight status=start reason=open_builder_menu");
            if (token.IsCancellationRequested) return false;

            BestEffortDismiss();
            if (InterruptibleSleep(300, token)) return false;

            _adb.Tap(BuilderMenuPoint.X, BuilderMenuPoint.Y);
            if (InterruptibleSleep(1000, token)) return false;

            if (!IsBuilderMenuOpen())
            {
                Console.WriteLine("[WALL] phase=preflight status=fail reason=builder_menu_not_visible");
                return false;
            }

            for (int i = 0; i < 3; i++)
            {
                if (token.IsCancellationRequested) return false;
                _adb.Swipe(RetrySwipeStart.X, RetrySwipeStart.Y, RetrySwipeEnd.X, RetrySwipeEnd.Y, SwipeDurationMs);
                if (InterruptibleSleep(250, token)) return false;
            }

            bool menuOpen = IsBuilderMenuOpen();
            Console.WriteLine($"[WALL] phase=preflight status={(menuOpen ? "ok" : "fail")} reason={(menuOpen ? "builder_menu_visible" : "builder_menu_not_visible")}");
            return menuOpen;
        }

        private void BestEffortDismiss()
        {
            try
            {
                _adb.Tap(HomeMenuPoint.X, HomeMenuPoint.Y);
                Thread.Sleep(150);
                _adb.Tap(DismissPoint.X, DismissPoint.Y);
            }
            catch { }
        }

        private void SafeDismiss(CancellationToken token)
        {
            if (token.IsCancellationRequested)
            {
                BestEffortDismiss();
                return;
            }
            _adb.Tap(HomeMenuPoint.X, HomeMenuPoint.Y);
            if (InterruptibleSleep(150, token)) return;
            _adb.Tap(DismissPoint.X, DismissPoint.Y);
        }

        private bool IsBuilderMenuOpen()
        {
            using Mat? screenshot = _adb.TakeScreenshot();
            if (screenshot == null || screenshot.Empty())
            {
                Console.WriteLine("[WALL RESULT] phase=preflight status=fail reason=screenshot_failed");
                return false;
            }
            Rect safeRoi = ImageUtils.ClampRect(BuilderUpgradeMenuRoi, screenshot.Width, screenshot.Height);
            if (safeRoi.Width <= 0 || safeRoi.Height <= 0)
            {
                Console.WriteLine($"[WALL RESULT] phase=preflight status=fail reason=empty_builder_roi width={screenshot.Width} height={screenshot.Height}");
                return false;
            }
            using Mat menu = new Mat(screenshot, safeRoi);
            using Mat gray = new Mat();
            using Mat dark = new Mat();
            Cv2.CvtColor(menu, gray, ColorConversionCodes.BGR2GRAY);
            Cv2.Threshold(gray, dark, 80, 255, ThresholdTypes.BinaryInv);
            double darkRatio = Cv2.CountNonZero(dark) / (double)(dark.Rows * dark.Cols);
            bool open = darkRatio >= 0.55;
            Console.WriteLine($"[WALL] phase=preflight_check status={(open ? "ok" : "fail")} dark_ratio={darkRatio:F2} reason={(open ? "builder_menu_panel_visible" : "builder_menu_panel_missing")}");
            return open;
        }

        private string[] GetWallTemplateNames()
        {
            return new[]
            {
                @"walls\wall.png",
                @"walls\wall_2.png",
                @"walls\wall_3.png",
                @"walls\wall_4.png"
            }.Where(name => TemplateAssetLoader.Exists(_templatesPath, name)).ToArray();
        }

        private IEnumerable<WallCandidate> MatchWallTemplateInRoi(Mat grayRoi, string templateName, Rect sourceRoi, double threshold = WallSearchThreshold)
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

        private bool ValidateWallPanelOpen(out bool goldAvailable, out bool elixirAvailable)
        {
            goldAvailable = false;
            elixirAvailable = false;

            using Mat? screenshot = _adb.TakeScreenshot();
            if (screenshot == null || screenshot.Empty())
            {
                Console.WriteLine("[WALL] phase=validate_tap status=fail reason=screenshot_failed");
                return false;
            }

            int width = screenshot.Width;
            int height = screenshot.Height;
            int px = Math.Clamp(PanelCheckPoint.X, 0, width - 1);
            int py = Math.Clamp(PanelCheckPoint.Y, 0, height - 1);
            Vec3b pixel = screenshot.At<Vec3b>(py, px);
            bool whitePanel = pixel.Item0 >= 180 && pixel.Item1 >= 180 && pixel.Item2 >= 180;

            goldAvailable = IsResourceUpgradeButtonAvailable(screenshot, "gold");
            elixirAvailable = IsResourceUpgradeButtonAvailable(screenshot, "elixir");

            bool panelOpen = whitePanel || goldAvailable || elixirAvailable;
            if (!panelOpen)
            {
                Console.WriteLine("[WALL] phase=validate_tap status=fail reason=panel_not_open");
                return false;
            }

            Console.WriteLine($"[WALL] phase=validate_tap status=ok reason=panel_open gold={goldAvailable} elixir={elixirAvailable}");
            return true;
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

        private bool ValidateSupportedLayout(Mat? screenshot, out string reason)
        {
            if (screenshot == null || screenshot.Empty())
            {
                reason = "screenshot_failed";
                Console.WriteLine($"[WALL RESULT] phase=layout cycle={_debugCycle} status=skip reason={reason}");
                return false;
            }
            if (screenshot.Width != SupportedScreenshotWidth || screenshot.Height != SupportedScreenshotHeight)
            {
                reason = "unsupported_screen_layout";
                Console.WriteLine($"[WALL RESULT] phase=layout cycle={_debugCycle} status=skip reason={reason} width={screenshot.Width} height={screenshot.Height} supported_width={SupportedScreenshotWidth} supported_height={SupportedScreenshotHeight}");
                return false;
            }
            reason = "supported_screen_layout";
            Console.WriteLine($"[WALL] phase=layout cycle={_debugCycle} status=ok reason={reason} width={screenshot.Width} height={screenshot.Height}");
            return true;
        }

        private void SaveDebugScreenshot(string phase)
        {
            if (!_debugScreenshotsEnabled) return;
            using Mat? screenshot = _adb.TakeScreenshot();
            if (screenshot == null || screenshot.Empty()) return;
            SaveDebugScreenshot(screenshot, phase);
        }

        private void SaveDebugScreenshot(Mat screenshot, string phase)
        {
            if (!_debugScreenshotsEnabled || screenshot.Empty()) return;
            try
            {
                Directory.CreateDirectory(_debugDirectory);
                string safePhase = string.Concat(phase.Select(ch => char.IsLetterOrDigit(ch) || ch == '_' || ch == '-' ? ch : '_'));
                string fileName = $"wall_cycle_{_debugCycle:D6}_{DateTimeOffset.UtcNow:yyyyMMdd_HHmmssfff}_{safePhase}.png";
                Cv2.ImWrite(Path.Combine(_debugDirectory, fileName), screenshot);
                Console.WriteLine($"[WALL DEBUG] phase={safePhase} cycle={_debugCycle} status=saved file=\"{fileName}\"");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[WALL DEBUG] phase={phase} cycle={_debugCycle} status=fail reason=\"{ex.Message}\"");
            }
        }

        private void LogSessionCounters(string phase, string resource, int cost, int candidateMatchCount, int requestedCount, int verifiedCount, string reason)
        {
            Console.WriteLine($"[WALL SESSION] phase={phase} cycle={_debugCycle} resource={resource} cost={cost:N0} candidate_match_count={candidateMatchCount} requested_count={requestedCount} verified_count={verifiedCount} reason={reason} wall_attempted={_sessionWallAttempted} wall_verified={_sessionWallVerified} wall_skipped={_sessionWallSkipped} wall_unknown={_sessionWallUnknown}");
        }

        private bool IsResourceUpgradeButtonAvailable(Mat screenshot, string resource)
        {
            Point point = resource.Equals("gold", StringComparison.OrdinalIgnoreCase)
                ? FixedGoldUpgradePoint
                : FixedElixirUpgradePoint;
            int halfSize = 16;
            Rect roi = ImageUtils.ClampRect(new Rect(point.X - halfSize, point.Y - halfSize, halfSize * 2, halfSize * 2), screenshot.Width, screenshot.Height);
            if (roi.Width <= 0 || roi.Height <= 0)
            {
                return false;
            }
            using Mat button = new Mat(screenshot, roi);
            Scalar mean = Cv2.Mean(button);
            double brightness = (mean.Val0 + mean.Val1 + mean.Val2) / 3.0;
            return brightness >= 45;
        }

        private bool IsConfirmDialogOpen()
        {
            using Mat? screenshot = _adb.TakeScreenshot();
            if (screenshot == null || screenshot.Empty()) return false;
            Rect roi = ImageUtils.ClampRect(ConfirmDialogRoi, screenshot.Width, screenshot.Height);
            if (roi.Width <= 0 || roi.Height <= 0) return false;

            using Mat dialog = new Mat(screenshot, roi);
            Scalar mean = Cv2.Mean(dialog);
            double brightness = (mean.Val0 + mean.Val1 + mean.Val2) / 3.0;
            bool open = brightness >= 70;
            Console.WriteLine($"[WALL] phase=confirm_open status={(open ? "ok" : "fail")} brightness={brightness:F1} reason={(open ? "dialog_visible" : "dialog_not_visible")}");
            return open;
        }

        private bool IsConfirmDialogClosed()
        {
            using Mat? screenshot = _adb.TakeScreenshot();
            if (screenshot == null || screenshot.Empty()) return true;
            Rect roi = ImageUtils.ClampRect(ConfirmDialogRoi, screenshot.Width, screenshot.Height);
            if (roi.Width <= 0 || roi.Height <= 0) return true;

            using Mat dialog = new Mat(screenshot, roi);
            Scalar mean = Cv2.Mean(dialog);
            double brightness = (mean.Val0 + mean.Val1 + mean.Val2) / 3.0;
            return brightness < 60;
        }

        private static int IndexFromEnd<T>(IReadOnlyList<T> list, int negativeIndex)
        {
            return negativeIndex < 0 ? list.Count + negativeIndex : negativeIndex;
        }

        public void ResetSavedOffset()
        {
            _savedWallOffset = null;
        }
    }
}
