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
        // ROI used to search for walls inside the builder menu
        internal static readonly Rect BuilderUpgradeMenuRoi = new(646, 107, 347, 474);
        // Probe point for the light grey/white panel background that confirms the upgrade panel is open
        internal static readonly Point PanelCheckPoint = new(800, 750);
        // Builder suggestions button at the top center
        internal static readonly Point BuilderMenuPoint = new(738, 36);
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
        internal static readonly Rect GoldUpgradeCostRoi = new(860, 635, 120, 33);
        internal static readonly Rect ElixirUpgradeCostRoi = new(1035, 635, 120, 33);
        internal static readonly Point ConfirmUpgradePoint = new(1115, 782);
        internal static readonly Point ConfirmMultiPoint = new(990, 620);
        internal static readonly Rect ConfirmDialogRoi = new(820, 540, 430, 300);

        internal const int WallUiAnimationDelayMs = 400;
        internal const int RedCostPixelCountThreshold = 120;
        // Template match threshold for finding walls (kept high to avoid matching other objects)
        internal const double WallSearchThreshold = 0.90;
        internal const int SwipeDurationMs = 600;
        internal const double MaxCostMismatchRatio = 1.15;
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
