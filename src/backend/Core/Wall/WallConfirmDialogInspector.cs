using System;
using System.Collections.Generic;
using System.Linq;
using OpenCvSharp;
using Point = OpenCvSharp.Point;

namespace CvAut
{
    public enum WallConfirmationKind { None, SingleConfirm, MultiCancelOkay }

    internal sealed record WallConfirmDialogInfo(WallConfirmationKind Kind, bool Found, Rect ConfirmButton, Point ConfirmPoint, Rect CancelButton, double Score, string Reason);

    internal static class WallConfirmDialogInspector
    {
        public static WallConfirmDialogInfo Inspect(Mat screenshot)
        {
            if (screenshot == null || screenshot.Empty()) return None("screenshot_invalid");
            Rect search = ImageUtils.ClampRect(new Rect(
                (int)(screenshot.Width * 0.35), (int)(screenshot.Height * 0.48),
                (int)(screenshot.Width * 0.48), (int)(screenshot.Height * 0.48)), screenshot.Width, screenshot.Height);
            using Mat crop = new(screenshot, search);
            using Mat hsv = new();
            Cv2.CvtColor(crop, hsv, ColorConversionCodes.BGR2HSV);

            List<Rect> green = FindButtons(hsv, new Scalar(35, 65, 55), new Scalar(95, 255, 255), search);
            List<Rect> orange = FindButtons(hsv, new Scalar(5, 90, 70), new Scalar(30, 255, 255), search);
            Rect confirm = green.OrderByDescending(Area).FirstOrDefault();
            if (confirm.Width == 0) return None("green_confirm_missing");

            Rect cancel = orange.Where(o => o.X < confirm.X && Math.Abs((o.Y + o.Height / 2) - (confirm.Y + confirm.Height / 2)) < screenshot.Height * 0.10)
                .OrderByDescending(Area).FirstOrDefault();
            if (cancel.Width > 0)
                return new(WallConfirmationKind.MultiCancelOkay, true, confirm, Center(confirm), cancel, 1.0, "cancel_okay_pair_verified");

            return new(WallConfirmationKind.SingleConfirm, true, confirm, Center(confirm), default, 0.85, "single_confirm_verified");
        }

        private static List<Rect> FindButtons(Mat hsv, Scalar low, Scalar high, Rect offset)
        {
            using Mat mask = new();
            Cv2.InRange(hsv, low, high, mask);
            using Mat kernel = Cv2.GetStructuringElement(MorphShapes.Rect, new Size(9, 5));
            Cv2.MorphologyEx(mask, mask, MorphTypes.Close, kernel);
            Cv2.FindContours(mask, out Point[][] contours, out _, RetrievalModes.External, ContourApproximationModes.ApproxSimple);
            return contours.Select(Cv2.BoundingRect)
                .Where(r => r.Width >= 70 && r.Height >= 25 && r.Width <= 350 && r.Height <= 160)
                .Select(r => new Rect(r.X + offset.X, r.Y + offset.Y, r.Width, r.Height)).ToList();
        }

        private static double Area(Rect r) => r.Width * r.Height;
        private static Point Center(Rect r) => new(r.X + r.Width / 2, r.Y + r.Height / 2);
        private static WallConfirmDialogInfo None(string reason) => new(WallConfirmationKind.None, false, default, default, default, 0, reason);
    }
}
