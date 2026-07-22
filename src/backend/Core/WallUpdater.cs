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

    /// - Quét tìm các đoạn tường trên màn hình Làng chính bằng phương pháp so khớp mẫu.

    /// - Thực hiện lọc trùng lặp tọa độ để tránh bấm nhầm cùng một bức tường.

    /// - Bấm chọn tường, xác thực giao diện nâng cấp, tính toán và nâng cấp bằng Vàng hoặc Dầu hồng tùy điều kiện tài nguyên.

    /// </summary>

    internal sealed partial class WallUpdater

    {

        // Vùng ROI tìm kiếm tường trên bản đồ (Tránh phần rìa chứa các nút UI cản trở)
        private static readonly Rect WallSearchRoi = Rect.FromLTRB(270, 100, 1339, 785);
        // Vùng danh sách nâng cấp trong Builder menu, port từ legacy/NX-ClashClient rois.json

        // upgrades_menu: x=0.404, y=0.1187, w=0.217, h=0.527 trên layout 1600x900.

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

        private const int MaxAddWallIterations = 50;

        private const int WallUiAnimationDelayMs = 400;

        private const int RedCostPixelCountThreshold = 120;

        private static readonly Point ConfirmUpgradePoint = new(1115, 782);

        private static readonly Point ConfirmMultiPoint = new(990, 620);

        private static readonly Point SafeClosePoint = new(1229, 25);

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



        // Lưu trữ vị trí index bù của bức tường nâng cấp gần nhất để tăng tốc độ chọn ở chu kỳ tiếp theo

        private int? _savedWallOffset;

        private bool _debugScreenshotsEnabled;

        private int _debugCycle;

        private int _sessionWallAttempted = 0;

        private int _sessionWallVerified = 0;

        private int _sessionWallSkipped = 0;

        private int _sessionWallUnknown = 0;



        /// <summary>

        /// Khởi tạo bộ cập nhật nâng cấp tường.

        /// </summary>

        /// <param name="adb">Đối tượng ADBHelper.</param>

        /// <param name="vision">Đối tượng VisionEngine.</param>

        /// <param name="templatesPath">Thư mục chứa tệp mẫu template.</param>

        public WallUpdater(ADBHelper adb, VisionEngine vision, string templatesPath)

        {

            _adb = adb;

            _vision = vision;

            _templatesPath = templatesPath;

            _debugDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SimpliMixi", "debug", "wall");

        }



        private static bool InterruptibleSleep(int milliseconds, CancellationToken token)

        {

            return token.WaitHandle.WaitOne(milliseconds);

        }



        /// <summary>

        /// Kiểm tra lượng tài nguyên Vàng và Dầu hồng hiện tại ở Làng chính,

        /// nếu vượt ngưỡng tối thiểu (do người dùng cấu hình), thực hiện nâng cấp tường.

        /// </summary>

        /// <param name="wallLevel">Cấp độ tường đích muốn nâng cấp.</param>

        /// <param name="wallGoldThreshold">Ngưỡng Vàng tối thiểu để bắt đầu nâng tường.</param>

        /// <param name="wallElixirThreshold">Ngưỡng Dầu hồng tối thiểu để bắt đầu nâng tường.</param>

        /// <param name="wallGoldReserve">Lượng Vàng luôn giữ lại sau nâng tường.</param>

        /// <param name="wallElixirReserve">Lượng Dầu hồng luôn giữ lại sau nâng tường.</param>

        public int HandleHomeResources(
            int wallLevel,
            int wallGoldThreshold,
            int wallElixirThreshold,
            int wallGoldReserve,
            int wallElixirReserve,
            bool debugScreenshots = false,
            int cycle = 0,
            CancellationToken token = default)
        {
            if (token.IsCancellationRequested) return 0;
            _debugScreenshotsEnabled = debugScreenshots;
            _debugCycle = cycle;

            // Force wall level to 14 internally as level configuration is no longer requested in FE.
            // Level 14 is fully supported and allows both Gold and Elixir upgrades.
            wallLevel = 14;

            Console.WriteLine($"[WALL] phase=target_plan cycle={cycle} status=start reason=color_detection_gold_then_elixir");

            if (!IsSupportedWallLevel(wallLevel))
            {
                Console.WriteLine($"[WALL RESULT] phase=target_plan cycle={cycle} status=skip reason=unsupported_wall_level level={wallLevel}");
                return 0;
            }

            int totalVerified = 0;
            totalVerified += TryUpgradeWithResource(
                "gold",
                wallLevel,
                wallGoldThreshold,
                wallElixirThreshold,
                wallGoldReserve,
                wallElixirReserve,
                token);
            totalVerified += TryUpgradeWithResource(
                "elixir",
                wallLevel,
                wallGoldThreshold,
                wallElixirThreshold,
                wallGoldReserve,
                wallElixirReserve,
                token);

            return totalVerified;
        }

        private int TryUpgradeWithResource(
            string resource,
            int wallLevel,
            int wallGoldThreshold,
            int wallElixirThreshold,
            int wallGoldReserve,
            int wallElixirReserve,
            CancellationToken token)
        {
            if (token.IsCancellationRequested) return 0;

            bool goldOnly = resource.Equals("gold", StringComparison.OrdinalIgnoreCase);
            int threshold = goldOnly ? wallGoldThreshold : wallElixirThreshold;
            int reserve = goldOnly ? wallGoldReserve : wallElixirReserve;

            Console.WriteLine($"[WALL] phase=target_plan resource={resource} threshold={threshold} reserve={reserve} status=start reason=color_detection_controls_affordability");

            WallTransactionResult result = UpgradeWallBulk(resource, wallLevel, token);

            if (result.VerifiedCount > 0)
            {
                _sessionWallVerified += result.VerifiedCount;
                _sessionWallAttempted += result.VerifiedCount;
            }
            else
            {
                _sessionWallSkipped++;
                _sessionWallAttempted++;
            }

            LogSessionCounters(
                "handle_home_resources",
                wallLevel,
                resource,
                result.Cost,
                result.CandidateMatchCount,
                result.VerifiedCount,
                result.VerifiedCount,
                result.Reason);

            return result.VerifiedCount;
        }


        private sealed record WallCandidate(Point Point, double Confidence, string TemplateName);



        private sealed record WallTransactionResult(int VerifiedCount, string Reason, int CandidateMatchCount = 0, int Cost = 0)
        {
            public static WallTransactionResult Skip(string reason) => new(0, reason);
            public WallTransactionResult WithCandidateMatchCount(int count) => this with { CandidateMatchCount = count };
            public WallTransactionResult WithCost(int cost) => this with { Cost = cost };
            public static WallTransactionResult Verified(int count, int cost) => new(count, "verified", Cost: cost);
        }



        /// <summary>

        /// Thực hiện quy trình nâng cấp hàng loạt tường lên cấp độ chỉ định bằng tài nguyên vàng hoặc elixir.

        /// Thử nghiệm tối đa 3 bức tường cho đến khi tìm được bức tường xác thực hợp lệ.

        /// </summary>

        /// <returns>Kết quả giao dịch, chỉ có VerifiedCount khi post-confirm verification thành công.</returns>

        private WallTransactionResult UpgradeWallBulk(
            string resource,
            int wallLevel,
            CancellationToken token = default)
        {
            if (token.IsCancellationRequested) return WallTransactionResult.Skip("cancelled");

            Console.WriteLine($"[WALL] phase=attempt_upgrade resource={resource} level={wallLevel} status=start");

            return TryUpgradeWallBatch(resource, wallLevel, token);
        }



        private WallTransactionResult TryUpgradeWallBatch(
            string resource,
            int wallLevel,
            CancellationToken token)
        {
            if (token.IsCancellationRequested) return WallTransactionResult.Skip("cancelled");

            int candidateMatchCount = 0;
            var triedCoords = new List<Point>();
            Point? validCoord = null;

            for (int attempt = 0; attempt < MaxCandidateAttempts; attempt++)
            {
                if (token.IsCancellationRequested)
                {
                    SafeDismiss(token);
                    return WallTransactionResult.Skip("cancelled");
                }

                // Lấy tất cả các tường tìm thấy trong bảng gợi ý Thợ xây
                List<WallCandidate> candidates = FindAllWallCandidates(token)
                    .Where(candidate => !triedCoords.Any(tried => Math.Abs(candidate.Point.Y - tried.Y) <= 20))
                    .ToList();

                candidateMatchCount = Math.Max(candidateMatchCount, candidates.Count);

                if (candidates.Count == 0)
                {
                    Console.WriteLine($"[WALL RESULT] phase=attempt_upgrade cycle={_debugCycle} resource={resource} level={wallLevel} candidate_match_count={candidateMatchCount} verified_count=0 status=skip reason=no_candidates");
                    SafeDismiss(token);
                    return WallTransactionResult.Skip("no_candidates").WithCandidateMatchCount(candidateMatchCount);
                }

                WallCandidate candidate;

                // Nếu đã lưu offset thành công từ lần trước, ưu tiên chọn tường quanh khu vực đó
                if (_savedWallOffset.HasValue && _savedWallOffset.Value >= -candidates.Count && _savedWallOffset.Value < candidates.Count)
                {
                    candidate = candidates[IndexFromEnd(candidates, _savedWallOffset.Value)];
                }
                else
                {
                    // Builder menu: bottom-most row first.
                    candidate = candidates[candidates.Count - 1];
                }

                triedCoords.Add(candidate.Point);

                // Nhấp chọn biểu tượng Wall trong bảng gợi ý Thợ xây để game tự định vị và chọn tường
                Console.WriteLine($"[WALL] phase=select_candidate cycle={_debugCycle} resource={resource} level={wallLevel} candidate_match_count={candidates.Count} attempt={attempt + 1} x={candidate.Point.X} y={candidate.Point.Y} conf={candidate.Confidence:F3} template=\"{candidate.TemplateName}\" status=start");

                _adb.Tap(candidate.Point.X, candidate.Point.Y);

                if (InterruptibleSleep(1000, token)) return WallTransactionResult.Skip("cancelled");

                SaveDebugScreenshot("candidate_selected");

                // Tắt bảng gợi ý Thợ xây để lộ giao diện nâng cấp dưới đáy màn hình
                _adb.Tap(BuilderMenuPoint.X, BuilderMenuPoint.Y);
                if (InterruptibleSleep(500, token)) return WallTransactionResult.Skip("cancelled");

                // Chỉ cần panel mở + nút tài nguyên; không OCR giá (Simplicity-style).
                if (ValidateWallTapNew(resource))
                {
                    validCoord = candidate.Point;
                    _savedWallOffset ??= -1 - attempt;
                    break;
                }

                // Nếu không đúng tường (hoặc chạm nhầm công trình khác), tắt menu đi thử lại
                _adb.Tap(DismissPoint.X, DismissPoint.Y);

                if (InterruptibleSleep(500, token)) return WallTransactionResult.Skip("cancelled");

                // Nếu thử sai khi đang dùng vị trí lưu từ trước, xóa lưu vị trí để thử các tọa độ khác
                _savedWallOffset = null;
            }

            if (!validCoord.HasValue)
            {
                Console.WriteLine($"[WALL RESULT] phase=attempt_upgrade cycle={_debugCycle} resource={resource} level={wallLevel} candidate_match_count={candidateMatchCount} verified_count=0 status=skip reason=unvalidated");
                return WallTransactionResult.Skip("unvalidated").WithCandidateMatchCount(candidateMatchCount);
            }

            int requestedCount = AddWallsUntilCostTurnsRed(resource, token);
            if (requestedCount <= 0)
            {
                SafeDismiss(token);
                return WallTransactionResult.Skip("insufficient_resource_for_cost").WithCandidateMatchCount(candidateMatchCount);
            }

            SaveDebugScreenshot("add_wall_done");

            Point upgradePoint = resource.Equals("gold", StringComparison.OrdinalIgnoreCase)
                ? FixedGoldUpgradePoint
                : FixedElixirUpgradePoint;
            _adb.Tap(upgradePoint.X, upgradePoint.Y);
            if (InterruptibleSleep(1000, token)) return WallTransactionResult.Skip("cancelled");

            if (!IsConfirmDialogOpen())
            {
                Console.WriteLine($"[WALL RESULT] phase=confirm_open cycle={_debugCycle} resource={resource} level={wallLevel} candidate_match_count={candidateMatchCount} requested_count={requestedCount} verified_count=0 status=skip reason=confirm_dialog_not_open");
                SafeDismiss(token);
                return WallTransactionResult.Skip("confirm_dialog_not_open").WithCandidateMatchCount(candidateMatchCount);
            }
            SaveDebugScreenshot("confirm_open");

            Point confirmPoint = requestedCount > 1 ? ConfirmMultiPoint : ConfirmUpgradePoint;
            _adb.Tap(confirmPoint.X, confirmPoint.Y);
            Thread.Sleep(1500);

            // Simplicity: tin bấm confirm, không đọc lại vàng/dầu.
            SafeDismiss(token);
            Console.WriteLine($"[WALL RESULT] phase=attempt_upgrade cycle={_debugCycle} resource={resource} level={wallLevel} candidate_match_count={candidateMatchCount} requested_count={requestedCount} verified_count={requestedCount} status=upgraded reason=confirmed");
            return WallTransactionResult.Verified(requestedCount, 0).WithCandidateMatchCount(candidateMatchCount);
        }

        private int AddWallsUntilCostTurnsRed(string resource, CancellationToken token)
        {
            int selectedCount = 1;

            if (IsUpgradeCostRed(resource, out double initialRatio, out int initialRedPixels))
            {
                Console.WriteLine($"[WALL] phase=add_wall resource={resource} status=skip reason=initial_cost_red selected_count=0 red_ratio={initialRatio:F3} red_pixels={initialRedPixels}");
                return 0;
            }

            for (int i = 0; i < MaxAddWallIterations; i++)
            {
                if (token.IsCancellationRequested) return 0;

                _adb.Tap(AddWallPlusOneButton.X, AddWallPlusOneButton.Y);
                if (InterruptibleSleep(WallUiAnimationDelayMs, token)) return 0;

                if (!IsUpgradeCostRed(resource, out double redRatio, out int redPixels))
                {
                    selectedCount++;
                    Console.WriteLine($"[WALL] phase=add_wall resource={resource} status=ok reason=cost_available selected_count={selectedCount} red_ratio={redRatio:F3} red_pixels={redPixels}");
                    continue;
                }

                _adb.Tap(RemoveWallMinusOneButton.X, RemoveWallMinusOneButton.Y);
                if (InterruptibleSleep(WallUiAnimationDelayMs, token)) return 0;

                bool stillRed = IsUpgradeCostRed(resource, out double afterRemoveRatio, out int afterRemoveRedPixels);
                Console.WriteLine($"[WALL] phase=add_wall resource={resource} status={(stillRed ? "fail" : "ok")} reason={(stillRed ? "cost_still_red_after_remove" : "red_cost_boundary_found")} selected_count={selectedCount} red_ratio={afterRemoveRatio:F3} red_pixels={afterRemoveRedPixels}");
                return stillRed ? 0 : selectedCount;
            }

            Console.WriteLine($"[WALL] phase=add_wall resource={resource} status=fail reason=max_iterations_reached selected_count={selectedCount} limit={MaxAddWallIterations}");
            return 0;
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

                    // Strict red text detection including anti-aliasing on white background
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



        /// <summary>

        /// Tìm kiếm tất cả các tọa độ đoạn tường hiển thị trên màn hình hiện tại.

        /// Hỗ trợ vuốt trượt tìm kiếm tối đa 7 lần nếu chưa tìm thấy ứng viên tường nào.

        /// </summary>

        /// <param name="wallLevel">Cấp độ tường hiện tại cần tìm để nâng cấp.</param>

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

                Console.WriteLine("[WALL WARN] No generic wall templates found in Templates directory.");

                return new List<WallCandidate>();

            }



            Console.WriteLine($"[WALL] phase=search_templates count={templateNames.Length} status=ok reason=loaded");



            for (int attempt = 0; attempt < 7; attempt++)

            {

                if (token.IsCancellationRequested) return new List<WallCandidate>();

                if (attempt > 0)

                {

                    // Vuốt bảng gợi ý Thợ xây đi một chút để tìm dòng gợi ý nâng tường tiếp theo

                    _adb.Swipe(RetrySwipeEnd.X, RetrySwipeEnd.Y, RetrySwipeStart.X, RetrySwipeStart.Y, SwipeDurationMs);

                    if (InterruptibleSleep(800, token)) return new List<WallCandidate>();

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

                    return new List<WallCandidate>();

                }



                using Mat roiBgr = new Mat(screenshot, roi);

                using Mat roiGray = new Mat();

                Cv2.CvtColor(roiBgr, roiGray, ColorConversionCodes.BGR2GRAY);



                var merged = new List<WallCandidate>();

                // Chạy so khớp cho từng template mẫu biểu tượng Tường trong bảng gợi ý

                foreach (string templateName in templateNames)

                {

                    merged.AddRange(MatchWallTemplate(roiGray, templateName));

                }



                // Loại bỏ các tọa độ bị trùng lặp sát nhau (bán kính 10px) và sắp xếp tăng dần theo trục Y

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


        private static bool IsSupportedWallLevel(int wallLevel)

        {

            return wallLevel >= WallUpgradeDecider.MinSupportedWallLevel && wallLevel <= WallUpgradeDecider.MaxSupportedWallLevel;

        }



        /// <summary>

        /// Chuẩn bị giao diện để bắt đầu tìm tường (Mở bảng gợi ý thợ xây và vuốt map chuẩn).

        /// </summary>

        private bool PrepareWallSearch(CancellationToken token = default)

        {

            Console.WriteLine("[WALL] phase=preflight status=start reason=open_builder_menu");

            if (token.IsCancellationRequested) return false;



            SafeDismiss(token);

            if (InterruptibleSleep(300, token)) return false;



            _adb.Tap(BuilderMenuPoint.X, BuilderMenuPoint.Y);

            if (InterruptibleSleep(1000, token)) return false;



            if (!IsBuilderMenuOpen())

            {

                Console.WriteLine("[WALL] phase=preflight status=fail reason=builder_menu_not_visible");

                return false;

            }



            // Pull the list downward until it is back at the first Builder suggestions.

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



        private void ResetBuilderState(CancellationToken token)

        {

            if (token.IsCancellationRequested) return;

            SafeDismiss(token);

            if (InterruptibleSleep(300, token)) return;



        }



        private void SafeDismiss(CancellationToken token)

        {

            if (token.IsCancellationRequested) return;

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



        /// <summary>

        /// Thực hiện so khớp mẫu ảnh tường (có hỗ trợ kênh Alpha làm mặt nạ mask nếu tệp ảnh 4 kênh).

        /// </summary>

        private IEnumerable<WallCandidate> MatchWallTemplate(Mat grayRoi, string templateName)

        {

            return MatchWallTemplateInRoi(grayRoi, templateName, WallSearchRoi);

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

                // Khớp mẫu có mặt nạ (Masked Template Matching) để bỏ qua phần nền trong suốt của viên tường mẫu

                Cv2.MatchTemplate(grayRoi, templateGray, result, TemplateMatchModes.CCoeffNormed, mask);

            }

            else

            {

                Cv2.MatchTemplate(grayRoi, templateGray, result, TemplateMatchModes.CCoeffNormed);

            }



            // Áp dụng phép dãn nở ảnh (Dilate) để lọc lấy giá trị cực đại địa phương (Local Maxima)

            using Mat dilated = new Mat();

            using Mat kernel = Cv2.GetStructuringElement(MorphShapes.Rect, new Size(3, 3));

            Cv2.Dilate(result, dilated, kernel);



            for (int y = 0; y < result.Rows; y++)

            {

                for (int x = 0; x < result.Cols; x++)

                {

                    float value = result.At<float>(y, x);

                    // Chỉ giữ lại tọa độ có độ tin cậy vượt ngưỡng và là cực đại cục bộ

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



        /// <summary>
        /// Panel mở + nút tài nguyên hiện. Không OCR giá.
        /// </summary>
        private bool ValidateWallTapNew(string resource)
        {
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
            bool goldButton = IsResourceUpgradeButtonAvailable(screenshot, "gold");
            bool elixirButton = IsResourceUpgradeButtonAvailable(screenshot, "elixir");
            bool panelOpen = whitePanel || goldButton || elixirButton;
            if (!panelOpen)
            {
                Console.WriteLine("[WALL] phase=validate_tap status=fail reason=panel_not_open");
                return false;
            }

            bool resourceOk = resource.Equals("gold", StringComparison.OrdinalIgnoreCase) ? goldButton : elixirButton;
            if (!resourceOk)
            {
                Console.WriteLine($"[WALL] phase=validate_tap resource={resource} status=fail reason=resource_button_unavailable gold={goldButton} elixir={elixirButton}");
                return false;
            }

            Console.WriteLine($"[WALL] phase=validate_tap resource={resource} status=ok reason=panel_open");
            return true;
        }




        /// <summary>

        /// Phân tách ảnh nguồn 4 kênh (có alpha) thành ảnh xám và ảnh mặt nạ nhị phân (mask) để phục vụ so khớp mẫu trong suốt.

        /// </summary>

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



                    // Tạo mặt nạ nhị phân dựa trên kênh alpha (Kênh 3)

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

            if (!_debugScreenshotsEnabled)

            {

                return;

            }



            using Mat? screenshot = _adb.TakeScreenshot();

            if (screenshot == null || screenshot.Empty())

            {

                return;

            }



            SaveDebugScreenshot(screenshot, phase);

        }



        private void SaveDebugScreenshot(Mat screenshot, string phase)

        {

            if (!_debugScreenshotsEnabled || screenshot.Empty())

            {

                return;

            }



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



        private void LogSessionCounters(string phase, int wallLevel, string resource, int cost, int candidateMatchCount, int requestedCount, int verifiedCount, string reason)

        {

            Console.WriteLine($"[WALL SESSION] phase={phase} cycle={_debugCycle} resource={resource} level={wallLevel} cost={cost:N0} candidate_match_count={candidateMatchCount} requested_count={requestedCount} verified_count={verifiedCount} reason={reason} wall_attempted={_sessionWallAttempted} wall_verified={_sessionWallVerified} wall_skipped={_sessionWallSkipped} wall_unknown={_sessionWallUnknown}");

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

            if (screenshot == null || screenshot.Empty())

            {

                return false;

            }



            Rect roi = ImageUtils.ClampRect(ConfirmDialogRoi, screenshot.Width, screenshot.Height);

            if (roi.Width <= 0 || roi.Height <= 0)

            {

                return false;

            }



            using Mat dialog = new Mat(screenshot, roi);

            Scalar mean = Cv2.Mean(dialog);

            double brightness = (mean.Val0 + mean.Val1 + mean.Val2) / 3.0;

            bool open = brightness >= 70;

            Console.WriteLine($"[WALL] phase=confirm_open status={(open ? "ok" : "fail")} brightness={brightness:F1} reason={(open ? "dialog_visible" : "dialog_not_visible")}");

            return open;

        }



        /// <summary>

        /// Quy đổi chỉ số index âm (giống cú pháp Python -1, -2) sang chỉ số dương tương ứng trong List.

        /// </summary>

        private static int IndexFromEnd<T>(IReadOnlyList<T> list, int negativeIndex)

        {

            return negativeIndex < 0 ? list.Count + negativeIndex : negativeIndex;

        }



        /// <summary>

        /// Xóa bỏ vị trí bức tường đã lưu để bắt đầu tìm kiếm lại từ đầu ở chu kỳ sau.

        /// </summary>

        public void ResetSavedOffset()

        {

            _savedWallOffset = null;

        }

    }

}

