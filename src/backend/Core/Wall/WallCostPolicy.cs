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
        /// Cross-checks the two OCR cost values. A wall has the same price on its Gold and Elixir
        /// buttons, so spending is authorized only when both reads are identical and plausible.
        /// The tolerance parameter remains for source compatibility but is intentionally ignored.
        /// </summary>
        internal static WallCostValidationResult ValidateWallCosts(int goldCost, int elixirCost, double maxMismatchRatio = WallUiLayout.MaxCostMismatchRatio)
        {
            _ = maxMismatchRatio;
            if (goldCost <= 0 && elixirCost <= 0)
            {
                return new WallCostValidationResult(false, 0, "wall_cost_unreadable");
            }

            if (goldCost <= 0 || elixirCost <= 0)
            {
                return new WallCostValidationResult(false, 0, "wall_cost_pair_incomplete");
            }

            if (!IsPlausibleWallCost(goldCost) || !IsPlausibleWallCost(elixirCost))
            {
                return new WallCostValidationResult(false, 0, "wall_cost_implausible");
            }

            if (goldCost != elixirCost)
            {
                return new WallCostValidationResult(false, 0, "wall_cost_mismatch");
            }

            return new WallCostValidationResult(true, goldCost, "ok");
        }

        /// <summary>
        /// Validates a Gold-only wall cost. Levels 1 and 2 legitimately expose no Elixir
        /// upgrade button, so this path deliberately does not synthesize or require an Elixir read.
        /// </summary>
        internal static WallCostValidationResult ValidateGoldOnlyCost(int goldCost)
        {
            if (goldCost <= 0)
            {
                return new WallCostValidationResult(false, 0, "wall_gold_cost_unreadable");
            }
            if (!IsPlausibleGoldOnlyWallCost(goldCost))
            {
                return new WallCostValidationResult(false, 0, "wall_gold_cost_implausible");
            }
            return new WallCostValidationResult(true, goldCost, "ok_gold_only");
        }

        internal static bool IsPlausibleGoldOnlyWallCost(int value)
            // Levels 1-3 expose Gold-only upgrades in the supported UI.
            // Keep an exact whitelist so a partial OCR read cannot authorize a tap.
            => value is 1_000 or 5_000 or 10_000;

        internal static bool IsPlausibleWallCost(int value)
            // Exact supported prices for Gold/Elixir wall upgrades (levels 4-18).
            // A broad numeric range allowed clipped reads such as 1,500,000 -> 500,000.
            => value is 20_000 or 30_000 or 50_000 or 75_000 or 100_000 or
                200_000 or 500_000 or 1_000_000 or 1_500_000 or 2_000_000 or
                3_000_000 or 4_000_000 or 5_000_000 or 7_000_000 or 10_000_000;

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
        internal static bool IsUpgradeCostRed(Mat screenshot, Rect roi, out double redRatio, out int redPixels)
        {
            redRatio = 0;
            redPixels = 0;
            Rect clampedRoi = ImageUtils.ClampRect(roi, screenshot.Width, screenshot.Height);
            if (clampedRoi.Width <= 0 || clampedRoi.Height <= 0)
            {
                return true;
            }
            using Mat cost = new Mat(screenshot, clampedRoi);
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
            redRatio = redPixels / (double)(clampedRoi.Width * clampedRoi.Height);
            return redPixels >= WallUiLayout.RedCostPixelCountThreshold;
        }

        /// <summary>Counts red pixels inside the cost ROI to tell whether the player can afford the upgrade.</summary>
        internal static bool IsUpgradeCostRed(Mat screenshot, string resource, out double redRatio, out int redPixels)
        {
            return IsUpgradeCostRed(screenshot, WallUiLayout.CostRoiFor(resource), out redRatio, out redPixels);
        }
    }
}
