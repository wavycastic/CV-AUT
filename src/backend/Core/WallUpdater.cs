using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using OpenCvSharp;
using Point = OpenCvSharp.Point;

namespace CvAut
{
    /// <summary>
    /// Bộ nâng cấp tường (Wall Updater) - điều phối luồng nâng cấp:
    /// - Quét ứng viên tường qua WallCandidateScanner.
    /// - Xác thực giao diện qua WallPanelInspector.
    /// - Ra quyết định Vàng hay Dầu hồng bằng WallUpgradeDecider, kiểm tra cost OCR bằng WallCostPolicy.
    /// - Xác minh tài nguyên (resource delta) sau khi xác nhận giao dịch.
    /// </summary>
    internal sealed partial class WallUpdater
    {
        private readonly IADBHelper _adb;
        private readonly IVisionEngine _vision;
        private readonly WallMenuNavigator _navigator;
        private readonly WallPanelInspector _inspector;
        private readonly WallCandidateScanner _scanner;
        private readonly WallDebugRecorder _debug;

        private int? _savedWallOffset;

        public WallUpdater(IADBHelper adb, IVisionEngine vision, string templatesPath)
        {
            _adb = adb;
            _vision = vision;
            _navigator = new WallMenuNavigator(adb);
            _inspector = new WallPanelInspector(adb);
            _scanner = new WallCandidateScanner(adb, templatesPath, _inspector, _navigator);
            _debug = new WallDebugRecorder(adb);
        }

        private static bool InterruptibleSleep(int milliseconds, CancellationToken token)
            => ThreadingUtil.InterruptibleSleep(milliseconds, token);

        internal static WallCostValidationResult ValidateWallCosts(int goldCost, int elixirCost, double maxMismatchRatio = WallUiLayout.MaxCostMismatchRatio)
            => WallCostPolicy.ValidateWallCosts(goldCost, elixirCost, maxMismatchRatio);

        internal static bool IsResourceDeltaVerified(long resourceBefore, long resourceAfter, long expectedSpend, long tolerance = 0)
            => WallCostPolicy.IsResourceDeltaVerified(resourceBefore, resourceAfter, expectedSpend, tolerance);

        internal static bool IsUpgradeCostRed(Mat screenshot, string resource, out double redRatio, out int redPixels)
            => WallCostPolicy.IsUpgradeCostRed(screenshot, resource, out redRatio, out redPixels);

        /// <summary>
        /// Xử lý nâng cấp tường không phụ thuộc Wall Level.
        /// Quét Builder menu bằng 4 generic Wall templates, đọc tài nguyên hiện tại, tính toán qua WallUpgradeDecider và thực hiện nâng cấp.
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
            _debug.Configure(debugScreenshots, cycle);
            int safeBatchLimit = Math.Clamp(batchLimit, 1, 10);

            Console.WriteLine($"[WALL] phase=target_plan cycle={cycle} status=start gold_start={wallGoldThreshold:N0} elixir_start={wallElixirThreshold:N0} gold_reserve={wallGoldReserve:N0} elixir_reserve={wallElixirReserve:N0} batch_limit={safeBatchLimit}");

            string[] templateNames = _scanner.GetWallTemplateNames();
            if (templateNames.Length == 0)
            {
                Console.WriteLine($"[WALL RESULT] phase=target_plan cycle={cycle} status=skip reason=wall_templates_missing");
                return 0;
            }

            using Mat? initialScreenshot = _adb.TakeScreenshot();
            if (!WallPanelInspector.ValidateSupportedLayout(initialScreenshot, cycle, out string layoutReason))
            {
                Console.WriteLine($"[WALL RESULT] phase=target_plan cycle={cycle} status=skip reason={layoutReason}");
                return 0;
            }

            WallTransactionResult result = UpgradeWallBulk(
                wallGoldThreshold,
                wallElixirThreshold,
                wallGoldReserve,
                wallElixirReserve,
                safeBatchLimit,
                token);

            if (result.VerifiedCount > 0)
            {
                _debug.RecordVerified(result.VerifiedCount);
            }
            else if (string.Equals(result.Reason, "outcome_unknown", StringComparison.OrdinalIgnoreCase) ||
                     string.Equals(result.Reason, "cancelled_post_confirm", StringComparison.OrdinalIgnoreCase) ||
                     result.Reason.StartsWith("post_confirm_", StringComparison.OrdinalIgnoreCase) ||
                     result.Reason.Contains("delta_mismatch", StringComparison.OrdinalIgnoreCase))
            {
                _debug.RecordUnknown();
            }
            else
            {
                _debug.RecordSkipped();
            }

            _debug.LogSessionCounters(
                "handle_home_resources",
                result.Resource,
                result.Cost,
                result.CandidateMatchCount,
                result.RequestedCount,
                result.VerifiedCount,
                result.Reason);

            return result.VerifiedCount;
        }

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
            int safeBatchLimit = Math.Clamp(batchLimit, 1, 10);
            Console.WriteLine($"[WALL] phase=attempt_upgrade status=start batch_limit={safeBatchLimit}");
            return TryUpgradeWallBatch(wallGoldThreshold, wallElixirThreshold, wallGoldReserve, wallElixirReserve, safeBatchLimit, token);
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
            int safeBatchLimit = Math.Clamp(batchLimit, 1, 10);
            int candidateMatchCount = 0;
            var triedCoords = new List<Point>();
            Point? validCoord = null;

            try
            {
                for (int attempt = 0; attempt < WallUiLayout.MaxCandidateAttempts; attempt++)
                {
                    if (token.IsCancellationRequested)
                    {
                        _navigator.BestEffortDismiss();
                        return WallTransactionResult.Skip("cancelled");
                    }

                    List<WallCandidate> candidates = _scanner.FindAllWallCandidates(token)
                        .Where(candidate => !triedCoords.Any(tried => Math.Abs(candidate.Point.Y - tried.Y) <= 20))
                        .ToList();

                    candidateMatchCount = Math.Max(candidateMatchCount, candidates.Count);
                    if (candidates.Count == 0)
                    {
                        Console.WriteLine($"[WALL RESULT] phase=attempt_upgrade cycle={_debug.Cycle} candidate_match_count={candidateMatchCount} verified_count=0 status=skip reason=no_candidates");
                        _navigator.BestEffortDismiss();
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

                    Console.WriteLine($"[WALL] phase=select_candidate cycle={_debug.Cycle} candidate_match_count={candidates.Count} attempt={attempt + 1} x={candidate.Point.X} y={candidate.Point.Y} conf={candidate.Confidence:F3} template=\"{candidate.TemplateName}\" status=start");
                    _adb.Tap(candidate.Point.X, candidate.Point.Y);
                    if (InterruptibleSleep(1000, token)) return WallTransactionResult.Skip("cancelled");
                    _debug.Capture("candidate_selected");

                    // Tắt bảng Thợ xây để hiện panel nâng cấp bên dưới
                    _adb.Tap(WallUiLayout.BuilderMenuPoint.X, WallUiLayout.BuilderMenuPoint.Y);
                    if (InterruptibleSleep(500, token)) return WallTransactionResult.Skip("cancelled");

                    if (_inspector.ValidateWallPanelOpen(out _, out _))
                    {
                        validCoord = candidate.Point;
                        _savedWallOffset ??= -1 - attempt;
                        break;
                    }

                    _adb.Tap(WallUiLayout.DismissPoint.X, WallUiLayout.DismissPoint.Y);
                    if (InterruptibleSleep(500, token)) return WallTransactionResult.Skip("cancelled");
                    _savedWallOffset = null;
                }

                if (!validCoord.HasValue)
                {
                    Console.WriteLine($"[WALL RESULT] phase=attempt_upgrade cycle={_debug.Cycle} candidate_match_count={candidateMatchCount} verified_count=0 status=skip reason=unvalidated");
                    return WallTransactionResult.Skip("unvalidated").WithCandidateMatchCount(candidateMatchCount);
                }

                using Mat? currentScreenshot = _adb.TakeScreenshot();
                if (currentScreenshot == null || currentScreenshot.Empty())
                {
                    _navigator.BestEffortDismiss();
                    return WallTransactionResult.Skip("screenshot_failed").WithCandidateMatchCount(candidateMatchCount);
                }

                // Trích xuất tài nguyên hiện tại và chi phí một bức tường
                (int currentGold, int currentElixir, _) = IsTarget.ExtractHomeResources(_adb, _vision);
                int detectedGoldCost = _vision.ExtractNumericalMetrics(currentScreenshot, WallUiLayout.GoldUpgradeCostRoi);
                int detectedElixirCost = _vision.ExtractNumericalMetrics(currentScreenshot, WallUiLayout.ElixirUpgradeCostRoi);

                WallCostValidationResult costValidation = WallCostPolicy.ValidateWallCosts(detectedGoldCost, detectedElixirCost);
                if (!costValidation.IsValid)
                {
                    Console.WriteLine($"[WALL RESULT] phase=cost_ocr cycle={_debug.Cycle} gold_cost={detectedGoldCost} elixir_cost={detectedElixirCost} status=skip reason={costValidation.Reason}");
                    _navigator.BestEffortDismiss();
                    return WallTransactionResult.Skip(costValidation.Reason).WithCandidateMatchCount(candidateMatchCount);
                }

                int singleWallCost = costValidation.Cost;

                // Quyết định tài nguyên bằng WallUpgradeDecider
                var decisionInput = new WallUpgradeDecisionInput(
                    WallCost: singleWallCost,
                    Gold: currentGold,
                    Elixir: currentElixir,
                    GoldStartThreshold: wallGoldThreshold,
                    ElixirStartThreshold: wallElixirThreshold,
                    GoldReserve: wallGoldReserve,
                    ElixirReserve: wallElixirReserve,
                    BatchLimit: safeBatchLimit);

                WallUpgradeDecision decision = WallUpgradeDecider.Decide(decisionInput);
                if (decision.Resource == WallUpgradeResource.None || decision.RequestedCount <= 0)
                {
                    Console.WriteLine($"[WALL RESULT] phase=decider_check cycle={_debug.Cycle} gold={currentGold:N0} elixir={currentElixir:N0} cost={singleWallCost:N0} status=skip reason={decision.SkipReason}");
                    _navigator.BestEffortDismiss();
                    return WallTransactionResult.Skip(decision.SkipReason).WithCandidateMatchCount(candidateMatchCount);
                }

                string selectedResource = decision.Resource == WallUpgradeResource.Gold ? "gold" : "elixir";
                bool costIsRed = WallCostPolicy.IsUpgradeCostRed(currentScreenshot, selectedResource, out _, out _);
                bool btnAvailable = WallPanelInspector.IsResourceUpgradeButtonAvailable(currentScreenshot, selectedResource);

                if (!btnAvailable || costIsRed)
                {
                    Console.WriteLine($"[WALL RESULT] phase=attempt_upgrade cycle={_debug.Cycle} resource={selectedResource} status=skip reason=resource_button_unavailable_or_red");
                    _navigator.BestEffortDismiss();
                    return WallTransactionResult.Skip("resource_button_unavailable_or_red").WithCandidateMatchCount(candidateMatchCount);
                }

                int actualSelectedCount = AddWallsSafely(selectedResource, decision.RequestedCount, safeBatchLimit, token);
                if (actualSelectedCount <= 0)
                {
                    _navigator.BestEffortDismiss();
                    return WallTransactionResult.Skip("insufficient_resource_for_cost").WithCandidateMatchCount(candidateMatchCount);
                }

                _debug.Capture("add_wall_done");

                int resourceBefore = selectedResource.Equals("gold", StringComparison.OrdinalIgnoreCase) ? currentGold : currentElixir;

                Point upgradePoint = WallUiLayout.UpgradePointFor(selectedResource);

                _adb.Tap(upgradePoint.X, upgradePoint.Y);
                if (InterruptibleSleep(1000, token)) return WallTransactionResult.Skip("cancelled");

                if (!_inspector.IsConfirmDialogOpen())
                {
                    Console.WriteLine($"[WALL RESULT] phase=confirm_open cycle={_debug.Cycle} resource={selectedResource} candidate_match_count={candidateMatchCount} requested_count={actualSelectedCount} verified_count=0 status=skip reason=confirm_dialog_not_verified");
                    _navigator.BestEffortDismiss();
                    return WallTransactionResult.Skip("confirm_dialog_not_verified").WithCandidateMatchCount(candidateMatchCount);
                }

                _debug.Capture("confirm_open");

                Point confirmPoint = actualSelectedCount > 1 ? WallUiLayout.ConfirmMultiPoint : WallUiLayout.ConfirmUpgradePoint;
                _adb.Tap(confirmPoint.X, confirmPoint.Y);

                if (InterruptibleSleep(1500, token))
                {
                    _navigator.BestEffortDismiss();
                    return new WallTransactionResult(0, "cancelled_post_confirm", Resource: selectedResource, CandidateMatchCount: candidateMatchCount, RequestedCount: actualSelectedCount);
                }

                if (!_inspector.IsConfirmDialogClosed())
                {
                    Console.WriteLine($"[WALL RESULT] phase=confirm_verify cycle={_debug.Cycle} resource={selectedResource} status=unknown reason=dialog_still_open");
                    _navigator.BestEffortDismiss();
                    return new WallTransactionResult(0, "outcome_unknown", Resource: selectedResource, CandidateMatchCount: candidateMatchCount, RequestedCount: actualSelectedCount);
                }

                // Poll đọc lại tài nguyên sau confirm tối đa 3 lần (mỗi lần 250ms) để chờ thanh tài nguyên cập nhật xong
                int resourceAfter = 0;
                long expectedSpend = (long)singleWallCost * actualSelectedCount;
                long actualSpend = 0;
                bool deltaOk = false;

                for (int poll = 0; poll < 3; poll++)
                {
                    (int goldAfter, int elixirAfter, _) = IsTarget.ExtractHomeResources(_adb, _vision);
                    resourceAfter = selectedResource.Equals("gold", StringComparison.OrdinalIgnoreCase) ? goldAfter : elixirAfter;
                    if (resourceAfter > 0)
                    {
                        actualSpend = (long)resourceBefore - resourceAfter;
                        if (WallCostPolicy.IsResourceDeltaVerified(resourceBefore, resourceAfter, expectedSpend))
                        {
                            deltaOk = true;
                            break;
                        }
                    }
                    Thread.Sleep(250);
                }

                _navigator.BestEffortDismiss();

                if (resourceAfter > 0 && deltaOk)
                {
                    int totalCost = (int)actualSpend > 0 ? (int)actualSpend : (int)expectedSpend;
                    Console.WriteLine($"[WALL RESULT] phase=attempt_upgrade cycle={_debug.Cycle} resource={selectedResource} candidate_match_count={candidateMatchCount} requested_count={actualSelectedCount} verified_count={actualSelectedCount} cost={totalCost:N0} status=upgraded reason=verified");
                    return WallTransactionResult.Verified(selectedResource, actualSelectedCount, totalCost, candidateMatchCount, actualSelectedCount);
                }
                else
                {
                    string reason = resourceAfter <= 0 ? "post_confirm_resource_unreadable" : "resource_delta_mismatch";
                    Console.WriteLine($"[WALL RESULT] phase=confirm_verify cycle={_debug.Cycle} resource={selectedResource} status=unknown reason={reason} before={resourceBefore:N0} after={resourceAfter:N0} expectedSpend={expectedSpend:N0} actualSpend={actualSpend:N0}");
                    return new WallTransactionResult(0, reason, Resource: selectedResource, CandidateMatchCount: candidateMatchCount, RequestedCount: actualSelectedCount);
                }
            }
            finally
            {
                _navigator.BestEffortDismiss();
            }
        }

        /// <summary>
        /// Bấm nút +1 từng bước, dừng ngay khi chi phí chuyển đỏ hoặc vùng chi phí không đổi (đã chạm trần).
        /// </summary>
        private int AddWallsSafely(string resource, int requestedCount, int batchLimit, CancellationToken token)
        {
            int targetCount = Math.Clamp(requestedCount, 1, Math.Clamp(batchLimit, 1, 10));
            int selectedCount = 1;
            int addMoreTaps = targetCount - 1;
            if (addMoreTaps <= 0) return 1;

            Rect costRoi = WallUiLayout.CostRoiFor(resource);

            for (int i = 0; i < addMoreTaps; i++)
            {
                if (token.IsCancellationRequested) break;

                using Mat? beforeScreenshot = _adb.TakeScreenshot();
                if (beforeScreenshot == null || beforeScreenshot.Empty())
                {
                    Console.WriteLine($"[WALL] phase=add_wall resource={resource} status=stop reason=before_screenshot_failed");
                    break;
                }

                _adb.Tap(WallUiLayout.AddWallPlusOneButton.X, WallUiLayout.AddWallPlusOneButton.Y);
                if (InterruptibleSleep(WallUiLayout.WallUiAnimationDelayMs, token)) break;

                using Mat? afterScreenshot = _adb.TakeScreenshot();
                if (afterScreenshot == null || afterScreenshot.Empty())
                {
                    Console.WriteLine($"[WALL] phase=add_wall resource={resource} status=stop reason=after_screenshot_failed");
                    break;
                }

                if (WallCostPolicy.IsUpgradeCostRed(afterScreenshot, resource, out _, out _))
                {
                    _adb.Tap(WallUiLayout.RemoveWallMinusOneButton.X, WallUiLayout.RemoveWallMinusOneButton.Y);
                    InterruptibleSleep(WallUiLayout.WallUiAnimationDelayMs, token);
                    break;
                }

                Rect clamped = ImageUtils.ClampRect(costRoi, afterScreenshot.Width, afterScreenshot.Height);
                if (clamped.Width <= 0 || clamped.Height <= 0)
                {
                    Console.WriteLine($"[WALL] phase=add_wall resource={resource} status=stop reason=invalid_roi");
                    break;
                }

                using Mat beforeCost = new Mat(beforeScreenshot, clamped);
                using Mat afterCost = new Mat(afterScreenshot, clamped);
                using Mat diff = new Mat();
                Cv2.Absdiff(beforeCost, afterCost, diff);
                Scalar meanDiff = Cv2.Mean(diff);
                double diffVal = meanDiff.Val0 + meanDiff.Val1 + meanDiff.Val2;

                if (diffVal < 3.0)
                {
                    Console.WriteLine($"[WALL] phase=add_wall resource={resource} status=stop reason=cost_region_unchanged diff={diffVal:F2}");
                    break;
                }

                selectedCount++;
            }

            return selectedCount;
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
