using System;
using OpenCvSharp;

namespace CvAut
{
    internal enum BuilderAvailabilityState
    {
        Available,
        Busy,
        Unknown
    }

    internal sealed record BuilderAvailabilityResult(
        BuilderAvailabilityState State,
        int? FreeBuilders,
        int? TotalBuilders,
        double Confidence,
        double IconScore,
        string Reason);

    /// <summary>
    /// Read-only Main Village preflight. The regular-builder icon distinguishes normal
    /// builders from the Goblin Builder, while OCR determines whether a normal builder is free.
    /// Any ambiguous state fails closed.
    /// </summary>
    internal sealed class MainVillageBuilderAvailabilityDetector
    {
        private const string RegularBuilderTemplate = @"ui\open_upgrade2";
        private readonly IVisionEngine _vision;

        public MainVillageBuilderAvailabilityDetector(IVisionEngine vision)
        {
            _vision = vision;
        }

        public BuilderAvailabilityResult Detect(Mat? screenshot)
        {
            if (screenshot == null || screenshot.Empty())
            {
                return Unknown("screenshot_failed");
            }

            Rect iconRoi = ImageUtils.ClampRect(
                WallUiLayout.RegularBuilderIconRoi,
                screenshot.Width,
                screenshot.Height);
            if (iconRoi.Width <= 0 || iconRoi.Height <= 0)
            {
                return Unknown("builder_icon_roi_invalid");
            }

            Point? regularBuilderIcon = _vision.FindElement(
                screenshot,
                RegularBuilderTemplate,
                WallUiLayout.RegularBuilderIconThreshold,
                iconRoi,
                out double iconScore);

            // In the captured Goblin Builder state (1/7), open_upgrade2 is replaced.
            // Do not infer that a missing icon is definitely Goblin Builder: a covered or
            // changed header is also possible. Both cases must skip wall upgrades.
            if (regularBuilderIcon == null)
            {
                return new BuilderAvailabilityResult(
                    BuilderAvailabilityState.Unknown,
                    null,
                    null,
                    0,
                    iconScore,
                    "regular_builder_icon_missing");
            }

            Rect countRoi = ImageUtils.ClampRect(
                WallUiLayout.BuilderCountRoi,
                screenshot.Width,
                screenshot.Height);
            if (countRoi.Width <= 0 || countRoi.Height <= 0)
            {
                return Unknown("builder_count_roi_invalid", iconScore);
            }

            if (!TryReadBuilderCount(
                    screenshot,
                    countRoi,
                    out int freeBuilders,
                    out int totalBuilders,
                    out double confidence))
            {
                return Unknown("builder_count_unreadable", iconScore);
            }

            if (confidence < WallUiLayout.BuilderCountMinimumConfidence)
            {
                return Unknown("builder_count_low_confidence", iconScore, confidence);
            }

            if (freeBuilders <= 0)
            {
                return new BuilderAvailabilityResult(
                    BuilderAvailabilityState.Busy,
                    freeBuilders,
                    totalBuilders,
                    confidence,
                    iconScore,
                    "no_free_builder");
            }

            return new BuilderAvailabilityResult(
                BuilderAvailabilityState.Available,
                freeBuilders,
                totalBuilders,
                confidence,
                iconScore,
                "regular_builder_available");
        }

        private bool TryReadBuilderCount(
            Mat screenshot,
            Rect roi,
            out int freeBuilders,
            out int totalBuilders,
            out double confidence)
        {
            bool rgbRead = _vision.TryExtractNumericalMetrics(
                screenshot,
                roi,
                out int rgbRaw,
                out double rgbConfidence,
                useRgbThresh: true);
            bool grayRead = _vision.TryExtractNumericalMetrics(
                screenshot,
                roi,
                out int grayRaw,
                out double grayConfidence);

            return TryResolveBuilderCount(
                rgbRead ? rgbRaw : null,
                rgbConfidence,
                grayRead ? grayRaw : null,
                grayConfidence,
                out freeBuilders,
                out totalBuilders,
                out confidence);
        }

        internal static bool TryResolveBuilderCount(
            int? rgbRaw,
            double rgbConfidence,
            int? grayRaw,
            double grayConfidence,
            out int freeBuilders,
            out int totalBuilders,
            out double confidence)
        {
            freeBuilders = 0;
            totalBuilders = 0;
            confidence = 0;

            int rgbFree = 0;
            int rgbTotal = 0;
            int grayFree = 0;
            int grayTotal = 0;
            bool rgbValid = rgbRaw.HasValue
                && TryParseBuilderCount(rgbRaw.Value, out rgbFree, out rgbTotal);
            bool grayValid = grayRaw.HasValue
                && TryParseBuilderCount(grayRaw.Value, out grayFree, out grayTotal);

            if (rgbValid && grayValid)
            {
                if (rgbFree != grayFree || rgbTotal != grayTotal) return false;
                freeBuilders = rgbFree;
                totalBuilders = rgbTotal;
                confidence = Math.Max(rgbConfidence, grayConfidence);
                return true;
            }

            if (rgbValid)
            {
                freeBuilders = rgbFree;
                totalBuilders = rgbTotal;
                confidence = rgbConfidence;
                return true;
            }

            if (grayValid)
            {
                freeBuilders = grayFree;
                totalBuilders = grayTotal;
                confidence = grayConfidence;
                return true;
            }

            // The current digit OCR reads the wide Supercell '0' as either 8 or 6.
            // If both preprocessing paths independently agree on the total digit and
            // both produce an impossible free>total pair, treat it as 0/total. This is
            // fail-closed: the only resulting action is to skip the wall transaction.
            if (rgbRaw.HasValue
                && grayRaw.HasValue
                && TryInferZeroFree(rgbRaw.Value, grayRaw.Value, out int inferredTotal))
            {
                freeBuilders = 0;
                totalBuilders = inferredTotal;
                confidence = Math.Min(rgbConfidence, grayConfidence);
                return true;
            }

            return false;
        }

        private static bool TryInferZeroFree(int rgbRaw, int grayRaw, out int totalBuilders)
        {
            totalBuilders = 0;
            if (rgbRaw < 10 || rgbRaw > 99 || grayRaw < 10 || grayRaw > 99) return false;

            int rgbFree = rgbRaw / 10;
            int rgbTotal = rgbRaw % 10;
            int grayFree = grayRaw / 10;
            int grayTotal = grayRaw % 10;

            if (rgbTotal <= 0 || rgbTotal != grayTotal) return false;
            if (rgbFree <= rgbTotal || grayFree <= grayTotal) return false;

            totalBuilders = rgbTotal;
            return true;
        }

        /// <summary>
        /// Digit OCR removes '/'. Thus 1/6 becomes 16. A leading zero is also removed,
        /// so 0/3 becomes 3 and must be interpreted as zero free out of three total.
        /// </summary>
        internal static bool TryParseBuilderCount(int raw, out int freeBuilders, out int totalBuilders)
        {
            freeBuilders = 0;
            totalBuilders = 0;
            if (raw <= 0 || raw > 99) return false;

            if (raw < 10)
            {
                totalBuilders = raw;
                return totalBuilders > 0;
            }

            freeBuilders = raw / 10;
            totalBuilders = raw % 10;
            return totalBuilders > 0 && freeBuilders <= totalBuilders;
        }

        private static BuilderAvailabilityResult Unknown(
            string reason,
            double iconScore = 0,
            double confidence = 0)
            => new(
                BuilderAvailabilityState.Unknown,
                null,
                null,
                confidence,
                iconScore,
                reason);
    }
}
