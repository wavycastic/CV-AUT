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
    internal sealed class WallUpdater
    {
        // Vùng ROI tìm kiếm tường trên bản đồ (Tránh phần rìa chứa các nút UI cản trở)
        private static readonly Rect WallSearchRoi = Rect.FromLTRB(270, 100, 1339, 785);

        // Bảng giá nâng cấp tường tiêu chuẩn theo cấp độ (Clash of Clans)
        private static readonly Dictionary<int, int> WallCosts = new()
        {
            { 1, 1_000 },       { 2, 5_000 },       { 3, 10_000 },
            { 4, 20_000 },      { 5, 30_000 },      { 6, 50_000 },
            { 7, 75_000 },      { 8, 100_000 },     { 9, 200_000 },
            { 10, 500_000 },    { 11, 1_000_000 },  { 12, 1_500_000 },
            { 13, 2_000_000 },  { 14, 3_000_000 },  { 15, 4_000_000 },
            { 16, 5_000_000 },  { 17, 7_000_000 },  { 18, 10_000_000 }
        };

        // Vùng hiển thị giá tiền nâng cấp trên bảng thông tin ở đáy màn hình
        private static readonly Rect UpgradeCostRoi = new(680, 730, 240, 45);

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
        private static readonly Point AddMoreButton = new(800, 720);
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
        private const int DefaultWallBatchLimit = 10;
        private const int SupportedScreenshotWidth = 1600;
        private const int SupportedScreenshotHeight = 900;
        private const int MaxCandidateAttempts = 3;
        private const int MaxBuilderMenuPages = 10;
        private const double ResourceSpendTolerance = 0.20;
        private const double BuilderRowCostTolerance = 0.25;

        // Lưu trữ vị trí index bù của bức tường nâng cấp gần nhất để tăng tốc độ chọn ở chu kỳ tiếp theo
        private int? _savedWallOffset;
        private bool _debugScreenshotsEnabled;
        private int _debugCycle;
        private int _sessionWallAttempted;
        private int _sessionWallVerified;
        private int _sessionWallSkipped;
        private int _sessionWallUnknown;

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
            int wallBatchLimit = 1,
            bool debugScreenshots = false,
            int cycle = 0,
            CancellationToken token = default)
        {
            if (token.IsCancellationRequested) return 0;
            _debugScreenshotsEnabled = debugScreenshots;
            _debugCycle = cycle;
            int targetWallLevel = wallLevel;
            WallCosts.TryGetValue(targetWallLevel, out int targetCost);

            using (Mat? layoutScreenshot = _adb.TakeScreenshot())
            {
                if (!ValidateSupportedLayout(layoutScreenshot, out string layoutReason))
                {
                    _sessionWallSkipped++;
                    LogSessionCounters("layout", wallLevel, "none", targetCost, 0, 0, 0, layoutReason);
                    return 0;
                }

                SaveDebugScreenshot(layoutScreenshot!, "preflight");
            }

            int cappedBatchLimit = Math.Clamp(wallBatchLimit, 0, DefaultWallBatchLimit);
            Console.WriteLine($"[WALL] phase=read_resources cycle={cycle} target_level={targetWallLevel} batch_limit={cappedBatchLimit} status=start reason=legacy_builder_scan");

            List<BuilderWallRow> wallRows = ScanBuilderWallRows(targetWallLevel, token);
            if (wallRows.Count == 0)
            {
                _sessionWallSkipped++;
                Console.WriteLine($"[WALL RESULT] phase=builder_scan cycle={cycle} target_level={targetWallLevel} candidate_match_count=0 requested_count=0 verified_count=0 status=skip reason=no_wall_rows");
                LogSessionCounters("builder_scan", targetWallLevel, "none", targetCost, 0, 0, 0, "no_wall_rows");
                return 0;
            }

            int totalVerified = 0;
            int remainingBatch = cappedBatchLimit;
            foreach (BuilderWallRow row in wallRows.OrderBy(row => row.Cost).ThenBy(row => row.SourceLevel))
            {
                if (token.IsCancellationRequested) break;
                if (remainingBatch <= 0)
                {
                    Console.WriteLine($"[WALL PLAN] phase=target_plan cycle={cycle} target_level={targetWallLevel} verified_count={totalVerified} status=stop reason=batch_limit_reached");
                    break;
                }

                var (gold, elixir, _) = IsTarget.ExtractHomeResources(_adb, _vision);
                Console.WriteLine($"[WALL PLAN] phase=target_plan cycle={cycle} target_level={targetWallLevel} source_level={row.SourceLevel} cost={row.Cost:N0} page={row.Page} y={row.Point.Y} gold={gold:N0} elixir={elixir:N0} status=check");

                var decision = WallUpgradeDecider.Decide(new WallUpgradeDecisionInput(
                    row.SourceLevel,
                    row.Cost,
                    gold,
                    elixir,
                    wallGoldThreshold,
                    wallElixirThreshold,
                    wallGoldReserve,
                    wallElixirReserve,
                    remainingBatch));

                Console.WriteLine($"[WALL DECISION] phase=target_plan cycle={cycle} target_level={targetWallLevel} source_level={row.SourceLevel} cost={row.Cost:N0} affordable_gold={decision.AffordableGold} affordable_elixir={decision.AffordableElixir} requested_count={decision.RequestedCount} gthr={wallGoldThreshold:N0} ethr={wallElixirThreshold:N0} gres={wallGoldReserve:N0} eres={wallElixirReserve:N0} status=check");

                if (decision.Resource == WallUpgradeResource.None)
                {
                    _sessionWallSkipped++;
                    Console.WriteLine($"[WALL RESULT] phase=target_plan cycle={cycle} resource=none target_level={targetWallLevel} source_level={row.SourceLevel} cost={row.Cost:N0} candidate_match_count={wallRows.Count} affordable_count=0 requested_count=0 verified_count=0 status=skip reason={decision.SkipReason}");
                    LogSessionCounters("target_plan", row.SourceLevel, "none", row.Cost, wallRows.Count, 0, 0, decision.SkipReason);
                    continue;
                }

                string bestResource = decision.Resource == WallUpgradeResource.Gold ? "gold" : "elixir";
                int affordableCount = decision.Resource == WallUpgradeResource.Gold ? decision.AffordableGold : decision.AffordableElixir;
                Console.WriteLine($"[WALL DECISION] phase=decide cycle={cycle} resource={bestResource} target_level={targetWallLevel} source_level={row.SourceLevel} cost={row.Cost:N0} affordable_count={affordableCount} requested_count={decision.RequestedCount} status=selected");

                _sessionWallAttempted += decision.RequestedCount;
                Console.WriteLine($"[WALL] phase=batch_plan cycle={cycle} resource={bestResource} available={(bestResource == "gold" ? gold : elixir):N0} reserve={(bestResource == "gold" ? wallGoldReserve : wallElixirReserve):N0} threshold={(bestResource == "gold" ? wallGoldThreshold : wallElixirThreshold):N0} unit_cost={row.Cost:N0} affordable_count={affordableCount} batch_remaining={remainingBatch} requested_count={decision.RequestedCount} more_taps={Math.Max(0, decision.RequestedCount - 1)} status=selected");
                WallTransactionResult result = UpgradeWallBulk(bestResource, row.SourceLevel, decision.RequestedCount, row.Cost, gold, elixir, token, row);
                if (result.VerifiedCount > 0)
                {
                    _sessionWallVerified += result.VerifiedCount;
                    totalVerified += result.VerifiedCount;
                    remainingBatch = Math.Max(0, cappedBatchLimit - totalVerified);
                }
                else if (result.Reason == "outcome_unknown") _sessionWallUnknown++;
                else _sessionWallSkipped++;

                Console.WriteLine($"[WALL RESULT] phase=target_plan cycle={cycle} resource={bestResource} target_level={targetWallLevel} source_level={row.SourceLevel} cost={row.Cost:N0} candidate_match_count={result.CandidateMatchCount} affordable_count={affordableCount} requested_count={decision.RequestedCount} verified_count={result.VerifiedCount} status={(result.VerifiedCount > 0 ? "upgraded" : result.Reason == "outcome_unknown" ? "unknown" : "skip")} reason={result.Reason}");
                LogSessionCounters("target_plan", row.SourceLevel, bestResource, row.Cost, result.CandidateMatchCount, decision.RequestedCount, result.VerifiedCount, result.Reason);

                if (result.VerifiedCount <= 0 && result.Reason == "outcome_unknown") break;
            }

            return totalVerified;
        }

        private sealed record WallCandidate(Point Point, double Confidence, string TemplateName);

        private sealed record BuilderWallRow(int SourceLevel, int Cost, Point Point, int Page, double Confidence, string TemplateName, int ReadCost);

        private sealed record WallTransactionResult(int VerifiedCount, string Reason, int CandidateMatchCount = 0)
        {
            public static WallTransactionResult Skip(string reason) => new(0, reason);
            public WallTransactionResult WithCandidateMatchCount(int count) => this with { CandidateMatchCount = count };
            public static WallTransactionResult Verified(int count) => new(count, "verified");
        }

        /// <summary>
        /// Thực hiện quy trình nâng cấp hàng loạt tường lên cấp độ chỉ định bằng tài nguyên vàng hoặc elixir.
        /// Thử nghiệm tối đa 3 bức tường cho đến khi tìm được bức tường xác thực hợp lệ.
        /// </summary>
        /// <returns>Kết quả giao dịch, chỉ có VerifiedCount khi post-confirm verification thành công.</returns>
        private WallTransactionResult UpgradeWallBulk(
            string resource,
            int wallLevel,
            int requestedCount,
            int wallCost,
            int startGold,
            int startElixir,
            CancellationToken token = default,
            BuilderWallRow? preferredRow = null)
        {
            if (token.IsCancellationRequested) return WallTransactionResult.Skip("cancelled");

            int cappedRequestedCount = Math.Clamp(requestedCount, 0, DefaultWallBatchLimit);
            Console.WriteLine($"[WALL] phase=attempt_upgrade resource={resource} level={wallLevel} requested_count={cappedRequestedCount} status=start");

            if (cappedRequestedCount <= 0)
            {
                Console.WriteLine($"[WALL RESULT] phase=attempt_upgrade resource={resource} level={wallLevel} status=skip reason=invalid_requested_count requested_count={requestedCount}");
                SafeDismiss(token);
                return WallTransactionResult.Skip("invalid_requested_count");
            }

            WallTransactionResult result = TryUpgradeWallBatch(resource, wallLevel, cappedRequestedCount, wallCost, startGold, startElixir, token, preferredRow);
            if (result.VerifiedCount > 0 || cappedRequestedCount == 1 ||
                result.Reason is not ("selection_count_unverified" or "selection_count_mismatch"))
            {
                return result;
            }

            Console.WriteLine($"[WALL] phase=selection_verify resource={resource} level={wallLevel} status=retry requested_count=1 reason=retry_single previous_reason={result.Reason}");
            SafeDismiss(token);
            ResetBuilderState(token);
            return TryUpgradeWallBatch(resource, wallLevel, 1, wallCost, startGold, startElixir, token, preferredRow);
        }

        private WallTransactionResult TryUpgradeWallBatch(
            string resource,
            int wallLevel,
            int requestedCount,
            int wallCost,
            int startGold,
            int startElixir,
            CancellationToken token,
            BuilderWallRow? preferredRow = null)
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

                List<WallCandidate> candidates;
                if (attempt == 0 && preferredRow != null && PrepareBuilderPage(preferredRow.Page, token))
                {
                    candidates = new List<WallCandidate>
                    {
                        new(preferredRow.Point, preferredRow.Confidence, preferredRow.TemplateName)
                    };
                }
                else
                {
                    // Lấy tất cả các tường tìm thấy trong bảng gợi ý Thợ xây
                    candidates = FindAllWallCandidates(token)
                        .Where(candidate => !triedCoords.Any(tried => Math.Abs(candidate.Point.Y - tried.Y) <= 20))
                        .ToList();
                }
                candidateMatchCount = Math.Max(candidateMatchCount, candidates.Count);

                if (candidates.Count == 0)
                {
                    Console.WriteLine($"[WALL RESULT] phase=attempt_upgrade cycle={_debugCycle} resource={resource} level={wallLevel} cost={wallCost:N0} candidate_match_count={candidateMatchCount} requested_count={requestedCount} verified_count=0 status=skip reason=no_candidates");
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
                    candidate = candidates[candidates.Count - 1];
                }

                triedCoords.Add(candidate.Point);

                // Nhấp chọn biểu tượng Wall trong bảng gợi ý Thợ xây để game tự định vị và chọn tường
                Console.WriteLine($"[WALL] phase=select_candidate cycle={_debugCycle} resource={resource} level={wallLevel} cost={wallCost:N0} candidate_match_count={candidates.Count} attempt={attempt + 1} x={candidate.Point.X} y={candidate.Point.Y} conf={candidate.Confidence:F3} template=\"{candidate.TemplateName}\" status=start");
                _adb.Tap(candidate.Point.X, candidate.Point.Y);
                if (InterruptibleSleep(1000, token)) return WallTransactionResult.Skip("cancelled");
                SaveDebugScreenshot("candidate_selected");

                // Tắt bảng gợi ý Thợ xây để lộ giao diện nâng cấp dưới đáy màn hình
                _adb.Tap(BuilderMenuPoint.X, BuilderMenuPoint.Y);
                if (InterruptibleSleep(500, token)) return WallTransactionResult.Skip("cancelled");

                // Kiểm tra xem bảng nâng cấp đã mở và khớp giá tiền cấp độ tương ứng hay không
                if (ValidateWallTapNew(wallLevel, wallCost, resource))
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
                Console.WriteLine($"[WALL RESULT] phase=attempt_upgrade cycle={_debugCycle} resource={resource} level={wallLevel} cost={wallCost:N0} candidate_match_count={candidateMatchCount} requested_count={requestedCount} verified_count=0 status=skip reason=unvalidated");
                return WallTransactionResult.Skip("unvalidated").WithCandidateMatchCount(candidateMatchCount);
            }

            if (requestedCount > 1)
            {
                Console.WriteLine($"[WALL] phase=add_more resource={resource} level={wallLevel} count={requestedCount - 1} status=start");
                for (int i = 0; i < requestedCount - 1; i++)
                {
                    if (token.IsCancellationRequested) return WallTransactionResult.Skip("cancelled");
                    _adb.Tap(AddMoreButton.X, AddMoreButton.Y);
                    if (InterruptibleSleep(350, token)) return WallTransactionResult.Skip("cancelled");
                }

                if (!VerifySelectionCount(wallLevel, requestedCount, wallCost, token, out string selectionReason))
                {
                    Console.WriteLine($"[WALL RESULT] phase=selection_verify cycle={_debugCycle} resource={resource} level={wallLevel} cost={wallCost:N0} candidate_match_count={candidateMatchCount} requested_count={requestedCount} verified_count=0 status=skip reason={selectionReason}");
                    SafeDismiss(token);
                    return WallTransactionResult.Skip(selectionReason).WithCandidateMatchCount(candidateMatchCount);
                }
                SaveDebugScreenshot("selection_verified");
            }

            Point upgradePoint = resource.Equals("gold", StringComparison.OrdinalIgnoreCase)
                ? FixedGoldUpgradePoint
                : FixedElixirUpgradePoint;
            _adb.Tap(upgradePoint.X, upgradePoint.Y);
            if (InterruptibleSleep(1000, token)) return WallTransactionResult.Skip("cancelled");

            if (!IsConfirmDialogOpen())
            {
                Console.WriteLine($"[WALL RESULT] phase=confirm_open cycle={_debugCycle} resource={resource} level={wallLevel} cost={wallCost:N0} candidate_match_count={candidateMatchCount} requested_count={requestedCount} verified_count=0 status=skip reason=confirm_dialog_not_open");
                SafeDismiss(token);
                return WallTransactionResult.Skip("confirm_dialog_not_open").WithCandidateMatchCount(candidateMatchCount);
            }
            SaveDebugScreenshot("confirm_open");

            Point confirmPoint = requestedCount > 1 ? ConfirmMultiPoint : ConfirmUpgradePoint;
            _adb.Tap(confirmPoint.X, confirmPoint.Y);
            Thread.Sleep(1500);

            if (!VerifyTransactionOutcome(resource, requestedCount, wallCost, startGold, startElixir, out string outcomeReason))
            {
                Console.WriteLine($"[WALL RESULT] phase=outcome_verify cycle={_debugCycle} resource={resource} level={wallLevel} cost={wallCost:N0} candidate_match_count={candidateMatchCount} requested_count={requestedCount} verified_count=0 status=unknown reason={outcomeReason}");
                SafeDismiss(token);
                return WallTransactionResult.Skip("outcome_unknown").WithCandidateMatchCount(candidateMatchCount);
            }
            SaveDebugScreenshot("outcome_verified");

            SafeDismiss(token);
            Console.WriteLine($"[WALL RESULT] phase=attempt_upgrade cycle={_debugCycle} resource={resource} level={wallLevel} cost={wallCost:N0} candidate_match_count={candidateMatchCount} requested_count={requestedCount} verified_count={requestedCount} status=upgraded reason=verified");
            return WallTransactionResult.Verified(requestedCount).WithCandidateMatchCount(candidateMatchCount);
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

        private bool PrepareBuilderPage(int page, CancellationToken token)
        {
            if (!PrepareWallSearch(token)) return false;

            int targetPage = Math.Clamp(page, 0, MaxBuilderMenuPages - 1);
            for (int i = 0; i < targetPage; i++)
            {
                if (token.IsCancellationRequested) return false;
                _adb.Swipe(RetrySwipeEnd.X, RetrySwipeEnd.Y, RetrySwipeStart.X, RetrySwipeStart.Y, SwipeDurationMs);
                if (InterruptibleSleep(500, token)) return false;
            }

            return true;
        }

        private List<BuilderWallRow> ScanBuilderWallRows(int targetWallLevel, CancellationToken token)
        {
            var rows = new List<BuilderWallRow>();
            if (!WallCosts.TryGetValue(targetWallLevel, out int expectedCost))
            {
                Console.WriteLine($"[WALL WARN] phase=builder_scan target_level={targetWallLevel} status=skip reason=missing_target_cost");
                return rows;
            }

            if (!PrepareWallSearch(token))
            {
                Console.WriteLine($"[WALL RESULT] phase=builder_scan target_level={targetWallLevel} status=skip reason=preflight_failed");
                return rows;
            }

            string[] templateNames = GetWallTemplateNames();
            if (templateNames.Length == 0)
            {
                Console.WriteLine("[WALL WARN] phase=builder_scan status=skip reason=no_wall_templates");
                return rows;
            }

            for (int page = 0; page < MaxBuilderMenuPages; page++)
            {
                if (token.IsCancellationRequested) break;
                using Mat? screenshot = _adb.TakeScreenshot();
                if (screenshot == null || screenshot.Empty())
                {
                    Console.WriteLine($"[WALL WARN] phase=builder_scan page={page} status=retry reason=screenshot_failed");
                }
                else
                {
                    IReadOnlyList<BuilderWallRow> pageRows = ExtractBuilderWallRows(screenshot, templateNames, targetWallLevel, expectedCost, page);
                    rows.AddRange(pageRows);
                    Console.WriteLine($"[WALL] phase=builder_scan page={page} target_level={targetWallLevel} expected_cost={expectedCost:N0} rows={pageRows.Count} total_rows={rows.Count} status=scan");
                }

                if (page == MaxBuilderMenuPages - 1) break;
                _adb.Swipe(RetrySwipeEnd.X, RetrySwipeEnd.Y, RetrySwipeStart.X, RetrySwipeStart.Y, SwipeDurationMs);
                if (InterruptibleSleep(700, token)) break;
            }

            List<BuilderWallRow> deduped = rows
                .GroupBy(row => new { row.Page, BucketY = row.Point.Y / 24 })
                .Select(group => group.OrderByDescending(row => row.Confidence).ThenBy(row => Math.Abs(row.ReadCost - expectedCost)).First())
                .OrderBy(row => row.Cost)
                .ThenBy(row => row.Page)
                .ThenBy(row => row.Point.Y)
                .ToList();

            Console.WriteLine($"[WALL PLAN] phase=builder_scan target_level={targetWallLevel} expected_cost={expectedCost:N0} rows={deduped.Count} status={(deduped.Count > 0 ? "ok" : "skip")} reason={(deduped.Count > 0 ? "matched_cost" : "no_matching_wall_cost")}");
            return deduped;
        }

        private IReadOnlyList<BuilderWallRow> ExtractBuilderWallRows(Mat screenshot, string[] templateNames, int targetWallLevel, int expectedCost, int page)
        {
            Rect safeRoi = ImageUtils.ClampRect(BuilderUpgradeMenuRoi, screenshot.Width, screenshot.Height);
            if (safeRoi.Width <= 0 || safeRoi.Height <= 0)
            {
                Console.WriteLine($"[WALL WARN] phase=builder_scan page={page} status=skip reason=empty_menu_roi width={screenshot.Width} height={screenshot.Height}");
                return Array.Empty<BuilderWallRow>();
            }

            using Mat roiBgr = new Mat(screenshot, safeRoi);
            using Mat roiGray = new Mat();
            Cv2.CvtColor(roiBgr, roiGray, ColorConversionCodes.BGR2GRAY);

            var matchedIcons = new List<WallCandidate>();
            foreach (string templateName in templateNames)
            {
                matchedIcons.AddRange(MatchWallTemplateInRoi(roiGray, templateName, safeRoi));
            }

            var rows = new List<BuilderWallRow>();
            foreach (WallCandidate icon in DedupeCandidates(matchedIcons, 12).OrderBy(candidate => candidate.Point.Y))
            {
                Rect costRoi = BuildBuilderRowCostRoi(icon.Point, screenshot.Width, screenshot.Height);
                if (costRoi.Width <= 0 || costRoi.Height <= 0) continue;

                if (!TryReadCost(screenshot, costRoi, out int readCost, out double ocrConfidence))
                {
                    Console.WriteLine($"[WALL] phase=builder_row page={page} target_level={targetWallLevel} x={icon.Point.X} y={icon.Point.Y} status=skip reason=cost_ocr_failed");
                    continue;
                }

                if (!IsApproxCost(readCost, expectedCost, BuilderRowCostTolerance))
                {
                    Console.WriteLine($"[WALL] phase=builder_row page={page} target_level={targetWallLevel} x={icon.Point.X} y={icon.Point.Y} read_cost={readCost:N0} expected={expectedCost:N0} conf={ocrConfidence:F2} status=skip reason=cost_mismatch");
                    continue;
                }

                rows.Add(new BuilderWallRow(targetWallLevel, readCost, icon.Point, page, Math.Min(icon.Confidence, ocrConfidence), icon.TemplateName, readCost));
                Console.WriteLine($"[WALL] phase=builder_row page={page} target_level={targetWallLevel} x={icon.Point.X} y={icon.Point.Y} read_cost={readCost:N0} expected={expectedCost:N0} conf={ocrConfidence:F2} status=match");
            }

            return rows;
        }

        private static Rect BuildBuilderRowCostRoi(Point iconPoint, int width, int height)
        {
            // Cost trong Builder menu nằm về bên phải icon wall; ROI rộng để bắt cả số có dấu phân cách.
            Rect roi = new(iconPoint.X + 52, iconPoint.Y - 18, 210, 42);
            return ImageUtils.ClampRect(roi, width, height);
        }

        private bool TryReadCost(Mat screenshot, Rect roi, out int cost, out double confidence)
        {
            Rect safeRoi = ImageUtils.ClampRect(roi, screenshot.Width, screenshot.Height);
            cost = 0;
            confidence = 0;
            if (safeRoi.Width <= 0 || safeRoi.Height <= 0) return false;

            if (_vision.TryExtractNumericalMetrics(screenshot, safeRoi, out cost, out confidence, useRgbThresh: true)) return true;
            return _vision.TryExtractNumericalMetrics(screenshot, safeRoi, out cost, out confidence);
        }

        private static bool IsApproxCost(int readCost, int expectedCost, double tolerance)
        {
            if (readCost <= 0 || expectedCost <= 0) return false;
            double error = Math.Abs(readCost - expectedCost) / (double)expectedCost;
            return error <= tolerance;
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

        private IEnumerable<WallCandidate> MatchWallTemplateInRoi(Mat grayRoi, string templateName, Rect sourceRoi)
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
                    if (value >= WallSearchThreshold && Math.Abs(value - dilated.At<float>(y, x)) < 0.0001)
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
        /// Xác thực bảng nâng cấp tường bằng màu panel và OCR giá tiền, không phụ thuộc template ảnh theo cấp.
        /// </summary>
        private bool ValidateWallTapNew(int wallLevel, int expectedCost, string resource)
        {
            if (!IsSupportedWallLevel(wallLevel))
            {
                Console.WriteLine($"[WALL WARN] phase=validate status=skip level={wallLevel} reason=unsupported_wall_level supported={WallUpgradeDecider.MinSupportedWallLevel}-{WallUpgradeDecider.MaxSupportedWallLevel}");
                return false;
            }

            using Mat? screenshot = _adb.TakeScreenshot();
            if (screenshot == null || screenshot.Empty())
            {
                Console.WriteLine("[WALL RESULT] phase=validate status=fail reason=screenshot_failed");
                return false;
            }

            int width = screenshot.Width;
            int height = screenshot.Height;
            int px = Math.Clamp(PanelCheckPoint.X, 0, width - 1);
            int py = Math.Clamp(PanelCheckPoint.Y, 0, height - 1);
            Vec3b pixel = screenshot.At<Vec3b>(py, px);
            bool panelOpen = pixel.Item0 >= 200 && pixel.Item1 >= 200 && pixel.Item2 >= 200;

            if (!panelOpen)
            {
                Console.WriteLine($"[WALL RESULT] phase=validate status=fail reason=panel_not_open pixel_bgr=[{pixel.Item0},{pixel.Item1},{pixel.Item2}]");
                return false;
            }

            if (expectedCost <= 0)
            {
                Console.WriteLine($"[WALL RESULT] phase=validate status=fail reason=missing_wall_cost level={wallLevel}");
                return false;
            }

            Rect safeRoi = ImageUtils.ClampRect(UpgradeCostRoi, width, height);
            if (safeRoi.Width <= 0 || safeRoi.Height <= 0)
            {
                Console.WriteLine($"[WALL RESULT] phase=validate status=fail reason=empty_cost_roi width={width} height={height}");
                return false;
            }

            if (_vision.TryExtractNumericalMetrics(screenshot, safeRoi, out int readCost, out double confidence, useRgbThresh: true))
            {
                double error = Math.Abs(readCost - expectedCost) / (double)expectedCost;
                if (error <= 0.15)
                {
                    bool buttonAvailable = IsResourceUpgradeButtonAvailable(screenshot, resource);
                    Console.WriteLine($"[WALL RESULT] phase=validate level={wallLevel} resource={resource} status={(buttonAvailable ? "pass" : "fail")} read={readCost:N0} expected={expectedCost:N0} conf={confidence:F2} reason={(buttonAvailable ? "cost_matched" : "upgrade_button_unavailable")}");
                    return buttonAvailable;
                }

                Console.WriteLine($"[WALL RESULT] phase=validate level={wallLevel} status=retry read={readCost:N0} expected={expectedCost:N0} error={error:P2} reason=cost_mismatch");
            }
            else
            {
                Console.WriteLine($"[WALL RESULT] phase=validate level={wallLevel} status=retry reason=ocr_failed_to_extract");
            }

            return false;
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

        private bool VerifySelectionCount(int wallLevel, int requestedCount, int wallCost, CancellationToken token, out string reason)
        {
            reason = "selection_count_unverified";
            if (token.IsCancellationRequested)
            {
                reason = "cancelled";
                return false;
            }

            using Mat? screenshot = _adb.TakeScreenshot();
            if (screenshot == null || screenshot.Empty())
            {
                return false;
            }

            Rect safeRoi = ImageUtils.ClampRect(UpgradeCostRoi, screenshot.Width, screenshot.Height);
            if (safeRoi.Width <= 0 || safeRoi.Height <= 0)
            {
                return false;
            }

            if (!_vision.TryExtractNumericalMetrics(screenshot, safeRoi, out int readTotalCost, out double confidence, useRgbThresh: true))
            {
                return false;
            }

            long expectedTotal = (long)requestedCount * wallCost;
            double error = Math.Abs(readTotalCost - expectedTotal) / (double)expectedTotal;
            if (error <= 0.15)
            {
                Console.WriteLine($"[WALL] phase=selection_verify level={wallLevel} requested_count={requestedCount} read={readTotalCost:N0} expected={expectedTotal:N0} conf={confidence:F2} status=ok reason=cost_matched");
                reason = "selection_count_verified";
                return true;
            }

            reason = "selection_count_mismatch";
            Console.WriteLine($"[WALL] phase=selection_verify level={wallLevel} requested_count={requestedCount} read={readTotalCost:N0} expected={expectedTotal:N0} error={error:P2} status=fail reason={reason}");
            return false;
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

        private bool VerifyTransactionOutcome(string resource, int requestedCount, int wallCost, int startGold, int startElixir, out string reason)
        {
            reason = "outcome_unknown";
            var (goldAfter, elixirAfter, _) = IsTarget.ExtractHomeResources(_adb, _vision);
            int before = resource.Equals("gold", StringComparison.OrdinalIgnoreCase) ? startGold : startElixir;
            int after = resource.Equals("gold", StringComparison.OrdinalIgnoreCase) ? goldAfter : elixirAfter;
            long expectedSpend = (long)requestedCount * wallCost;
            long actualSpend = before - after;

            if (actualSpend <= 0)
            {
                reason = "resource_not_decreased";
                return false;
            }

            double error = Math.Abs(actualSpend - expectedSpend) / (double)expectedSpend;
            if (error <= ResourceSpendTolerance)
            {
                Console.WriteLine($"[WALL] phase=outcome_verify resource={resource} requested_count={requestedCount} before={before:N0} after={after:N0} spent={actualSpend:N0} expected={expectedSpend:N0} status=ok reason=resource_decreased");
                reason = "verified";
                return true;
            }

            reason = "resource_delta_mismatch";
            Console.WriteLine($"[WALL] phase=outcome_verify resource={resource} requested_count={requestedCount} before={before:N0} after={after:N0} spent={actualSpend:N0} expected={expectedSpend:N0} error={error:P2} status=fail reason={reason}");
            return false;
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
