using System;
using OpenCvSharp;

namespace CvAut
{
    /// <summary>Result of validating the wall upgrade cost read through OCR.</summary>
    internal sealed record WallCostValidationResult(bool IsValid, int Cost, string Reason);

    /// <summary>
    /// Pure rules (no ADB access) for validating the wall upgrade cost: cross-checking the gold/elixir
    /// cost, verifying that resources were actually deducted after confirming, and detecting a red cost label.
    /// </summary>
    internal static class WallCostPolicy
    {
        /// <summary>
        /// Cross-checks the two OCR cost values. When both are readable their mismatch ratio must not exceed
        /// maxMismatchRatio. The larger value is always chosen, to stay on the safe side.
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

        /// <summary>Verifies that resources really dropped by roughly the expected amount after confirming the transaction.</summary>
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

        /// <summary>Counts red pixels inside the cost ROI to tell whether the player can afford the upgrade.</summary>
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
