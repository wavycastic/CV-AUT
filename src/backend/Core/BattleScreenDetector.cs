using System;
using System.Linq;
using OpenCvSharp;

namespace CvAut
{
    /// <summary>
    /// Bộ phát hiện trạng thái trận đấu (Active Battle vs Result Screen) dùng chung cho CV-AUT.
    /// Kiểm tra preflight nút End Battle (qua template matching và contour button-shape màu đỏ)
    /// để tránh nhận diện nhầm màn hình kết quả khi lính vẫn đang tấn công.
    /// </summary>
    internal static class BattleScreenDetector
    {
        private static readonly Rect EndBattleButtonRoi = new(20, 670, 180, 70);

        /// <summary>
        /// Kiểm tra xem màn hình hiện tại có nút "End Battle" (Trận đánh đang diễn ra) hay không.
        /// </summary>
        public static bool IsActiveBattlePresent(ADBHelper adb, VisionEngine vision, Mat screenshot, out double endBattleScore)
        {
            endBattleScore = 0;
            if (screenshot == null || screenshot.Empty()) return false;

            // 1. Khớp mẫu template ui\end_battle.png trong ScoutUiRoi
            Point? endBtn = vision.FindElement(screenshot, @"ui\end_battle.png", AutomationThresholds.ScoutUiThreshold, AutomationRoiConstants.ScoutUiRoi, out endBattleScore);
            if (endBtn.HasValue)
            {
                return true;
            }

            // 2. Kiểm tra hình dạng nút bấm màu đỏ ở góc dưới bên trái (ROI: 20, 670, 180, 70)
            Rect roi = ImageUtils.ClampRect(EndBattleButtonRoi, screenshot.Width, screenshot.Height);
            if (roi.Width <= 0 || roi.Height <= 0) return false;

            using Mat roiBgr = new Mat(screenshot, roi);
            using Mat redMask = new Mat(roiBgr.Size(), MatType.CV_8UC1, new Scalar(0));

            int redPixelCount = 0;
            for (int y = 0; y < roiBgr.Rows; y++)
            {
                for (int x = 0; x < roiBgr.Cols; x++)
                {
                    Vec3b px = roiBgr.At<Vec3b>(y, x);
                    byte b = px.Item0;
                    byte g = px.Item1;
                    byte r = px.Item2;

                    // Màu đỏ tươi của nút End Battle: R > 160, G < 90, B < 90, lệch R so với G/B > 60
                    if (r > 160 && g < 90 && b < 90 && (r - g) > 60 && (r - b) > 60)
                    {
                        redMask.Set<byte>(y, x, 255);
                        redPixelCount++;
                    }
                }
            }

            double redRatio = redPixelCount / (double)(roi.Width * roi.Height);
            if (redPixelCount < 400 || redRatio < 0.035)
            {
                endBattleScore = 0;
                return false;
            }

            // Tìm contour kiểm tra hình dạng chữ nhật của nút bấm (tránh vệt đỏ hoặc nhiễu rải rác)
            Cv2.FindContours(redMask, out Point[][] contours, out _, RetrievalModes.External, ContourApproximationModes.ApproxSimple);
            bool hasButtonShape = contours.Any(contour =>
            {
                Rect r = Cv2.BoundingRect(contour);
                double area = Cv2.ContourArea(contour);
                return r.Width >= 45 &&
                       r.Height >= 18 &&
                       r.Width > (r.Height * 1.2) &&
                       area >= 400;
            });

            if (hasButtonShape)
            {
                endBattleScore = Math.Min(0.99, Math.Max(0.70, redRatio * 5.0));
                return true;
            }

            endBattleScore = 0;
            return false;
        }
    }
}
