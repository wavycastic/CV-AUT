using System;
using System.Collections.Generic;
using System.Linq;
using OpenCvSharp;

namespace CvAut
{
    /// <summary>Reads a multi-wall total without applying the single-wall price whitelist.</summary>
    internal static class WallBatchTotalReader
    {
        internal const int MaximumSupportedTotal = 100_000_000;

        public static bool TryRead(IVisionEngine vision, Mat screenshot, Rect roi, out long value, out double confidence)
        {
            value = 0;
            confidence = 0;
            if (screenshot == null || screenshot.Empty()) return false;
            Rect safe = ImageUtils.ClampRect(roi, screenshot.Width, screenshot.Height);
            if (safe.Width <= 0 || safe.Height <= 0) return false;

            var reads = new List<(long Value, double Confidence)>();
            AddRead(vision, screenshot, safe, reads, useRgb: false);
            AddRead(vision, screenshot, safe, reads, useRgb: true);

            using Mat crop = new(screenshot, safe);
            using Mat gray = new();
            using Mat binary = new();
            using Mat binaryBgr = new();
            Cv2.CvtColor(crop, gray, ColorConversionCodes.BGR2GRAY);
            foreach (double threshold in new[] { 242d, 225d, 205d })
            {
                Cv2.Threshold(gray, binary, threshold, 255, ThresholdTypes.Binary);
                Cv2.CvtColor(binary, binaryBgr, ColorConversionCodes.GRAY2BGR);
                AddRead(vision, binaryBgr, new Rect(0, 0, binaryBgr.Width, binaryBgr.Height), reads, useRgb: false);
            }

            var valid = reads.Where(r => r.Value >= 1_000 && r.Value <= MaximumSupportedTotal).ToList();
            if (valid.Count == 0) return false;
            var winner = valid.GroupBy(r => r.Value)
                .Select(g => new { Value = g.Key, Count = g.Count(), Confidence = g.Max(x => x.Confidence) })
                .OrderByDescending(g => g.Count)
                .ThenByDescending(g => g.Confidence)
                .First();
            value = winner.Value;
            confidence = winner.Confidence;
            return winner.Count >= 2 || confidence >= 0.70;
        }

        public static bool Validate(long batchTotal, long singleWallCost, int selectedCount)
        {
            if (batchTotal <= 0 || singleWallCost <= 0 || selectedCount <= 0) return false;
            try { return checked(singleWallCost * selectedCount) == batchTotal; }
            catch (OverflowException) { return false; }
        }

        private static void AddRead(IVisionEngine vision, Mat image, Rect roi, List<(long Value, double Confidence)> reads, bool useRgb)
        {
            if (vision.TryExtractNumericalMetrics(image, roi, out int raw, out double conf, useRgbThresh: useRgb, allowVerticalShift: true) && raw > 0)
                reads.Add((raw, conf));
        }
    }
}
