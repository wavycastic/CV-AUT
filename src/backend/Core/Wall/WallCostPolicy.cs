using System;
using OpenCvSharp;

namespace CvAut
{
    /// <summary>Kết quả kiểm tra chi phí nâng cấp tường đọc được từ OCR.</summary>
    internal sealed record WallCostValidationResult(bool IsValid, int Cost, string Reason);

    /// <summary>
    /// Các quy tắc thuần tuý (không chạm ADB) để kiểm tra chi phí nâng cấp tường:
    /// đối chiếu chi phí Vàng/Dầu hồng, xác minh tài nguyên đã trừ sau confirm, và phát hiện chi phí bị tô đỏ.
    /// </summary>
    internal static class WallCostPolicy
    {
        /// <summary>
        /// Đối chiếu hai giá trị chi phí OCR. Nếu cả hai đọc được thì tỉ lệ lệch không được vượt maxMismatchRatio.
        /// Luôn chọn giá trị lớn hơn để an toàn.
        /// </summary>
        internal static WallCostValidationResult ValidateWallCosts(int goldCost, int elixirCost, double maxMismatchRatio = WallUiLayout.MaxCostMismatchRatio)
        {
            if (goldCost <= 0 && elixirCost <= 0)
            {
                return new WallCostValidationResult(false, 0, "wall_cost_unreadable");
            }

            if (goldCost > 0 && elixirCost > 0)
            {
                double ratio = (double)Math.Max(goldCost, elixirCost) / Math.Min(goldCost, elixirCost);
                if (ratio > maxMismatchRatio)
                {
                    return new WallCostValidationResult(false, 0, "wall_cost_mismatch");
                }
                return new WallCostValidationResult(true, Math.Max(goldCost, elixirCost), "ok");
            }

            return new WallCostValidationResult(true, Math.Max(goldCost, elixirCost), "ok");
        }

        /// <summary>Xác minh tài nguyên thực sự bị trừ đúng khoảng kỳ vọng sau khi xác nhận giao dịch.</summary>
        internal static bool IsResourceDeltaVerified(long resourceBefore, long resourceAfter, long expectedSpend, long tolerance = 0)
        {
            if (resourceAfter <= 0 || resourceBefore <= 0) return false;
            long actualSpend = resourceBefore - resourceAfter;
            if (tolerance <= 0)
            {
                tolerance = Math.Max(20_000, expectedSpend / 10);
            }
            return actualSpend >= (expectedSpend - tolerance) && actualSpend <= (expectedSpend + tolerance);
        }

        /// <summary>Đếm số điểm ảnh đỏ trong ROI chi phí để biết tài nguyên có đủ hay không.</summary>
        internal static bool IsUpgradeCostRed(Mat screenshot, string resource, out double redRatio, out int redPixels)
        {
            redRatio = 0;
            redPixels = 0;
            Rect sourceRoi = WallUiLayout.CostRoiFor(resource);
            Rect roi = ImageUtils.ClampRect(sourceRoi, screenshot.Width, screenshot.Height);
            if (roi.Width <= 0 || roi.Height <= 0)
            {
                return true;
            }
            using Mat cost = new Mat(screenshot, roi);
            for (int y = 0; y < cost.Rows; y++)
            {
                for (int x = 0; x < cost.Cols; x++)
                {
                    Vec3b pixel = cost.At<Vec3b>(y, x);
                    byte b = pixel.Item0;
                    byte g = pixel.Item1;
                    byte r = pixel.Item2;
                    bool isRed = r > 200 && g < 160 && b < 160 && (r - g) > 50 && (r - b) > 50;
                    if (isRed)
                    {
                        redPixels++;
                    }
                }
            }
            redRatio = redPixels / (double)(roi.Width * roi.Height);
            return redPixels >= WallUiLayout.RedCostPixelCountThreshold;
        }
    }
}
