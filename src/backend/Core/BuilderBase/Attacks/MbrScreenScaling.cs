using System;
using OpenCvSharp;
using Point = OpenCvSharp.Point;

namespace CvAut
{
    internal static class MbrScreenScaling
    {
        public static double ScaleX(Mat screenshot) => screenshot.Width / 860.0;

        public static int ScaleY(Mat screenshot, int mbrY) => (int)Math.Round(mbrY * (screenshot.Height / 732.0));

        public static int ScaleYDistance(Mat screenshot, int pixels) => Math.Max(1, (int)Math.Round(pixels * (screenshot.Height / 732.0)));

        public static Point ScaleMbrPoint(int x, int y, int imageWidth, int imageHeight)
        {
            return new Point(
                (int)Math.Round(x * (imageWidth / 860.0)),
                (int)Math.Round(y * (imageHeight / 732.0)));
        }

        public static Rect ScaleMbrRect(int left, int top, int right, int bottom, int imageWidth, int imageHeight)
        {
            Point tl = ScaleMbrPoint(left, top, imageWidth, imageHeight);
            Point br = ScaleMbrPoint(right, bottom, imageWidth, imageHeight);
            return ImageUtils.ClampRect(Rect.FromLTRB(tl.X, tl.Y, br.X, br.Y), imageWidth, imageHeight);
        }
    }
}
