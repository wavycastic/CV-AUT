using System;
using OpenCvSharp;

namespace CvAut
{
    /// <summary>
    /// Recognises UI state during the wall upgrade flow: the builder panel,
    /// the upgrade panel, the confirmation dialog and the state of the resource buttons.
    /// </summary>
    internal sealed class WallPanelInspector
    {
        private readonly IADBHelper _adb;
        private readonly IVisionEngine _vision;

        public WallPanelInspector(IADBHelper adb, IVisionEngine vision)
        {
            _adb = adb;
            _vision = vision;
        }

        /// <summary>Whether the builder suggestions panel is open (based on the ratio of dark pixels in the ROI).</summary>
        public bool IsBuilderMenuOpen() => IsBuilderMenuOpen(out _);

        public bool IsBuilderMenuOpen(out double darkRatio, string trigger = "unknown", string? runId = null, int cycle = 0)
        {
            darkRatio = 0;
            using Mat? screenshot = _adb.TakeScreenshot();
            if (screenshot == null || screenshot.Empty())
            {
                Console.WriteLine("[WALL RESULT] phase=preflight status=fail reason=screenshot_failed");
                WallLogger.LogInfo("builder_menu_check", "fail", reason: "screenshot_failed", cycle: cycle, trigger: trigger, runId: runId);
                return false;
            }
            Rect safeRoi = ImageUtils.ClampRect(WallUiLayout.BuilderUpgradeMenuRoi, screenshot.Width, screenshot.Height);
            if (safeRoi.Width <= 0 || safeRoi.Height <= 0)
            {
                Console.WriteLine($"[WALL RESULT] phase=preflight status=fail reason=empty_builder_roi width={screenshot.Width} height={screenshot.Height}");
                WallLogger.LogInfo("builder_menu_check", "fail", reason: "empty_builder_roi", cycle: cycle, trigger: trigger, runId: runId, extra: $"layout_width={screenshot.Width} layout_height={screenshot.Height}");
                return false;
            }
            using Mat menu = new Mat(screenshot, safeRoi);
            using Mat gray = new Mat();
            using Mat dark = new Mat();
            Cv2.CvtColor(menu, gray, ColorConversionCodes.BGR2GRAY);
            Cv2.Threshold(gray, dark, 80, 255, ThresholdTypes.BinaryInv);
            darkRatio = Cv2.CountNonZero(dark) / (double)(dark.Rows * dark.Cols);
            bool open = darkRatio >= 0.55;
            Console.WriteLine($"[WALL] phase=preflight_check status={(open ? "ok" : "fail")} dark_ratio={darkRatio:F2} reason={(open ? "builder_menu_panel_visible" : "builder_menu_panel_missing")}");
            WallLogger.LogInfo("builder_menu_check", open ? "ok" : "fail", reason: open ? "builder_menu_panel_visible" : "builder_menu_panel_missing", cycle: cycle, trigger: trigger, runId: runId, extra: $"dark_ratio={darkRatio:F2}");
            return open;
        }

        /// <summary>Validates that the wall upgrade panel is open using dynamic resource button localization.</summary>
        public bool ValidateWallPanelOpen(out bool goldAvailable, out bool elixirAvailable) => ValidateWallPanelOpen(out goldAvailable, out elixirAvailable, out _);

        public bool ValidateWallPanelOpen(out bool goldAvailable, out bool elixirAvailable, out bool whitePanel, string trigger = "unknown", string? runId = null, int cycle = 0)
        {
            goldAvailable = false;
            elixirAvailable = false;
            whitePanel = false;

            using Mat? screenshot = _adb.TakeScreenshot();
            if (screenshot == null || screenshot.Empty())
            {
                Console.WriteLine("[WALL] phase=validate_tap status=fail reason=screenshot_failed");
                WallLogger.LogInfo("validate_panel", "fail", reason: "screenshot_failed", cycle: cycle, trigger: trigger, runId: runId);
                return false;
            }

            int width = screenshot.Width;
            int height = screenshot.Height;
            int px = Math.Clamp((int)(width * (800.0 / 1600.0)), 0, width - 1);
            int py = Math.Clamp((int)(height * (750.0 / 900.0)), 0, height - 1);
            Vec3b pixel = screenshot.At<Vec3b>(py, px);
            whitePanel = pixel.Item0 >= 180 && pixel.Item1 >= 180 && pixel.Item2 >= 180;

            var panelLocal = WallDynamicLocalizer.LocalizePanelAndButtons(_vision, screenshot);

            goldAvailable = IsResourceUpgradeButtonAvailable(screenshot, panelLocal.GoldInfo);
            elixirAvailable = IsResourceUpgradeButtonAvailable(screenshot, panelLocal.ElixirInfo);

            bool panelOpen = whitePanel || goldAvailable || elixirAvailable;
            if (!panelOpen)
            {
                Console.WriteLine("[WALL] phase=validate_tap status=fail reason=panel_not_open");
                WallLogger.LogInfo("validate_panel", "fail", reason: "panel_not_open", cycle: cycle, trigger: trigger, runId: runId, extra: $"white_panel={whitePanel} gold_avail={goldAvailable} elixir_avail={elixirAvailable}");
                return false;
            }

            Console.WriteLine($"[WALL] phase=validate_tap status=ok reason=panel_open gold={goldAvailable} elixir={elixirAvailable}");
            WallLogger.LogInfo("validate_panel", "ok", reason: "panel_open", cycle: cycle, trigger: trigger, runId: runId, extra: $"white_panel={whitePanel} gold_avail={goldAvailable} elixir_avail={elixirAvailable}");
            return true;
        }

        /// <summary>Takes a fresh screenshot and checks whether the cost label is red.</summary>
        public bool IsUpgradeCostRed(string resource, out double redRatio, out int redPixels, WallResourceButtonInfo? buttonInfo = null)
        {
            redRatio = 0;
            redPixels = 0;
            using Mat? screenshot = _adb.TakeScreenshot();
            if (screenshot == null || screenshot.Empty())
            {
                Console.WriteLine($"[WALL] phase=color_check resource={resource} status=fail reason=screenshot_failed");
                return true;
            }

            Rect costRoi = (buttonInfo != null && buttonInfo.Found)
                ? buttonInfo.CostRoi
                : WallUiLayout.CostRoiFor(resource);

            bool red = WallCostPolicy.IsUpgradeCostRed(screenshot, costRoi, out redRatio, out redPixels);
            Console.WriteLine($"[WALL] phase=color_check resource={resource} status=ok reason={(red ? "cost_red" : "cost_available")} red_ratio={redRatio:F3} red_pixels={redPixels}");
            return red;
        }

        /// <summary>Whether the confirmation dialog is open (based on normalized dialog ROI average brightness).</summary>
        public bool IsConfirmDialogOpen() => IsConfirmDialogOpen(out _);

        public bool IsConfirmDialogOpen(out double brightness, string trigger = "unknown", string? runId = null, int cycle = 0)
        {
            brightness = 0;
            using Mat? screenshot = _adb.TakeScreenshot();
            if (screenshot == null || screenshot.Empty())
            {
                WallLogger.LogInfo("confirm_open_check", "fail", reason: "screenshot_failed", cycle: cycle, trigger: trigger, runId: runId);
                return false;
            }
            return IsConfirmDialogOpen(screenshot, out brightness, trigger, runId, cycle);
        }

        public bool IsConfirmDialogOpen(Mat screenshot, out double brightness, string trigger = "unknown", string? runId = null, int cycle = 0)
        {
            brightness = 0;
            if (screenshot == null || screenshot.Empty()) return false;

            Rect roi = WallDynamicLocalizer.GetNormalizedConfirmDialogRoi(screenshot.Width, screenshot.Height);
            if (roi.Width <= 0 || roi.Height <= 0)
            {
                WallLogger.LogInfo("confirm_open_check", "fail", reason: "empty_confirm_roi", cycle: cycle, trigger: trigger, runId: runId);
                return false;
            }

            using Mat dialog = new Mat(screenshot, roi);
            Scalar mean = Cv2.Mean(dialog);
            brightness = (mean.Val0 + mean.Val1 + mean.Val2) / 3.0;
            WallConfirmDialogInfo info = WallConfirmDialogInspector.Inspect(screenshot);
            bool open = info.Found;
            Console.WriteLine($"[WALL] phase=confirm_open status={(open ? "ok" : "fail")} brightness={brightness:F1} kind={info.Kind} reason={info.Reason}");
            WallLogger.LogInfo("confirm_open_check", open ? "ok" : "fail", reason: info.Reason, cycle: cycle, trigger: trigger, runId: runId, extra: $"dialog_brightness={brightness:F1} dialog_open={open} confirm_kind={info.Kind}");
            return open;
        }

        /// <summary>Whether the confirmation dialog has fully closed.</summary>
        public bool IsConfirmDialogClosed() => IsConfirmDialogClosed(out _);

        public bool IsConfirmDialogClosed(out double brightness, string trigger = "unknown", string? runId = null, int cycle = 0)
        {
            brightness = 0;
            using Mat? screenshot = _adb.TakeScreenshot();
            if (screenshot == null || screenshot.Empty())
            {
                WallLogger.LogInfo("confirm_close_check", "fail", reason: "screenshot_failed", cycle: cycle, trigger: trigger, runId: runId);
                return false;
            }

            Rect roi = WallDynamicLocalizer.GetNormalizedConfirmDialogRoi(screenshot.Width, screenshot.Height);
            if (roi.Width <= 0 || roi.Height <= 0)
            {
                WallLogger.LogInfo("confirm_close_check", "fail", reason: "empty_confirm_roi", cycle: cycle, trigger: trigger, runId: runId);
                return false;
            }

            using Mat dialog = new Mat(screenshot, roi);
            Scalar mean = Cv2.Mean(dialog);
            brightness = (mean.Val0 + mean.Val1 + mean.Val2) / 3.0;
            WallConfirmDialogInfo info = WallConfirmDialogInspector.Inspect(screenshot);
            bool closed = !info.Found;
            WallLogger.LogInfo("confirm_close_check", closed ? "ok" : "fail", reason: closed ? "dialog_closed" : "dialog_still_open", cycle: cycle, trigger: trigger, runId: runId, extra: $"dialog_brightness={brightness:F1} dialog_closed={closed} confirm_kind={info.Kind}");
            return closed;
        }

        /// <summary>Whether the upgrade button for the given resource is lit up (affordable).</summary>
        public static bool IsResourceUpgradeButtonAvailable(Mat screenshot, WallResourceButtonInfo? buttonInfo)
        {
            if (buttonInfo == null || !buttonInfo.Found)
            {
                return false;
            }

            Point point = buttonInfo.TapPoint;
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

        /// <summary>Only the calibrated resolution is supported; a different screen size skips the whole cycle.</summary>
        public static bool ValidateSupportedLayout(Mat? screenshot, int cycle, out string reason) => ValidateSupportedLayout(screenshot, cycle, out reason, "unknown", null);

        public static bool ValidateSupportedLayout(Mat? screenshot, int cycle, out string reason, string trigger = "unknown", string? runId = null)
        {
            if (screenshot == null || screenshot.Empty())
            {
                reason = "screenshot_failed";
                Console.WriteLine($"[WALL RESULT] phase=layout cycle={cycle} status=skip reason={reason}");
                WallLogger.LogInfo("preflight_layout", "skip", reason: reason, cycle: cycle, trigger: trigger, runId: runId);
                return false;
            }
            if (screenshot.Width != WallUiLayout.SupportedScreenshotWidth || screenshot.Height != WallUiLayout.SupportedScreenshotHeight)
            {
                reason = "unsupported_screen_layout";
                Console.WriteLine($"[WALL RESULT] phase=layout cycle={cycle} status=skip reason={reason} width={screenshot.Width} height={screenshot.Height} supported_width={WallUiLayout.SupportedScreenshotWidth} supported_height={WallUiLayout.SupportedScreenshotHeight}");
                WallLogger.LogInfo("preflight_layout", "skip", reason: reason, cycle: cycle, trigger: trigger, runId: runId, extra: $"layout_width={screenshot.Width} layout_height={screenshot.Height} supported_width={WallUiLayout.SupportedScreenshotWidth} supported_height={WallUiLayout.SupportedScreenshotHeight}");
                return false;
            }
            reason = "supported_screen_layout";
            Console.WriteLine($"[WALL] phase=layout cycle={cycle} status=ok reason={reason} width={screenshot.Width} height={screenshot.Height}");
            WallLogger.LogInfo("preflight_layout", "ok", reason: reason, cycle: cycle, trigger: trigger, runId: runId, extra: $"layout_width={screenshot.Width} layout_height={screenshot.Height} supported_width={WallUiLayout.SupportedScreenshotWidth} supported_height={WallUiLayout.SupportedScreenshotHeight}");
            return true;
        }
    }
}
