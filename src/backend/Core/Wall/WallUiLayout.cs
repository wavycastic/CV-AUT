using System;
using OpenCvSharp;
using Point = OpenCvSharp.Point;

namespace CvAut
{
    /// <summary>
    /// All calibrated coordinates, ROIs and thresholds for the wall upgrade UI (1600x900 resolution).
    /// Ported from legacy/NX-ClashClient rois.json; do not change these values without recalibrating on a real device.
    /// </summary>
    internal static class WallUiLayout
    {
        // ROI used to search for walls inside the builder menu.
        // Expanded from the legacy crop because 1600x900 runtime labels begin near x=623
        // and the lower visible Wall row reaches y=630; the old ROI clipped both labels.
        internal static readonly Rect BuilderUpgradeMenuRoi = new(590, 90, 450, 570);
        // Probe point for the light grey/white panel background that confirms the upgrade panel is open
        internal static readonly Point PanelCheckPoint = new(800, 750);
        // Builder suggestions button at the top center
        internal static readonly Point BuilderMenuPoint = new(738, 36);
        // Regular-builder icon and x/y builder counter in the Main Village header.
        // Calibrated from 1600x900 screenshots and the legacy normalized builders_icon ROI.
        internal static readonly Rect RegularBuilderIconRoi = new(700, 10, 110, 90);
        internal static readonly Rect BuilderCountRoi = new(790, 29, 65, 30);
        // Safe point at the edge of the map, tapped to dismiss leftover menus/popups
        internal static readonly Point HomeMenuPoint = new(140, 606);
        // Swipe coordinates used to scroll the builder suggestions panel
        internal static readonly Point RetrySwipeStart = new(977, 157);
        internal static readonly Point RetrySwipeEnd = new(999, 432);
        // Tap points for navigating the wall upgrade UI
        internal static readonly Point DismissPoint = new(1143, 209);
        internal static readonly Point FixedGoldUpgradePoint = new(920, 707);
        internal static readonly Point FixedElixirUpgradePoint = new(1095, 702);
        internal static readonly Point AddWallPlusOneButton = new(660, 650);
        internal static readonly Point RemoveWallMinusOneButton = new(330, 650);
        // Cost capsules on the Level 15 wall panel. The legacy ROIs were about 50 px too far left
        // and clipped most of the 100,000 label, producing false OCR values such as 66 and 896.
        internal static readonly Rect GoldUpgradeCostRoi = new(910, 630, 135, 35);
        internal static readonly Rect ElixirUpgradeCostRoi = new(1085, 630, 135, 35);
        internal static readonly Point ConfirmUpgradePoint = new(1115, 782);
        internal static readonly Point ConfirmMultiPoint = new(990, 620);
        internal static readonly Rect ConfirmDialogRoi = new(820, 540, 430, 300);

        internal const int WallUiAnimationDelayMs = 400;
        internal const int RedCostPixelCountThreshold = 120;
        // Template match threshold for finding walls (kept high to avoid matching other objects)
        internal const double WallSearchThreshold = 0.90;
        internal const double RegularBuilderIconThreshold = 0.95;
        internal const double BuilderCountMinimumConfidence = 0.60;
        internal const int SwipeDurationMs = 600;
        internal const double MaxCostMismatchRatio = 1.15;
        internal const double WallCostOcrThreshold = 242;
        // Second, slightly lower gray threshold used for a redundant consensus OCR pass on the wall cost.
        internal const double WallCostConsensusThreshold = 225;
        internal const int SupportedScreenshotWidth = 1600;
        internal const int SupportedScreenshotHeight = 900;
        internal const int MaxCandidateAttempts = 3;

        /// <summary>Returns the cost ROI that matches the given resource type.</summary>
        internal static Rect CostRoiFor(string resource)
            => resource.Equals("gold", StringComparison.OrdinalIgnoreCase)
                ? GoldUpgradeCostRoi
                : ElixirUpgradeCostRoi;

        /// <summary>Returns the upgrade button tap point that matches the given resource type.</summary>
        internal static Point UpgradePointFor(string resource)
            => resource.Equals("gold", StringComparison.OrdinalIgnoreCase)
                ? FixedGoldUpgradePoint
                : FixedElixirUpgradePoint;
    }
}
