using System;
using OpenCvSharp;
using Point = OpenCvSharp.Point;

namespace CvAut
{
    internal static class AttackBarBannerReader
    {
        public static bool IsSlotAlreadyDeployedByBanner(Mat screenshot, Point center, bool secondAttack)
        {
            int bannerX = center.X + (int)Math.Round(34 * MbrScreenScaling.ScaleX(screenshot));
            int bannerY = MbrScreenScaling.ScaleY(screenshot, 585);
            if (!TryGetPixel(screenshot, bannerX, bannerY, out Vec3b topPixel)) return false;

            if (IsColorNear(topPixel, 0x7B7B7B, 12)) return true;
            if (IsColorNear(topPixel, 0xCA49FF, 30)) return true;
            if (IsColorNear(topPixel, 0x12244B, 30)) return true;

            if (secondAttack)
            {
                if (IsColorNear(topPixel, 0xD77AFF, 30)) return true;
                if (IsColorNear(topPixel, 0x15274A, 30)) return true;
            }

            if (!TryGetPixel(screenshot, bannerX, bannerY - 15, out Vec3b deployedPixel)) return false;
            return IsColorNear(deployedPixel, 0xCA49FF, 30)
                || IsColorNear(deployedPixel, 0x232323, 10)
                || IsColorNear(deployedPixel, 0x4482FE, 30)
                || IsColorNear(deployedPixel, 0x3E7BFF, 30);
        }

        public static bool TryReadMbrBannerState(Mat screenshot, int bannerX, int bannerY, bool remaining, bool secondAttack, IVisionEngine vision, out bool readable, out int count, out string state)
        {
            readable = false;
            count = 0;
            state = "missing_pixel";
            if (!TryGetPixel(screenshot, bannerX, bannerY, out Vec3b pixel)) return false;
            TryGetPixel(screenshot, bannerX, bannerY - MbrScreenScaling.ScaleYDistance(screenshot, 15), out Vec3b deployedPixel);

            bool grey = IsColorNear(pixel, 0x7B7B7B, 10);
            bool violetDeployed = IsColorNear(deployedPixel, 0xCA49FF, 30);
            bool darkDeployed = IsColorNear(deployedPixel, 0x232323, 10);
            if (remaining && (grey || violetDeployed || darkDeployed))
            {
                if (grey && ReadSlotCountAtBanner(screenshot, bannerX, bannerY, vision) > 0)
                {
                    state = "grey_with_count";
                }
                else
                {
                    state = grey ? "grey_deployed" : violetDeployed ? "violet_deployed" : "dark_deployed";
                    return true;
                }
            }

            bool violet = IsColorNear(pixel, 0xCA4AFF, 30) || IsColorNear(pixel, 0xD77AFF, 30);
            bool giantViolet = IsColorNear(pixel, 0x12244B, 30) || IsColorNear(pixel, 0x15274A, 30);
            bool blue = IsColorNear(pixel, remaining ? 0x4482FE : 0x3E7BFF, 30) || IsColorNear(pixel, 0x4482FE, 30);
            if (!violet && secondAttack) violet = giantViolet;

            if (blue)
            {
                count = ReadSlotCountAtBanner(screenshot, bannerX, bannerY, vision);
                readable = count > 0;
                state = readable ? "blue_count" : "blue_ocr_empty";
                return true;
            }

            if (violet || giantViolet)
            {
                count = 1;
                readable = true;
                state = violet ? "violet_one" : "giant_violet_one";
                return true;
            }

            state = $"unknown_color_{pixel.Item2:X2}{pixel.Item1:X2}{pixel.Item0:X2}";
            return true;
        }

        public static int ReadSlotCount(Mat screenshot, Point center, IVisionEngine vision)
        {
            // Clamp against the real frame, not the 1600x900 layout constants: the rest of the
            // attack flow works in the MBR 860x732 space, so the frame is often a different size.
            Rect roi = ImageUtils.ClampRect(
                Rect.FromLTRB(center.X + 8, center.Y - 45, center.X + 48, center.Y - 8),
                screenshot.Width,
                screenshot.Height);
            if (roi.Width <= 0 || roi.Height <= 0) return 1;

            if (vision.TryExtractNumericalMetrics(screenshot, roi, out int value, out _, useRgbThresh: true)
                && value > 0 && value <= 20)
            {
                return value;
            }

            if (vision.TryExtractNumericalMetrics(screenshot, roi, out value, out _)
                && value > 0 && value <= 20)
            {
                return value;
            }

            return 1;
        }

        public static int ReadSlotCountAtBanner(Mat screenshot, int bannerX, int bannerY, IVisionEngine vision)
        {
            Rect roi = Rect.FromLTRB(bannerX, bannerY - MbrScreenScaling.ScaleYDistance(screenshot, 14), bannerX + (int)Math.Round(31 * MbrScreenScaling.ScaleX(screenshot)), bannerY + MbrScreenScaling.ScaleYDistance(screenshot, 8));
            roi = ImageUtils.ClampRect(roi, screenshot.Width, screenshot.Height);
            if (roi.Width <= 0 || roi.Height <= 0) return 0;
            if (vision.TryExtractNumericalMetrics(screenshot, roi, out int value, out _, useRgbThresh: true) && value > 0 && value <= 20) return value;
            if (vision.TryExtractNumericalMetrics(screenshot, roi, out value, out _) && value > 0 && value <= 20) return value;
            return 0;
        }

        public static bool TryGetPixel(Mat image, int x, int y, out Vec3b pixel)
        {
            pixel = default;
            if (x < 0 || y < 0 || x >= image.Width || y >= image.Height) return false;
            pixel = image.At<Vec3b>(y, x);
            return true;
        }

        public static bool IsColorNear(Vec3b pixel, int rgb, int tolerance)
        {
            int r = (rgb >> 16) & 0xFF;
            int g = (rgb >> 8) & 0xFF;
            int b = rgb & 0xFF;
            return Math.Abs(pixel.Item2 - r) <= tolerance
                && Math.Abs(pixel.Item1 - g) <= tolerance
                && Math.Abs(pixel.Item0 - b) <= tolerance;
        }
    }
}
