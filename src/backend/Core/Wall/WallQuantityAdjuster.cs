using System;
using System.Linq;
using System.Threading;
using OpenCvSharp;

namespace CvAut
{
    /// <summary>
    /// Grows the wall batch one wall at a time by tapping the +1 button, stopping as soon as the
    /// cost turns red or the cost region stops changing (the cap has been reached).
    /// </summary>
    internal sealed class WallQuantityAdjuster
    {
        private readonly IADBHelper _adb;
        private readonly IVisionEngine? _vision;

        public WallQuantityAdjuster(IADBHelper adb) : this(adb, null)
        {
        }

        public WallQuantityAdjuster(IADBHelper adb, IVisionEngine? vision)
        {
            _adb = adb;
            _vision = vision;
        }

        public int AddWallsSafely(
            string resource,
            int requestedCount,
            int batchLimit,
            CancellationToken token,
            string trigger = "unknown",
            string? runId = null,
            int cycle = 0,
            WallResourceButtonInfo? buttonInfo = null,
            int singleWallCost = 0)
        {
            int targetCount = WallQuantityPlanner.ClampTarget(requestedCount, batchLimit);
            if (targetCount == 1) return 1;
            if (_vision == null || buttonInfo == null || !buttonInfo.Found || singleWallCost <= 0)
                return Stop("runtime_detector_dependencies_missing", resource, trigger, runId, cycle);

            using Mat? initial = _adb.TakeScreenshot();
            if (initial == null || initial.Empty()) return Stop("before_screenshot_failed", resource, trigger, runId, cycle);
            WallQuantityPanelInfo state = WallQuantityControlLocalizer.Localize(_vision, initial);
            if (!state.Header.Found) return Stop(state.Header.Reason, resource, trigger, runId, cycle);

            int selectedCount = state.Header.SelectedCount;
            if (state.Header.Mode == WallSelectionMode.Single)
            {
                WallQuantityControlInfo? gateway = state.Controls.SingleOrDefault(c => c.Role == WallQuantityControlRole.UpgradeMore);
                if (gateway == null || !gateway.Found || !gateway.Available)
                    return Stop("upgrade_more_not_localized_or_disabled", resource, trigger, runId, cycle);
                _adb.Tap(gateway.TapPoint.X, gateway.TapPoint.Y);
                if (ThreadingUtil.InterruptibleSleep(WallUiLayout.WallUiAnimationDelayMs, token)) return Stop("cancelled", resource, trigger, runId, cycle);

                using Mat? afterGateway = _adb.TakeScreenshot();
                if (afterGateway == null || afterGateway.Empty()) return Stop("gateway_screenshot_failed", resource, trigger, runId, cycle);
                WallPanelLocalizationResult gatewayPanel = WallDynamicLocalizer.LocalizePanelAndButtons(_vision, afterGateway);
                WallResourceButtonInfo gatewayResource = ResourceInfo(resource, gatewayPanel);
                if (!gatewayResource.Found ||
                    !WallBatchTotalReader.TryRead(_vision, afterGateway, gatewayResource.CostRoi, out long gatewayTotal, out _) ||
                    !WallBatchTotalReader.Validate(gatewayTotal, singleWallCost, 1))
                    return Stop("gateway_batch_total_not_verified", resource, trigger, runId, cycle);
                state = WallQuantityControlLocalizer.Localize(_vision, afterGateway, WallSelectionMode.Multi, 1);
                if (!state.Controls.Any(c => c.Role is WallQuantityControlRole.AddOne or WallQuantityControlRole.AddTen))
                    return Stop("gateway_quantity_controls_missing", resource, trigger, runId, cycle);
                selectedCount = 1;
            }

            while (selectedCount < targetCount)
            {
                if (token.IsCancellationRequested) return Stop("cancelled", resource, trigger, runId, cycle);
                using Mat? before = _adb.TakeScreenshot();
                if (before == null || before.Empty()) return Stop("before_screenshot_failed", resource, trigger, runId, cycle);
                state = WallQuantityControlLocalizer.Localize(_vision, before, WallSelectionMode.Multi, selectedCount);
                WallQuantityPlanStep step = WallQuantityPlanner.PlanNext(selectedCount, targetCount, state.Controls);
                if (!step.CanExecute)
                    return Stop(step.Reason, resource, trigger, runId, cycle);
                WallQuantityControlInfo control = state.Controls.Single(c => c.Role == step.Role);
                int expectedCount = step.ExpectedCount;
                WallLogger.LogInfo("quantity_plan", "ok", cycle: cycle, trigger: trigger, runId: runId,
                    extra: $"role={step.Role} delta={step.Delta} current_count={selectedCount} target_count={targetCount} expected_count={expectedCount} reason={step.Reason}");
                _adb.Tap(control.TapPoint.X, control.TapPoint.Y);
                if (ThreadingUtil.InterruptibleSleep(WallUiLayout.WallUiAnimationDelayMs, token)) return Stop("cancelled", resource, trigger, runId, cycle);
                using Mat? after = _adb.TakeScreenshot();
                if (after == null || after.Empty()) return Stop("after_screenshot_failed", resource, trigger, runId, cycle);

                WallHeaderInfo header = WallHeaderInspector.Inspect(_vision, after);
                if (!header.Found || header.Mode != WallSelectionMode.Multi || header.SelectedCount != expectedCount)
                    return Stop("header_delta_not_verified", resource, trigger, runId, cycle);
                WallPanelLocalizationResult afterPanel = WallDynamicLocalizer.LocalizePanelAndButtons(_vision, after);
                WallResourceButtonInfo afterResource = ResourceInfo(resource, afterPanel);
                if (!afterResource.Found ||
                    !WallBatchTotalReader.TryRead(_vision, after, afterResource.CostRoi, out long batchTotal, out _) ||
                    !WallBatchTotalReader.Validate(batchTotal, singleWallCost, expectedCount))
                    return Stop("batch_total_not_verified", resource, trigger, runId, cycle);
                selectedCount = expectedCount;
            }
            return selectedCount;
        }

        private static WallResourceButtonInfo ResourceInfo(string resource, WallPanelLocalizationResult panel)
            => resource.Equals("gold", StringComparison.OrdinalIgnoreCase) ? panel.GoldInfo : panel.ElixirInfo;

        private static int Stop(string reason, string resource, string trigger, string? runId, int cycle)
        {
            Console.WriteLine($"[WALL] phase=quantity_runtime resource={resource} status=stop reason={reason}");
            WallLogger.LogInfo("quantity_runtime", "stop", reason: reason, cycle: cycle, trigger: trigger, runId: runId, extra: $"resource={resource}");
            return 0;
        }
    }
}
