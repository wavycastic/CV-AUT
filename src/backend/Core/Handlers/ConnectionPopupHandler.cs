using System;
using System.IO;
using System.Threading;
using OpenCvSharp;

namespace CvAut.Handlers
{
    /// <summary>
    /// Xử lý phát hiện và giải tỏa các popup lỗi mạng, mất kết nối, tài khoản đăng nhập từ thiết bị khác.
    /// </summary>
    internal class ConnectionPopupHandler
    {
        private readonly ADBHelper _adb;
        private readonly VisionEngine _vision;
        private readonly string _templatesPath;
        private bool _handlingConnectionPopup;

        public ConnectionPopupHandler(ADBHelper adb, VisionEngine vision, string templatesPath)
        {
            _adb = adb;
            _vision = vision;
            _templatesPath = templatesPath;
        }

        public bool IsHandling => _handlingConnectionPopup;

        public bool ConnectionPopupVisible(out string matchInfo, bool allowDialogShapeFallback = true, bool disableDialogShapeFallback = false)
        {
            matchInfo = string.Empty;
            using Mat? screenshot = _adb.TakeScreenshot();
            if (screenshot == null) return false;

            return ConnectionPopupVisible(screenshot, out matchInfo, allowDialogShapeFallback, disableDialogShapeFallback);
        }

        public bool ConnectionPopupVisible(Mat screenshot, out string matchInfo, bool allowDialogShapeFallback = true, bool disableDialogShapeFallback = false)
        {
            matchInfo = string.Empty;

            foreach (string templateName in AutomationThresholds.ConnectionPopupTemplates)
            {
                string fullPath = Path.Combine(_templatesPath, templateName);
                if (!File.Exists(fullPath)) continue;

                bool isLegacyConnectionTemplate = templateName.Equals(@"ui\conn.png", StringComparison.OrdinalIgnoreCase);
                double threshold = isLegacyConnectionTemplate
                    ? AutomationThresholds.LegacyConnectionPopupThreshold
                    : AutomationThresholds.ConnectionPopupThreshold;
                Rect? popupRoi = isLegacyConnectionTemplate ? null : AutomationRoiConstants.ConnectionPopupRoi;

                if (_vision.TryFindTemplate(screenshot, fullPath, popupRoi, threshold, out Point p, out double score))
                {
                    matchInfo = $"template={templateName} score={score:F3} pos=({p.X},{p.Y})";
                    return true;
                }
            }

            if (allowDialogShapeFallback && !disableDialogShapeFallback)
            {
                if (IsGenericConnectionPopupDialogShape(screenshot, out string shapeDetails))
                {
                    matchInfo = shapeDetails;
                    return true;
                }
            }

            return false;
        }

        public bool HandleBlockingConnectionPopup(string warningMessage, Action? reloadAction = null, bool disableDialogShapeFallback = false)
        {
            if (_handlingConnectionPopup || !ConnectionPopupVisible(out string matchInfo, allowDialogShapeFallback: true, disableDialogShapeFallback: disableDialogShapeFallback))
            {
                return false;
            }

            _handlingConnectionPopup = true;
            Console.WriteLine($"{warningMessage} details=\"{matchInfo}\"");

            try
            {
                if (reloadAction != null)
                {
                    reloadAction();
                }
                else
                {
                    _adb.Tap(800, 500); // Bấm OK mặc định ở giữa màn hình
                    Thread.Sleep(3000);
                }
                return true;
            }
            finally
            {
                _handlingConnectionPopup = false;
            }
        }

        private bool IsGenericConnectionPopupDialogShape(Mat screenshot, out string shapeDetails)
        {
            shapeDetails = string.Empty;
            if (screenshot == null || screenshot.Empty()) return false;

            Rect roi = GetCenteredConnectionPopupRoi(screenshot.Width, screenshot.Height);
            using Mat cropped = new Mat(screenshot, roi);
            using Mat hsv = new Mat();
            Cv2.CvtColor(cropped, hsv, ColorConversionCodes.BGR2HSV);

            using Mat maskDark = new Mat();
            using Mat maskGold = new Mat();
            Cv2.InRange(hsv, new Scalar(0, 0, 10), new Scalar(180, 255, 75), maskDark);
            Cv2.InRange(hsv, new Scalar(10, 100, 100), new Scalar(35, 255, 255), maskGold);

            double totalPixels = cropped.Width * cropped.Height;
            double darkRatio = Cv2.CountNonZero(maskDark) / totalPixels;
            double goldRatio = Cv2.CountNonZero(maskGold) / totalPixels;

            if (darkRatio > 0.40 && goldRatio > 0.01)
            {
                shapeDetails = $"dialog_shape_fallback darkRatio={darkRatio:F2} goldRatio={goldRatio:F2}";
                return true;
            }

            return false;
        }

        private static Rect GetCenteredConnectionPopupRoi(int width, int height)
        {
            int roiWidth = (int)(width * 0.55);
            int roiHeight = (int)(height * 0.60);
            int roiX = (width - roiWidth) / 2;
            int roiY = (height - roiHeight) / 2;
            return new Rect(roiX, roiY, roiWidth, roiHeight);
        }
    }
}
