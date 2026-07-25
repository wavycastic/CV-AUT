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

        public WallPanelInspector(IADBHelper adb)
        {
            _adb = adb;
        }

        /// <summary>Whether the builder suggestions panel is open (based on the ratio of dark pixels in the ROI).</summary>
        public bool IsBuilderMenuOpen()
        {
            using Mat? screenshot = _adb.TakeScreenshot();
            if (screenshot == null || screenshot.Empty())
            {
                Console.WriteLine("[WALL RESULT] phase=preflight status=fail reason=screenshot_failed");
                return false;
            }
            Rect safeRoi = ImageUtils.ClampRect(WallUiLayout.BuilderUpgradeMenuRoi, screenshot.Width, screenshot.Height);
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

        /// <summary>Validates that the wall upgrade panel is open and reads the state of both resource buttons.</summary>
        public bool ValidateWallPanelOpen(out bool goldAvailable, out bool elixirAvailable)
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
            int px = Math.Clamp(WallUiLayout.PanelCheckPoint.X, 0, width - 1);
            int py = Math.Clamp(WallUiLayout.PanelCheckPoint.Y, 0, height - 1);
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

        /// <summary>Takes a fresh screenshot and checks whether the cost label is red.</summary>
        public bool IsUpgradeCostRed(string resource, out double redRatio, out int redPixels)
        {
            redRatio = 0;
            redPixels = 0;
            using Mat? screenshot = _adb.TakeScreenshot();
            if (screenshot == null || screenshot.Empty())
            {
                Console.WriteLine($"[WALL] phase=color_check resource={resource} status=fail reason=screenshot_failed");
                return true;
            }
            bool red = WallCostPolicy.IsUpgradeCostRed(screenshot, resource, out redRatio, out redPixels);
            Console.WriteLine($"[WALL] phase=color_check resource={resource} status=ok reason={(red ? "cost_red" : "cost_available")} red_ratio={redRatio:F3} red_pixels={redPixels}");
            return red;
        }

        /// <summary>Whether the confirmation dialog is open (based on average brightness).</summary>
        public bool IsConfirmDialogOpen()
        {
            using Mat? screenshot = _adb.TakeScreenshot();
            if (screenshot == null || screenshot.Empty()) return false;
            Rect roi = ImageUtils.ClampRect(WallUiLayout.ConfirmDialogRoi, screenshot.Width, screenshot.Height);
            if (roi.Width <= 0 || roi.Height <= 0) return false;

            using Mat dialog = new Mat(screenshot, roi);
            Scalar mean = Cv2.Mean(dialog);
            double brightness = (mean.Val0 + mean.Val1 + mean.Val2) / 3.0;
            bool open = brightness >= 70;
            Console.WriteLine($"[WALL] phase=confirm_open status={(open ? "ok" : "fail")} brightness={brightness:F1} reason={(open ? "dialog_visible" : "dialog_not_visible")}");
            return open;
        }

        /// <summary>Whether the confirmation dialog has fully closed.</summary>
        public bool IsConfirmDialogClosed()
        {
            using Mat? screenshot = _adb.TakeScreenshot();
            if (screenshot == null || screenshot.Empty()) return false;
            Rect roi = ImageUtils.ClampRect(WallUiLayout.ConfirmDialogRoi, screenshot.Width, screenshot.Height);
            if (roi.Width <= 0 || roi.Height <= 0) return false;

            using Mat dialog = new Mat(screenshot, roi);
            Scalar mean = Cv2.Mean(dialog);
            double brightness = (mean.Val0 + mean.Val1 + mean.Val2) / 3.0;
            return brightness < 60;
        }

        /// <summary>Whether the upgrade button for the given resource is lit up (affordable).</summary>
        public static bool IsResourceUpgradeButtonAvailable(Mat screenshot, string resource)
        {
            Point point = WallUiLayout.UpgradePointFor(resource);
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
        public static bool ValidateSupportedLayout(Mat? screenshot, int cycle, out string reason)
        {
            if (screenshot == null || screenshot.Empty())
            {
                reason = "screenshot_failed";
                Console.WriteLine($"[WALL RESULT] phase=layout cycle={cycle} status=skip reason={reason}");
                return false;
            }
            if (screenshot.Width != WallUiLayout.SupportedScreenshotWidth || screenshot.Height != WallUiLayout.SupportedScreenshotHeight)
            {
                reason = "unsupported_screen_layout";
                Console.WriteLine($"[WALL RESULT] phase=layout cycle={cycle} status=skip reason={reason} width={screenshot.Width} height={screenshot.Height} supported_width={WallUiLayout.SupportedScreenshotWidth} supported_height={WallUiLayout.SupportedScreenshotHeight}");
                return false;
            }
            reason = "supported_screen_layout";
            Console.WriteLine($"[WALL] phase=layout cycle={cycle} status=ok reason={reason} width={screenshot.Width} height={screenshot.Height}");
            return true;
        }
    }
}
