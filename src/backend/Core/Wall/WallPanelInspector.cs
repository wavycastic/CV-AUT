using System;
using OpenCvSharp;

namespace CvAut
{
    /// <summary>
    /// Nhận diện trạng thái giao diện trong luồng nâng cấp tường:
    /// bảng Thợ xây, bảng nâng cấp, hộp thoại xác nhận và tình trạng nút tài nguyên.
    /// </summary>
    internal sealed class WallPanelInspector
    {
        private readonly IADBHelper _adb;

        public WallPanelInspector(IADBHelper adb)
        {
            _adb = adb;
        }

        /// <summary>Bảng gợi ý Thợ xây có đang mở không (dựa trên tỉ lệ điểm ảnh tối trong ROI).</summary>
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

        /// <summary>Xác thực bảng nâng cấp tường đã mở và đọc tình trạng hai nút tài nguyên.</summary>
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

        /// <summary>Chụp màn hình mới rồi kiểm tra chi phí có bị tô đỏ không.</summary>
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

        /// <summary>Hộp thoại xác nhận có đang mở không (dựa trên độ sáng trung bình).</summary>
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

        /// <summary>Hộp thoại xác nhận đã đóng hẳn chưa.</summary>
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

        /// <summary>Nút nâng cấp bằng tài nguyên chỉ định có sáng (khả dụng) không.</summary>
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

        /// <summary>Chỉ hỗ trợ đúng độ phân giải đã hiệu chỉnh; sai kích thước thì bỏ qua toàn bộ chu kỳ.</summary>
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
