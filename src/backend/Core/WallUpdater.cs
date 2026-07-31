using System;
using System.Collections.Generic;
using System.Threading;
using OpenCvSharp;
using Point = OpenCvSharp.Point;

namespace CvAut
{
    /// <summary>
    /// Wall Updater - orchestrates the wall upgrade flow:
    /// - Scans wall candidates through WallCandidateScanner.
    /// - Picks and validates one candidate through WallCandidateSelector.
    /// - Decides between Gold and Elixir with WallUpgradeDecider, checking the OCR cost with WallCostPolicy.
    /// - Grows the batch through WallQuantityAdjuster.
    /// - Verifies the resource delta after the transaction is confirmed.
    /// </summary>
    internal sealed partial class WallUpdater
    {
        private readonly IADBHelper _adb;
        private readonly IVisionEngine _vision;
        private readonly WallMenuNavigator _navigator;
        private readonly WallPanelInspector _inspector;
        private readonly WallCandidateScanner _scanner;
        private readonly WallCandidateSelector _selector;
        private readonly WallQuantityAdjuster _quantityAdjuster;
        private readonly WallDebugRecorder _debug;
        private readonly MainVillageBuilderAvailabilityDetector _builderDetector;

        public WallUpdater(IADBHelper adb, IVisionEngine vision, string templatesPath)
        {
            _adb = adb;
            _vision = vision;
            _navigator = new WallMenuNavigator(adb);
            _inspector = new WallPanelInspector(adb);
            _scanner = new WallCandidateScanner(adb, templatesPath, _inspector, _navigator);
            _debug = new WallDebugRecorder(adb);
            _selector = new WallCandidateSelector(adb, _scanner, _inspector, _navigator, _debug);
            _quantityAdjuster = new WallQuantityAdjuster(adb);
            _builderDetector = new MainVillageBuilderAvailabilityDetector(vision);
        }

        /// <summary>Scans wall locations on an existing screenshot; delegates to WallCandidateScanner.</summary>
        public List<Point> ScanWallLocations(Mat screenshot) => _scanner.ScanWallLocations(screenshot);

        private static bool InterruptibleSleep(int milliseconds, CancellationToken token)
            => ThreadingUtil.InterruptibleSleep(milliseconds, token);

        internal static WallCostValidationResult ValidateWallCosts(int goldCost, int elixirCost, double maxMismatchRatio = WallUiLayout.MaxCostMismatchRatio)
            => WallCostPolicy.ValidateWallCosts(goldCost, elixirCost, maxMismatchRatio);

        internal static bool IsResourceDeltaVerified(long resourceBefore, long resourceAfter, long expectedSpend, long tolerance = 0)
            => WallCostPolicy.IsResourceDeltaVerified(resourceBefore, resourceAfter, expectedSpend, tolerance);

        internal static bool IsUpgradeCostRed(Mat screenshot, string resource, out double redRatio, out int redPixels)
            => WallCostPolicy.IsUpgradeCostRed(screenshot, resource, out redRatio, out redPixels);

        /// <summary>
        /// Handles wall upgrades independently of the wall level.
        /// Scans the builder menu with the 4 generic wall templates, reads the current resources, runs the numbers through WallUpgradeDecider and performs the upgrade.
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

            BuilderAvailabilityResult builder = _builderDetector.Detect(initialScreenshot);
            Console.WriteLine(
                $"[WALL] phase=builder_preflight cycle={cycle} state={builder.State.ToString().ToLowerInvariant()} " +
                $"free_builders={builder.FreeBuilders?.ToString() ?? "unknown"} " +
                $"total_builders={builder.TotalBuilders?.ToString() ?? "unknown"} " +
                $"confidence={builder.Confidence:F2} icon_score={builder.IconScore:F3} reason={builder.Reason}");

            if (builder.State != BuilderAvailabilityState.Available)
            {
                Console.WriteLine($"[WALL RESULT] phase=builder_preflight cycle={cycle} status=skip reason={builder.Reason}");
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

            try
            {
                WallCandidateSelection selection = _selector.SelectValidatedCandidate(token);
                candidateMatchCount = selection.CandidateMatchCount;

                if (selection.SkipReason is string skipReason)
                {
                    return string.Equals(skipReason, "cancelled", StringComparison.Ordinal)
                        ? WallTransactionResult.Skip("cancelled")
                        : WallTransactionResult.Skip(skipReason).WithCandidateMatchCount(candidateMatchCount);
                }

                using Mat? currentScreenshot = _adb.TakeScreenshot();
                if (currentScreenshot == null || currentScreenshot.Empty())
                {
                    _navigator.BestEffortDismiss();
                    return WallTransactionResult.Skip("screenshot_failed").WithCandidateMatchCount(candidateMatchCount);
                }

                // Extract the current resources and the cost of a single wall
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

                // Pick the resource with WallUpgradeDecider
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

                int actualSelectedCount = _quantityAdjuster.AddWallsSafely(selectedResource, decision.RequestedCount, safeBatchLimit, token);
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

                // Re-read the resources after confirming, polling up to 3 times (250 ms each) to let the resource bar finish updating
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

        public void ResetSavedOffset()
        {
            _selector.ResetSavedOffset();
        }
    }
}
