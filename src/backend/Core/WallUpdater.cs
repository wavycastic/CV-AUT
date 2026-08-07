using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using OpenCvSharp;
using Point = OpenCvSharp.Point;

namespace CvAut
{
    /// <summary>
    /// Wall Updater - orchestrates the wall upgrade flow:
    /// - Scans wall candidates through WallCandidateScanner.
    /// - Picks and validates one candidate through WallCandidateSelector.
    /// - Decides between Gold and Elixir with WallUpgradeDecider, checking the OCR cost with WallCostPolicy.
    /// - Grows the batch through WallQuantityAdjuster.
    /// - Verifies the resource delta after the transaction is confirmed.
    /// </summary>
    internal sealed partial class WallUpdater
    {
        private readonly IADBHelper _adb;
        private readonly IVisionEngine _vision;
        private readonly WallMenuNavigator _navigator;
        private readonly WallPanelInspector _inspector;
        private readonly WallCandidateScanner _scanner;
        private readonly WallCandidateSelector _selector;
        private readonly WallQuantityAdjuster _quantityAdjuster;
        private readonly WallDebugRecorder _debug;
        private readonly MainVillageBuilderAvailabilityDetector _builderDetector;

        public WallUpdater(IADBHelper adb, IVisionEngine vision, string templatesPath)
        {
            _adb = adb;
            _vision = vision;
            _navigator = new WallMenuNavigator(adb);
            _inspector = new WallPanelInspector(adb, vision);
            _scanner = new WallCandidateScanner(adb, templatesPath, _inspector, _navigator);
            _debug = new WallDebugRecorder(adb);
            _selector = new WallCandidateSelector(adb, _scanner, _inspector, _navigator, _debug);
            _quantityAdjuster = new WallQuantityAdjuster(adb, vision);
            _builderDetector = new MainVillageBuilderAvailabilityDetector(vision);
        }

        /// <summary>Scans wall locations on an existing screenshot; delegates to WallCandidateScanner.</summary>
        public List<Point> ScanWallLocations(Mat screenshot) => _scanner.ScanWallLocations(screenshot);

        private static bool InterruptibleSleep(int milliseconds, CancellationToken token)
            => ThreadingUtil.InterruptibleSleep(milliseconds, token);

        internal static WallCostValidationResult ValidateWallCosts(int goldCost, int elixirCost, double maxMismatchRatio = WallUiLayout.MaxCostMismatchRatio)
            => WallCostPolicy.ValidateWallCosts(goldCost, elixirCost, maxMismatchRatio);

        internal static WallCostValidationResult ValidateGoldOnlyWallCost(int goldCost)
            => WallCostPolicy.ValidateGoldOnlyCost(goldCost);

        internal static bool IsResourceDeltaVerified(long resourceBefore, long resourceAfter, long expectedSpend, long tolerance = 0)
            => WallCostPolicy.IsResourceDeltaVerified(resourceBefore, resourceAfter, expectedSpend, tolerance);

        internal static bool IsUpgradeCostRed(Mat screenshot, string resource, out double redRatio, out int redPixels)
            => WallCostPolicy.IsUpgradeCostRed(screenshot, resource, out redRatio, out redPixels);

        /// <summary>
        /// Reads a wall upgrade cost from dark digits rendered on a light capsule. This preprocessing is
        /// intentionally wall-specific so the threshold used by resource/loot OCR remains unchanged.
        /// </summary>
        internal static bool TryReadWallUpgradeCost(IVisionEngine vision, Mat screenshot, Rect roi, out int value, out double confidence)
        {
            value = 0;
            confidence = 0;
            if (screenshot == null || screenshot.Empty()) return false;

            Rect safeRoi = ImageUtils.ClampRect(roi, screenshot.Width, screenshot.Height);
            if (safeRoi.Width <= 0 || safeRoi.Height <= 0) return false;

            if (TryReadWallCostConsensus(vision, screenshot, safeRoi, out value, out confidence))
            {
                return true;
            }

            // Contour candidates can capture different amounts of the resource-button border. When that
            // makes the standard ROI clip the top of the digits, retry one digit-height lower inside the
            // same button. This remains a local recovery; it cannot scan a different button.
            int downwardOffset = Math.Max(2, safeRoi.Height / 3);
            Rect loweredRoi = ImageUtils.ClampRect(
                new Rect(safeRoi.X, safeRoi.Y + downwardOffset, safeRoi.Width, safeRoi.Height),
                screenshot.Width,
                screenshot.Height);

            return loweredRoi != safeRoi &&
                   TryReadWallCostConsensus(vision, screenshot, loweredRoi, out value, out confidence);
        }

        /// <summary>
        /// Reads the same cost ROI through several algorithmically-independent preprocessing passes
        /// (two gray thresholds plus a per-channel RGB segmentation) and returns the value the passes
        /// agree on. Consensus removes random single-pass OCR errors and is discount-proof, because every
        /// pass reads the exact same digits. It cannot fix a systematic font misread shared by all passes;
        /// that stays the job of the gold==elixir cross-check and the post-purchase resource-delta check.
        /// </summary>
        private static bool TryReadWallCostConsensus(IVisionEngine vision, Mat screenshot, Rect roi, out int value, out double confidence)
        {
            value = 0;
            confidence = 0;

            // Million-value labels use a smaller font and are especially vulnerable to a
            // clipped leading digit (for example 1,500,000 being read as 500,000). Detect
            // the complete seven/eight-glyph sequence before trying the shorter labels.
            if (TryReadKnownMillionWallCost(vision, screenshot, roi, out value, out confidence))
            {
                return true;
            }

            // Verify the complete glyph sequence before trusting OCR. Small white labels can lose
            // their zero glyphs in OCR (for example 20,000 -> 20), while red labels use a separate
            // segmentation path. Reading both colors through the same topology check keeps mixed
            // affordability states symmetric and rejects clipped labels.
            if (TryReadKnownThousandsWallCost(vision, screenshot, roi, out value, out confidence))
            {
                return true;
            }

            bool p1 = TryReadWallUpgradeCostAtRoi(vision, screenshot, roi, WallUiLayout.WallCostOcrThreshold, out int v1, out double c1);
            bool p2 = TryReadWallUpgradeCostAtRoi(vision, screenshot, roi, WallUiLayout.WallCostConsensusThreshold, out int v2, out double c2);
            bool p3 = TryReadWallCostRgb(vision, screenshot, roi, out int v3, out double c3);
            bool p4 = TryReadWallCostRed(vision, screenshot, roi, out int v4, out double c4);

            var reads = new List<(int Value, double Confidence)>();
            if (p1) reads.Add((v1, c1));
            if (p2) reads.Add((v2, c2));
            if (p3) reads.Add((v3, c3));
            if (p4) reads.Add((v4, c4));
            if (reads.Count == 0) return false;

            var byValue = reads
                .GroupBy(r => r.Value)
                .Select(g => new { Value = g.Key, Count = g.Count(), Confidence = g.Max(r => r.Confidence) })
                .ToList();

            int maxCount = byValue.Max(g => g.Count);
            if (maxCount >= 2)
            {
                // At least two independent passes agree -> trust that value
                var winner = byValue
                    .Where(g => g.Count == maxCount)
                    .OrderByDescending(g => g.Confidence)
                    .First();
                value = winner.Value;
                confidence = winner.Confidence;
                return true;
            }

            // Prefer any pass that yields a plausible wall cost (e.g. 5,000,000 vs garbage 56666)
            var plausibleRead = reads
                .Where(r => WallCostPolicy.IsPlausibleWallCost(r.Value))
                .OrderByDescending(r => r.Confidence)
                .FirstOrDefault();

            if (plausibleRead.Value > 0)
            {
                value = plausibleRead.Value;
                confidence = plausibleRead.Confidence;
                return true;
            }

            if (p1)
            {
                value = v1;
                confidence = c1;
                return true;
            }

            var best = reads.OrderByDescending(r => r.Confidence).First();
            value = best.Value;
            confidence = best.Confidence;
            return true;
        }

        private static bool TryReadKnownThousandsWallCost(IVisionEngine vision, Mat screenshot, Rect roi, out int value, out double confidence)
        {
            value = 0;
            confidence = 0;
            using Mat crop = new Mat(screenshot, roi);
            if (crop.Empty()) return false;

            using Mat gray = new();
            using Mat whiteMask = new();
            Cv2.CvtColor(crop, gray, ColorConversionCodes.BGR2GRAY);
            Cv2.Threshold(gray, whiteMask, WallUiLayout.WallCostConsensusThreshold, 255, ThresholdTypes.Binary);
            if (TryReadKnownThousandsWallCostMask(vision, whiteMask, out value))
            {
                confidence = 0.90;
                return true;
            }

            using Mat redMask = Mat.Zeros(crop.Size(), MatType.CV_8UC1);
            for (int y = 0; y < crop.Rows; y++)
            {
                for (int x = 0; x < crop.Cols; x++)
                {
                    Vec3b pixel = crop.At<Vec3b>(y, x);
                    int b = pixel.Item0;
                    int g = pixel.Item1;
                    int r = pixel.Item2;
                    if (r >= 140 && r - g >= 35 && r - b >= 35 && g <= 190 && b <= 190)
                    {
                        redMask.Set(y, x, (byte)255);
                    }
                }
            }

            if (!TryReadKnownThousandsWallCostMask(vision, redMask, out value)) return false;
            confidence = 0.90;
            return true;
        }

        private static bool TryReadKnownThousandsWallCostMask(IVisionEngine vision, Mat sourceMask, out int value)
        {
            value = 0;
            using Mat mask = sourceMask.Clone();
            int labelRight = Math.Max(1, (int)Math.Round(mask.Width * 0.90));
            if (labelRight < mask.Width)
            {
                Cv2.Rectangle(mask, new Rect(labelRight, 0, mask.Width - labelRight, mask.Height), Scalar.Black, -1);
            }

            using Mat contourSource = mask.Clone();
            Cv2.FindContours(contourSource, out Point[][] contours, out _, RetrievalModes.External, ContourApproximationModes.ApproxSimple);
            List<Rect> glyphs = contours
                .Select(Cv2.BoundingRect)
                .Where(r => r.Height >= 10 && r.Height <= 22 && r.Width >= 3 && r.Width <= 20 &&
                            r.Y >= 2 && r.Y <= Math.Min(22, mask.Height - 8) &&
                            r.X > 0 && r.X + r.Width < labelRight)
                .OrderBy(r => r.X)
                .ToList();

            if (glyphs.Count is not (5 or 6)) return false;
            double medianHeight = glyphs.Select(r => (double)r.Height).OrderBy(h => h).ElementAt(glyphs.Count / 2);
            if (glyphs.Any(r => Math.Abs(r.Height - medianHeight) > 3)) return false;
            int[] baselines = glyphs.Select(r => r.Y + r.Height).ToArray();
            if (baselines.Max() - baselines.Min() > 3) return false;
            if (glyphs.Zip(glyphs.Skip(1), (a, b) => b.X - (a.X + a.Width)).Any(gap => gap < 0 || gap > 12)) return false;
            // Keep a left safety margin. Without it, a crop that cuts off the leading "1" in
            // 1,500,000 leaves a valid-looking six-glyph suffix and can be misclassified as 500,000.
            if (glyphs[0].X < 14 || glyphs[0].X > mask.Width * 0.35 ||
                glyphs[^1].X + glyphs[^1].Width >= labelRight) return false;

            bool[] hasHole = glyphs.Select(r => GlyphHasHole(mask, r)).ToArray();
            if (hasHole.Skip(glyphs.Count - 3).Any(hole => !hole)) return false;

            if (glyphs.Count == 6)
            {
                if (hasHole.Skip(1).Any(hole => !hole) ||
                    !TryReadMaskedGlyphs(vision, mask, glyphs.Take(1).ToList(), out int leadingDigit)) return false;
                if (leadingDigit == 6 && !hasHole[0]) leadingDigit = 7;
                value = leadingDigit * 100_000;
            }
            else if (hasHole[1])
            {
                if (!TryReadMaskedGlyphs(vision, mask, glyphs.Take(1).ToList(), out int leadingDigit)) return false;
                if (leadingDigit == 6 && !hasHole[0]) leadingDigit = 7;
                value = leadingDigit * 10_000;
            }
            else
            {
                if (!TryReadMaskedGlyphs(vision, mask, glyphs.Take(2).ToList(), out int prefix)) return false;
                value = prefix * 1_000;
            }

            if (WallCostPolicy.IsPlausibleWallCost(value)) return true;
            value = 0;
            return false;
        }

        private static bool TryReadMaskedGlyphs(IVisionEngine vision, Mat mask, List<Rect> glyphs, out int value)
        {
            value = 0;
            if (glyphs.Count == 0) return false;
            int left = glyphs.Min(r => r.X);
            int top = glyphs.Min(r => r.Y);
            int right = glyphs.Max(r => r.X + r.Width);
            int bottom = glyphs.Max(r => r.Y + r.Height);
            using Mat glyphCrop = new Mat(mask, new Rect(left, top, right - left, bottom - top));
            using Mat glyphBgr = new();
            Cv2.CvtColor(glyphCrop, glyphBgr, ColorConversionCodes.GRAY2BGR);
            return vision.TryExtractNumericalMetrics(
                glyphBgr,
                new Rect(0, 0, glyphBgr.Width, glyphBgr.Height),
                out value,
                out _,
                allowVerticalShift: true);
        }

        private static bool TryReadKnownMillionWallCost(IVisionEngine vision, Mat screenshot, Rect roi, out int value, out double confidence)
        {
            value = 0;
            confidence = 0;
            using Mat crop = new Mat(screenshot, roi);
            if (crop.Empty()) return false;

            using Mat gray = new();
            using Mat mask = new();
            Cv2.CvtColor(crop, gray, ColorConversionCodes.BGR2GRAY);
            Cv2.Threshold(gray, mask, WallUiLayout.WallCostConsensusThreshold, 255, ThresholdTypes.Binary);

            // The resource badge occupies the far-right edge of a button and can look like
            // an extra zero. It is not part of the numeric label.
            int labelRight = Math.Max(1, (int)Math.Round(mask.Width * 0.86));
            if (labelRight < mask.Width)
            {
                Cv2.Rectangle(mask, new Rect(labelRight, 0, mask.Width - labelRight, mask.Height), Scalar.Black, -1);
            }

            using Mat contourSource = mask.Clone();
            Cv2.FindContours(contourSource, out Point[][] contours, out _, RetrievalModes.External, ContourApproximationModes.ApproxSimple);
            List<Rect> glyphs = contours
                .Select(Cv2.BoundingRect)
                .Where(r => r.Height >= 10 && r.Height <= 22 && r.Width >= 3 && r.Width <= 20)
                .OrderBy(r => r.X)
                .ToList();

            if (glyphs.Count is not (7 or 8)) return false;
            double medianHeight = glyphs.Select(r => (double)r.Height).OrderBy(h => h).ElementAt(glyphs.Count / 2);
            if (glyphs.Any(r => Math.Abs(r.Height - medianHeight) > 4)) return false;
            if (glyphs.Zip(glyphs.Skip(1), (a, b) => b.X - (a.X + a.Width)).Any(gap => gap < 0 || gap > 12)) return false;
            if (glyphs[0].X > mask.Width * 0.20 || glyphs[^1].X + glyphs[^1].Width >= labelRight - 1) return false;

            bool[] hasHole = glyphs.Select(r => GlyphHasHole(mask, r)).ToArray();
            int leadingDigit;
            if (glyphs[0].Width <= glyphs[0].Height * 0.48)
            {
                leadingDigit = 1;
            }
            else
            {
                using Mat glyph = new(mask, glyphs[0]);
                using Mat glyphBgr = new();
                Cv2.CvtColor(glyph, glyphBgr, ColorConversionCodes.GRAY2BGR);
                if (!vision.TryExtractNumericalMetrics(glyphBgr, new Rect(0, 0, glyphBgr.Width, glyphBgr.Height), out leadingDigit, out _, allowVerticalShift: true))
                    return false;
                if (leadingDigit == 6 && !hasHole[0]) leadingDigit = 7;
            }

            if (glyphs.Count == 8)
            {
                if (leadingDigit != 1 || hasHole.Skip(1).Any(hole => !hole)) return false;
                value = 10_000_000;
            }
            else if (hasHole[1])
            {
                if (hasHole.Skip(1).Any(hole => !hole)) return false;
                value = leadingDigit * 1_000_000;
            }
            else
            {
                // The only supported seven-digit non-zero second glyph is 1,500,000.
                if (leadingDigit != 1 || hasHole.Skip(2).Any(hole => !hole)) return false;
                using Mat secondGlyph = new(mask, glyphs[1]);
                using Mat secondBgr = new();
                Cv2.CvtColor(secondGlyph, secondBgr, ColorConversionCodes.GRAY2BGR);
                if (!vision.TryExtractNumericalMetrics(secondBgr, new Rect(0, 0, secondBgr.Width, secondBgr.Height), out int secondDigit, out _, allowVerticalShift: true) || secondDigit != 5)
                    return false;
                value = 1_500_000;
            }

            if (!WallCostPolicy.IsPlausibleWallCost(value))
            {
                value = 0;
                return false;
            }
            confidence = 0.90;
            return true;
        }

        private static bool GlyphHasHole(Mat mask, Rect glyphRect)
        {
            using Mat glyph = new(mask, glyphRect);
            using Mat source = glyph.Clone();
            Cv2.FindContours(source, out _, out HierarchyIndex[] hierarchy, RetrievalModes.CComp, ContourApproximationModes.ApproxSimple);
            return hierarchy.Any(h => h.Parent >= 0);
        }

        private static bool TryReadWallCostRgb(IVisionEngine vision, Mat screenshot, Rect roi, out int value, out double confidence)
        {
            value = 0;
            confidence = 0;
            using Mat crop = new Mat(screenshot, roi);
            if (crop.Empty()) return false;
            return vision.TryExtractNumericalMetrics(
                crop,
                new Rect(0, 0, crop.Width, crop.Height),
                out value,
                out confidence,
                isOffline: false,
                useRgbThresh: true,
                invert: false);
        }

        /// <summary>
        /// Extracts the red unaffordable-cost label as white digits on black. Red text is dark in
        /// grayscale, so the normal high-threshold passes otherwise ignore it and may OCR the
        /// button caption below instead.
        /// </summary>
        private static bool TryReadWallCostRed(IVisionEngine vision, Mat screenshot, Rect roi, out int value, out double confidence)
        {
            value = 0;
            confidence = 0;
            using Mat crop = new Mat(screenshot, roi);
            if (crop.Empty()) return false;

            using Mat redMask = Mat.Zeros(crop.Size(), MatType.CV_8UC1);
            for (int y = 0; y < crop.Rows; y++)
            {
                for (int x = 0; x < crop.Cols; x++)
                {
                    Vec3b pixel = crop.At<Vec3b>(y, x);
                    int b = pixel.Item0;
                    int g = pixel.Item1;
                    int r = pixel.Item2;
                    if (r >= 140 && r - g >= 35 && r - b >= 35 && g <= 190 && b <= 190)
                    {
                        redMask.Set(y, x, (byte)255);
                    }
                }
            }

            if (Cv2.CountNonZero(redMask) < 20) return false;
            SplitTouchingRedDigits(redMask);
            using Mat redBgr = new Mat();
            Cv2.CvtColor(redMask, redBgr, ColorConversionCodes.GRAY2BGR);
            bool read = vision.TryExtractNumericalMetrics(
                redBgr,
                new Rect(0, 0, redBgr.Width, redBgr.Height),
                out value,
                out confidence);
            if (!read) return false;
            if (!WallCostPolicy.IsPlausibleWallCost(value) && TryNormalizeRedWallCost(redMask, value, out int normalizedValue))
            {
                value = normalizedValue;
            }
            return true;
        }

        private static bool TryNormalizeRedWallCost(Mat redMask, int rawValue, out int normalizedValue)
        {
            normalizedValue = 0;
            using Mat contourSource = redMask.Clone();
            Cv2.FindContours(contourSource, out Point[][] contours, out _, RetrievalModes.External, ContourApproximationModes.ApproxSimple);
            List<Rect> glyphs = contours.Select(Cv2.BoundingRect)
                .Where(r => r.Height >= 10 && r.Width > 2 && r.Width < 30 && r.Y >= 2 && r.Y <= 20)
                .OrderBy(r => r.X).ToList();
            string rawDigits = rawValue.ToString(System.Globalization.CultureInfo.InvariantCulture);
            if (glyphs.Count < 4 || rawDigits.Length != glyphs.Count) return false;
            char[] digits = rawDigits.ToCharArray();
            for (int i = 0; i < glyphs.Count; i++)
            {
                using Mat glyphMask = new Mat(redMask, glyphs[i]);
                using Mat hierarchySource = glyphMask.Clone();
                Cv2.FindContours(hierarchySource, out _, out HierarchyIndex[] hierarchy, RetrievalModes.CComp, ContourApproximationModes.ApproxSimple);
                if (hierarchy.Any(h => h.Parent >= 0)) digits[i] = '0';
            }
            return int.TryParse(new string(digits), out normalizedValue) && WallCostPolicy.IsPlausibleWallCost(normalizedValue);
        }

        private static void SplitTouchingRedDigits(Mat redMask)
        {
            using Mat contourSource = redMask.Clone();
            Cv2.FindContours(contourSource, out Point[][] contours, out _, RetrievalModes.External, ContourApproximationModes.ApproxSimple);
            foreach (Point[] contour in contours)
            {
                Rect glyph = Cv2.BoundingRect(contour);
                if (glyph.Height < 10 || glyph.Width < glyph.Height * 1.20 || glyph.Width >= 30) continue;

                int searchStart = glyph.X + glyph.Width / 3;
                int searchEnd = glyph.X + (glyph.Width * 2) / 3;
                int splitX = searchStart;
                int fewestPixels = int.MaxValue;
                for (int x = searchStart; x <= searchEnd; x++)
                {
                    int columnPixels = 0;
                    for (int y = glyph.Y; y < glyph.Y + glyph.Height; y++)
                    {
                        if (redMask.At<byte>(y, x) != 0) columnPixels++;
                    }
                    if (columnPixels < fewestPixels)
                    {
                        fewestPixels = columnPixels;
                        splitX = x;
                    }
                }

                Cv2.Line(redMask, new Point(splitX, glyph.Y), new Point(splitX, glyph.Y + glyph.Height - 1), Scalar.Black, 1);
            }
        }

        private static bool TryReadWallUpgradeCostAtRoi(IVisionEngine vision, Mat screenshot, Rect roi, double grayThreshold, out int value, out double confidence)
        {
            value = 0;
            confidence = 0;
            using Mat crop = new Mat(screenshot, roi);
            using Mat gray = new Mat();
            using Mat binary = new Mat();
            using Mat binaryBgr = new Mat();
            Cv2.CvtColor(crop, gray, ColorConversionCodes.BGR2GRAY);
            Cv2.Threshold(gray, binary, grayThreshold, 255, ThresholdTypes.Binary);
            RemoveRightEdgeCostNoise(binary);
            Cv2.CvtColor(binary, binaryBgr, ColorConversionCodes.GRAY2BGR);

            return vision.TryExtractNumericalMetrics(
                binaryBgr,
                new Rect(0, 0, binaryBgr.Width, binaryBgr.Height),
                out value,
                out confidence);
        }

        private static void RemoveRightEdgeCostNoise(Mat binary)
        {
            if (binary == null || binary.Empty()) return;

            using Mat contourSource = binary.Clone();
            Cv2.FindContours(contourSource, out Point[][] contours, out _, RetrievalModes.External, ContourApproximationModes.ApproxSimple);
            int rightEdgeGuard = Math.Max(2, binary.Width / 40);

            foreach (var contour in contours)
            {
                Rect r = Cv2.BoundingRect(contour);
                bool isThinRightNoise = r.Y >= 2 && r.Y <= binary.Height / 2 && r.Height >= binary.Height * 0.30 && r.Width > 0 && r.Width <= 5;
                bool touchesRightEdge = r.X + r.Width >= binary.Width - rightEdgeGuard;
                if (isThinRightNoise && touchesRightEdge)
                {
                    Cv2.Rectangle(binary, r, Scalar.Black, -1);
                }
            }
        }

        /// <summary>
        /// Handles wall upgrades independently of the wall level.
        /// Scans the builder menu with the 4 generic wall templates, reads the current resources, runs the numbers through WallUpgradeDecider and performs the upgrade.
        /// </summary>
        public int HandleHomeResources(
            int wallGoldThreshold,
            int wallElixirThreshold,
            int wallGoldReserve,
            int wallElixirReserve,
            int batchLimit = 1,
            bool debugScreenshots = false,
            int cycle = 0,
            CancellationToken token = default,
            string trigger = "unknown",
            string? runId = null)
        {
            runId ??= WallLogger.GenerateRunId();
            var handleTimer = WallLogger.StartTimer();
            if (token.IsCancellationRequested)
            {
                WallLogger.LogInfo("handle_home_resources", "cancelled", reason: "cancellation_requested", cycle: cycle, trigger: trigger, runId: runId, elapsedMs: handleTimer.ElapsedMilliseconds);
                return 0;
            }

            _debug.Configure(debugScreenshots, cycle);
            int safeBatchLimit = Math.Clamp(batchLimit, 1, WallQuantityPlanner.HardSafetyMaximum);

            WallLogger.LogInfo("handle_home_resources", "start", cycle: cycle, trigger: trigger, batchLimit: safeBatchLimit, runId: runId, extra: $"gold_start={wallGoldThreshold:N0} elixir_start={wallElixirThreshold:N0} gold_reserve={wallGoldReserve:N0} elixir_reserve={wallElixirReserve:N0}");
            Console.WriteLine($"[WALL] phase=target_plan cycle={cycle} status=start gold_start={wallGoldThreshold:N0} elixir_start={wallElixirThreshold:N0} gold_reserve={wallGoldReserve:N0} elixir_reserve={wallElixirReserve:N0} batch_limit={safeBatchLimit}");

            string[] templateNames = _scanner.GetWallTemplateNames();
            WallLogger.LogInfo("preflight_templates", templateNames.Length > 0 ? "ok" : "skip", reason: templateNames.Length > 0 ? "templates_loaded" : "wall_templates_missing", cycle: cycle, trigger: trigger, runId: runId, extra: $"template_count={templateNames.Length} template_names=\"{string.Join(",", templateNames)}\"");

            if (templateNames.Length == 0)
            {
                Console.WriteLine($"[WALL RESULT] phase=target_plan cycle={cycle} status=skip reason=wall_templates_missing");
                WallLogger.LogInfo("handle_home_resources", "skip", reason: "wall_templates_missing", cycle: cycle, trigger: trigger, runId: runId, elapsedMs: handleTimer.ElapsedMilliseconds);
                return 0;
            }

            using Mat? initialScreenshot = _adb.TakeScreenshot();
            bool layoutOk = WallPanelInspector.ValidateSupportedLayout(initialScreenshot, cycle, out string layoutReason, trigger, runId);
            if (!layoutOk)
            {
                Console.WriteLine($"[WALL RESULT] phase=target_plan cycle={cycle} status=skip reason={layoutReason}");
                WallLogger.LogInfo("handle_home_resources", "skip", reason: layoutReason, cycle: cycle, trigger: trigger, runId: runId, elapsedMs: handleTimer.ElapsedMilliseconds);
                return 0;
            }

            BuilderAvailabilityResult builder = _builderDetector.Detect(initialScreenshot);
            WallLogger.LogInfo("preflight_builder", builder.State == BuilderAvailabilityState.Available ? "ok" : "skip", reason: builder.Reason, cycle: cycle, trigger: trigger, runId: runId, extra: $"builder_state={builder.State.ToString().ToLowerInvariant()} free_builders={builder.FreeBuilders?.ToString() ?? "unknown"} total_builders={builder.TotalBuilders?.ToString() ?? "unknown"} confidence={builder.Confidence:F2} icon_score={builder.IconScore:F3}");

            Console.WriteLine(
                $"[WALL] phase=builder_preflight cycle={cycle} state={builder.State.ToString().ToLowerInvariant()} " +
                $"free_builders={builder.FreeBuilders?.ToString() ?? "unknown"} " +
                $"total_builders={builder.TotalBuilders?.ToString() ?? "unknown"} " +
                $"confidence={builder.Confidence:F2} icon_score={builder.IconScore:F3} reason={builder.Reason}");

            if (builder.State != BuilderAvailabilityState.Available)
            {
                Console.WriteLine($"[WALL RESULT] phase=builder_preflight cycle={cycle} status=skip reason={builder.Reason}");
                WallLogger.LogInfo("handle_home_resources", "skip", reason: builder.Reason, cycle: cycle, trigger: trigger, runId: runId, elapsedMs: handleTimer.ElapsedMilliseconds);
                return 0;
            }

            try
            {
                WallTransactionResult result = UpgradeWallBulk(
                    wallGoldThreshold,
                    wallElixirThreshold,
                    wallGoldReserve,
                    wallElixirReserve,
                    safeBatchLimit,
                    token,
                    trigger,
                    runId,
                    cycle);

                if (result.VerifiedCount > 0)
                {
                    _debug.RecordVerified(result.VerifiedCount);
                }
                else if (string.Equals(result.Reason, "outcome_unknown", StringComparison.OrdinalIgnoreCase) ||
                         string.Equals(result.Reason, "cancelled_post_confirm", StringComparison.OrdinalIgnoreCase) ||
                         result.Reason.StartsWith("post_confirm_", StringComparison.OrdinalIgnoreCase) ||
                         result.Reason.Contains("delta_mismatch", StringComparison.OrdinalIgnoreCase))
                {
                    _debug.RecordUnknown();
                }
                else
                {
                    _debug.RecordSkipped();
                }

                _debug.LogSessionCounters(
                    "handle_home_resources",
                    result.Resource,
                    result.Cost,
                    result.CandidateMatchCount,
                    result.RequestedCount,
                    result.VerifiedCount,
                    result.Reason);

                WallLogger.LogInfo("handle_home_resources", "end", cycle: cycle, trigger: trigger, runId: runId, elapsedMs: handleTimer.ElapsedMilliseconds, extra: $"verified_count={result.VerifiedCount} reason={result.Reason}");
                return result.VerifiedCount;
            }
            catch (Exception ex)
            {
                WallLogger.LogInfo("handle_home_resources", "fail", reason: "exception", cycle: cycle, trigger: trigger, runId: runId, elapsedMs: handleTimer.ElapsedMilliseconds, extra: $"ex_type=\"{ex.GetType().Name}\" ex_msg=\"{ex.Message}\"");
                throw;
            }
        }

        private WallTransactionResult UpgradeWallBulk(
            int wallGoldThreshold,
            int wallElixirThreshold,
            int wallGoldReserve,
            int wallElixirReserve,
            int batchLimit,
            CancellationToken token = default,
            string trigger = "unknown",
            string? runId = null,
            int cycle = 0)
        {
            runId ??= WallLogger.GenerateRunId();
            if (token.IsCancellationRequested)
            {
                WallLogger.LogInfo("upgrade_wall_bulk", "cancelled", reason: "cancelled", cycle: cycle, trigger: trigger, runId: runId);
                return WallTransactionResult.Skip("cancelled");
            }
            int safeBatchLimit = Math.Clamp(batchLimit, 1, WallQuantityPlanner.HardSafetyMaximum);
            var timer = WallLogger.StartTimer();
            WallLogger.LogInfo("upgrade_wall_bulk", "start", cycle: cycle, trigger: trigger, batchLimit: safeBatchLimit, runId: runId);
            Console.WriteLine($"[WALL] phase=attempt_upgrade status=start batch_limit={safeBatchLimit}");

            var res = TryUpgradeWallBatch(wallGoldThreshold, wallElixirThreshold, wallGoldReserve, wallElixirReserve, safeBatchLimit, token, trigger, runId, cycle);
            WallLogger.LogInfo("upgrade_wall_bulk", "end", cycle: cycle, trigger: trigger, runId: runId, elapsedMs: timer.ElapsedMilliseconds, extra: $"verified_count={res.VerifiedCount} status_reason={res.Reason}");
            return res;
        }

        private WallTransactionResult TryUpgradeWallBatch(
            int wallGoldThreshold,
            int wallElixirThreshold,
            int wallGoldReserve,
            int wallElixirReserve,
            int batchLimit,
            CancellationToken token,
            string trigger = "unknown",
            string? runId = null,
            int cycle = 0)
        {
            runId ??= WallLogger.GenerateRunId();
            var batchTimer = WallLogger.StartTimer();
            if (token.IsCancellationRequested)
            {
                WallLogger.LogInfo("upgrade_batch", "cancelled", reason: "cancelled", cycle: cycle, trigger: trigger, runId: runId);
                return WallTransactionResult.Skip("cancelled");
            }
            int safeBatchLimit = Math.Clamp(batchLimit, 1, WallQuantityPlanner.HardSafetyMaximum);

            try
            {
                WallCandidateSelection selection = _selector.SelectValidatedCandidate(token, trigger, runId, cycle);
                int candidateMatchCount = selection.CandidateMatchCount;

                if (selection.SkipReason is string skipReason)
                {
                    WallLogger.LogInfo("upgrade_batch", "skip", reason: skipReason, cycle: cycle, trigger: trigger, runId: runId, elapsedMs: batchTimer.ElapsedMilliseconds, extra: $"candidate_match_count={candidateMatchCount}");
                    return string.Equals(skipReason, "cancelled", StringComparison.Ordinal)
                        ? WallTransactionResult.Skip("cancelled")
                        : WallTransactionResult.Skip(skipReason).WithCandidateMatchCount(candidateMatchCount);
                }

                using Mat? currentScreenshot = _adb.TakeScreenshot();
                if (currentScreenshot == null || currentScreenshot.Empty())
                {
                    WallLogger.LogInfo("upgrade_batch", "fail", reason: "screenshot_failed", cycle: cycle, trigger: trigger, runId: runId, elapsedMs: batchTimer.ElapsedMilliseconds);
                    _navigator.BestEffortDismiss();
                    return WallTransactionResult.Skip("screenshot_failed").WithCandidateMatchCount(candidateMatchCount);
                }

                // Extract current resources and cost of a single wall dynamically
                (int currentGold, int currentElixir, _) = IsTarget.ExtractHomeResources(_adb, _vision);

                var panelLocal = WallDynamicLocalizer.LocalizePanelAndButtons(_vision, currentScreenshot);
                WallResourceButtonInfo goldBtnInfo = panelLocal.GoldInfo;
                WallResourceButtonInfo elixirBtnInfo = panelLocal.ElixirInfo;

                int detectedGoldCost = 0;
                double goldCostConfidence = 0;
                int detectedElixirCost = 0;
                double elixirCostConfidence = 0;

                bool goldCostRead = goldBtnInfo.Found && TryReadWallUpgradeCost(_vision, currentScreenshot, goldBtnInfo.CostRoi, out detectedGoldCost, out goldCostConfidence);
                bool elixirCostRead = panelLocal.ResourceMode == WallUpgradeResourceMode.GoldAndElixir &&
                    elixirBtnInfo.Found &&
                    TryReadWallUpgradeCost(_vision, currentScreenshot, elixirBtnInfo.CostRoi, out detectedElixirCost, out elixirCostConfidence);
                if (!goldCostRead) { detectedGoldCost = 0; goldCostConfidence = 0; }
                if (!elixirCostRead) { detectedElixirCost = 0; elixirCostConfidence = 0; }

                Console.WriteLine($"[WALL-MV] resource=gold panel_rect=({panelLocal.SearchRoi.X},{panelLocal.SearchRoi.Y},{panelLocal.SearchRoi.Width},{panelLocal.SearchRoi.Height}) button_rect=({goldBtnInfo.ButtonRect.X},{goldBtnInfo.ButtonRect.Y},{goldBtnInfo.ButtonRect.Width},{goldBtnInfo.ButtonRect.Height}) cost_rect=({goldBtnInfo.CostRoi.X},{goldBtnInfo.CostRoi.Y},{goldBtnInfo.CostRoi.Width},{goldBtnInfo.CostRoi.Height}) tap_point=({goldBtnInfo.TapPoint.X},{goldBtnInfo.TapPoint.Y}) method={goldBtnInfo.Method} score={goldBtnInfo.Score:F3} ocr_val={detectedGoldCost} ocr_conf={goldCostConfidence:F2} ocr_method=wall_binary_242 reason={(goldBtnInfo.Found ? "ok" : goldBtnInfo.SkipReason)}");
                Console.WriteLine($"[WALL-MV] resource=elixir panel_rect=({panelLocal.SearchRoi.X},{panelLocal.SearchRoi.Y},{panelLocal.SearchRoi.Width},{panelLocal.SearchRoi.Height}) button_rect=({elixirBtnInfo.ButtonRect.X},{elixirBtnInfo.ButtonRect.Y},{elixirBtnInfo.ButtonRect.Width},{elixirBtnInfo.ButtonRect.Height}) cost_rect=({elixirBtnInfo.CostRoi.X},{elixirBtnInfo.CostRoi.Y},{elixirBtnInfo.CostRoi.Width},{elixirBtnInfo.CostRoi.Height}) tap_point=({elixirBtnInfo.TapPoint.X},{elixirBtnInfo.TapPoint.Y}) method={elixirBtnInfo.Method} score={elixirBtnInfo.Score:F3} ocr_val={detectedElixirCost} ocr_conf={elixirCostConfidence:F2} ocr_method=wall_binary_242 reason={(elixirBtnInfo.Found ? "ok" : elixirBtnInfo.SkipReason)}");

                bool roiPairVerified = goldBtnInfo.CostRoiVerified && elixirBtnInfo.CostRoiVerified;
                bool goldOnlyVerified = panelLocal.ResourceMode == WallUpgradeResourceMode.GoldOnly && goldBtnInfo.CostRoiVerified;
                WallCostValidationResult costValidation = panelLocal.ResourceMode switch
                {
                    WallUpgradeResourceMode.FullyUpgraded
                        => new WallCostValidationResult(false, 0, "wall_fully_upgraded"),
                    WallUpgradeResourceMode.GoldOnly when goldOnlyVerified
                        => WallCostPolicy.ValidateGoldOnlyCost(detectedGoldCost),
                    WallUpgradeResourceMode.GoldAndElixir when roiPairVerified
                        => WallCostPolicy.ValidateWallCosts(detectedGoldCost, detectedElixirCost),
                    WallUpgradeResourceMode.GoldOnly
                        => new WallCostValidationResult(false, 0, "wall_gold_cost_not_verified"),
                    _ => new WallCostValidationResult(false, 0, "wall_cost_pair_not_verified")
                };
                double mismatchRatio = (detectedGoldCost > 0 && detectedElixirCost > 0)
                    ? (double)Math.Max(detectedGoldCost, detectedElixirCost) / Math.Min(detectedGoldCost, detectedElixirCost)
                    : 1.0;

                WallLogger.LogInfo("ocr_decision", costValidation.IsValid ? "ok" : "fail", reason: costValidation.Reason, cycle: cycle, trigger: trigger, runId: runId, extra: $"current_gold={currentGold:N0} current_elixir={currentElixir:N0} raw_gold_cost={detectedGoldCost} raw_elixir_cost={detectedElixirCost} gold_cost_read={goldCostRead} elixir_cost_read={elixirCostRead} gold_cost_confidence={goldCostConfidence:F2} elixir_cost_confidence={elixirCostConfidence:F2} gold_cost_roi=({goldBtnInfo.CostRoi.X},{goldBtnInfo.CostRoi.Y},{goldBtnInfo.CostRoi.Width},{goldBtnInfo.CostRoi.Height}) elixir_cost_roi=({elixirBtnInfo.CostRoi.X},{elixirBtnInfo.CostRoi.Y},{elixirBtnInfo.CostRoi.Width},{elixirBtnInfo.CostRoi.Height}) cost_mismatch_ratio={mismatchRatio:F2}");

                if (!costValidation.IsValid)
                {
                    Console.WriteLine($"[WALL RESULT] phase=cost_ocr cycle={_debug.Cycle} gold_cost={detectedGoldCost} elixir_cost={detectedElixirCost} status=skip reason={costValidation.Reason}");
                    WallLogger.LogInfo("upgrade_batch", "skip", reason: costValidation.Reason, cycle: cycle, trigger: trigger, runId: runId, elapsedMs: batchTimer.ElapsedMilliseconds);
                    _navigator.BestEffortDismiss();
                    return WallTransactionResult.Skip(costValidation.Reason).WithCandidateMatchCount(candidateMatchCount);
                }

                int singleWallCost = costValidation.Cost;

                var decisionInput = new WallUpgradeDecisionInput(
                    WallCost: singleWallCost,
                    Gold: currentGold,
                    Elixir: currentElixir,
                    GoldStartThreshold: wallGoldThreshold,
                    ElixirStartThreshold: wallElixirThreshold,
                    GoldReserve: wallGoldReserve,
                    ElixirReserve: wallElixirReserve,
                    BatchLimit: safeBatchLimit,
                    ResourceMode: panelLocal.ResourceMode);

                WallUpgradeDecision decision = WallUpgradeDecider.Decide(decisionInput);
                WallLogger.LogInfo("decider_check", decision.Resource != WallUpgradeResource.None ? "ok" : "skip", reason: string.IsNullOrEmpty(decision.SkipReason) ? "decided" : decision.SkipReason, cycle: cycle, trigger: trigger, runId: runId, extra: $"gold_threshold={wallGoldThreshold:N0} elixir_threshold={wallElixirThreshold:N0} gold_reserve={wallGoldReserve:N0} elixir_reserve={wallElixirReserve:N0} affordable_gold={decision.AffordableGold} affordable_elixir={decision.AffordableElixir} selected_resource={decision.Resource} requested_count={decision.RequestedCount}");

                if (decision.Resource == WallUpgradeResource.None || decision.RequestedCount <= 0)
                {
                    Console.WriteLine($"[WALL RESULT] phase=decider_check cycle={_debug.Cycle} gold={currentGold:N0} elixir={currentElixir:N0} cost={singleWallCost:N0} status=skip reason={decision.SkipReason}");
                    WallLogger.LogInfo("upgrade_batch", "skip", reason: decision.SkipReason, cycle: cycle, trigger: trigger, runId: runId, elapsedMs: batchTimer.ElapsedMilliseconds);
                    _navigator.BestEffortDismiss();
                    return WallTransactionResult.Skip(decision.SkipReason).WithCandidateMatchCount(candidateMatchCount);
                }

                string selectedResource = decision.Resource == WallUpgradeResource.Gold ? "gold" : "elixir";
                WallResourceButtonInfo selectedBtnInfo = selectedResource == "gold" ? goldBtnInfo : elixirBtnInfo;

                if (!selectedBtnInfo.Found)
                {
                    string resourceSkipReason = string.IsNullOrEmpty(selectedBtnInfo.SkipReason) ? "resource_button_not_localized" : selectedBtnInfo.SkipReason;
                    Console.WriteLine($"[WALL RESULT] phase=attempt_upgrade cycle={_debug.Cycle} resource={selectedResource} status=skip reason={resourceSkipReason}");
                    WallLogger.LogInfo("upgrade_batch", "skip", reason: resourceSkipReason, cycle: cycle, trigger: trigger, runId: runId, elapsedMs: batchTimer.ElapsedMilliseconds);
                    _navigator.BestEffortDismiss();
                    return WallTransactionResult.Skip(resourceSkipReason).WithCandidateMatchCount(candidateMatchCount);
                }

                bool costIsRed = WallCostPolicy.IsUpgradeCostRed(currentScreenshot, selectedBtnInfo.CostRoi, out double redRatio, out int redPixels);
                bool btnAvailable = WallPanelInspector.IsResourceUpgradeButtonAvailable(currentScreenshot, selectedBtnInfo);

                Point upgradePoint = selectedBtnInfo.TapPoint;
                WallLogger.LogInfo("button_check", (!costIsRed && btnAvailable) ? "ok" : "fail", reason: (!btnAvailable ? "button_unavailable" : (costIsRed ? "cost_red" : "ok")), cycle: cycle, trigger: trigger, runId: runId, extra: $"selected_resource={selectedResource} btn_available={btnAvailable} cost_is_red={costIsRed} red_pixels={redPixels} red_ratio={redRatio:F3} upgrade_btn_x={upgradePoint.X} upgrade_btn_y={upgradePoint.Y}");

                if (!btnAvailable || costIsRed)
                {
                    Console.WriteLine($"[WALL RESULT] phase=attempt_upgrade cycle={_debug.Cycle} resource={selectedResource} status=skip reason=resource_button_unavailable_or_red");
                    WallLogger.LogInfo("upgrade_batch", "skip", reason: "resource_button_unavailable_or_red", cycle: cycle, trigger: trigger, runId: runId, elapsedMs: batchTimer.ElapsedMilliseconds);
                    _navigator.BestEffortDismiss();
                    return WallTransactionResult.Skip("resource_button_unavailable_or_red").WithCandidateMatchCount(candidateMatchCount);
                }

                int actualSelectedCount = _quantityAdjuster.AddWallsSafely(selectedResource, decision.RequestedCount, safeBatchLimit, token, trigger, runId, cycle, selectedBtnInfo, singleWallCost);
                WallLogger.LogInfo("quantity_adjuster", actualSelectedCount > 0 ? "ok" : "fail", cycle: cycle, trigger: trigger, runId: runId, extra: $"requested_count={decision.RequestedCount} selected_count={actualSelectedCount}");

                if (actualSelectedCount <= 0)
                {
                    WallLogger.LogInfo("upgrade_batch", "skip", reason: "quantity_controls_not_localized", cycle: cycle, trigger: trigger, runId: runId, elapsedMs: batchTimer.ElapsedMilliseconds);
                    _navigator.BestEffortDismiss();
                    return WallTransactionResult.Skip("quantity_controls_not_localized").WithCandidateMatchCount(candidateMatchCount);
                }

                _debug.Capture("add_wall_done");

                // Quantity changes can move/rebuild resource buttons. Never reuse the pre-gateway tap point.
                using Mat? finalPanelScreenshot = _adb.TakeScreenshot();
                if (finalPanelScreenshot == null || finalPanelScreenshot.Empty())
                {
                    _navigator.BestEffortDismiss();
                    return WallTransactionResult.Skip("final_panel_screenshot_failed").WithCandidateMatchCount(candidateMatchCount);
                }
                WallPanelLocalizationResult finalPanel = WallDynamicLocalizer.LocalizePanelAndButtons(_vision, finalPanelScreenshot);
                selectedBtnInfo = selectedResource == "gold" ? finalPanel.GoldInfo : finalPanel.ElixirInfo;
                if (!selectedBtnInfo.Found ||
                    !WallBatchTotalReader.TryRead(_vision, finalPanelScreenshot, selectedBtnInfo.CostRoi, out long finalBatchTotal, out _) ||
                    !WallBatchTotalReader.Validate(finalBatchTotal, singleWallCost, actualSelectedCount))
                {
                    _navigator.BestEffortDismiss();
                    return WallTransactionResult.Skip("final_batch_state_not_verified").WithCandidateMatchCount(candidateMatchCount);
                }
                upgradePoint = selectedBtnInfo.TapPoint;

                int resourceBefore = selectedResource.Equals("gold", StringComparison.OrdinalIgnoreCase) ? currentGold : currentElixir;

                WallLogger.LogInfo("tap_upgrade_btn", "ok", cycle: cycle, trigger: trigger, runId: runId, extra: $"selected_resource={selectedResource} tap_x={upgradePoint.X} tap_y={upgradePoint.Y}");
                _adb.Tap(upgradePoint.X, upgradePoint.Y);
                if (InterruptibleSleep(1000, token))
                {
                    WallLogger.LogInfo("upgrade_batch", "cancelled", reason: "cancelled", cycle: cycle, trigger: trigger, runId: runId, elapsedMs: batchTimer.ElapsedMilliseconds);
                    return WallTransactionResult.Skip("cancelled");
                }

                using Mat? confirmScreenshot = _adb.TakeScreenshot();
                double confirmBrightness = 0;
                bool confirmOpen = confirmScreenshot != null && !confirmScreenshot.Empty() && _inspector.IsConfirmDialogOpen(confirmScreenshot, out confirmBrightness, trigger, runId, cycle);
                WallLogger.LogInfo("confirm_dialog_open", confirmOpen ? "ok" : "fail", reason: confirmOpen ? "confirm_open" : "confirm_dialog_not_verified", cycle: cycle, trigger: trigger, runId: runId, extra: $"dialog_brightness={confirmBrightness:F1} dialog_open={confirmOpen}");

                if (!confirmOpen || confirmScreenshot == null)
                {
                    Console.WriteLine($"[WALL RESULT] phase=confirm_open cycle={_debug.Cycle} resource={selectedResource} candidate_match_count={candidateMatchCount} requested_count={actualSelectedCount} verified_count=0 status=skip reason=confirm_dialog_not_verified");
                    WallLogger.LogInfo("upgrade_batch", "skip", reason: "confirm_dialog_not_verified", cycle: cycle, trigger: trigger, runId: runId, elapsedMs: batchTimer.ElapsedMilliseconds, extra: "attempted_unverified=true");
                    _navigator.BestEffortDismiss();
                    return WallTransactionResult.Skip("confirm_dialog_not_verified").WithCandidateMatchCount(candidateMatchCount);
                }

                _debug.Capture("confirm_open");

                WallConfirmationValidation confirmation = WallConfirmationFlow.Validate(
                    _vision, confirmScreenshot, selectedResource, actualSelectedCount, singleWallCost, finalBatchTotal);
                WallLogger.LogInfo("confirmation_flow", confirmation.Valid ? "ok" : "fail",
                    reason: confirmation.Reason, cycle: cycle, trigger: trigger, runId: runId,
                    extra: $"kind={confirmation.Kind} expected_kind={(actualSelectedCount > 1 ? WallConfirmationKind.MultiCancelOkay : WallConfirmationKind.SingleConfirm)} expected_resource={selectedResource} observed_resource={confirmation.Resource} selected_count={actualSelectedCount} expected_total={(long)singleWallCost * actualSelectedCount:N0} observed_total={confirmation.Total:N0}");
                if (!confirmation.Valid)
                {
                    Console.WriteLine($"[WALL RESULT] phase=confirmation_flow cycle={_debug.Cycle} resource={selectedResource} requested_count={actualSelectedCount} verified_count=0 status=skip reason={confirmation.Reason}");
                    _navigator.BestEffortDismiss();
                    return WallTransactionResult.Skip(confirmation.Reason).WithCandidateMatchCount(candidateMatchCount);
                }

                WallConfirmButtonInfo confirmBtnInfo = WallDynamicLocalizer.LocalizeConfirmButton(_vision, confirmScreenshot, actualSelectedCount > 1, selectedBtnInfo);
                if (!confirmBtnInfo.Found)
                {
                    Console.WriteLine($"[WALL RESULT] phase=confirm_open cycle={_debug.Cycle} resource={selectedResource} candidate_match_count={candidateMatchCount} requested_count={actualSelectedCount} verified_count=0 status=skip reason=confirm_button_not_localized");
                    WallLogger.LogInfo("upgrade_batch", "skip", reason: "confirm_button_not_localized", cycle: cycle, trigger: trigger, runId: runId, elapsedMs: batchTimer.ElapsedMilliseconds, extra: "attempted_unverified=true");
                    _navigator.BestEffortDismiss();
                    return WallTransactionResult.Skip("confirm_button_not_localized").WithCandidateMatchCount(candidateMatchCount);
                }

                Point confirmPoint = confirmBtnInfo.TapPoint;
                WallLogger.LogInfo("tap_confirm_btn", "ok", cycle: cycle, trigger: trigger, runId: runId, extra: $"confirm_btn_x={confirmPoint.X} confirm_btn_y={confirmPoint.Y} is_multi={actualSelectedCount > 1} method={confirmBtnInfo.Method} score={confirmBtnInfo.Score:F3}");
                _adb.Tap(confirmPoint.X, confirmPoint.Y);

                if (InterruptibleSleep(1500, token))
                {
                    WallLogger.LogInfo("upgrade_batch", "cancelled", reason: "cancelled_post_confirm", cycle: cycle, trigger: trigger, runId: runId, elapsedMs: batchTimer.ElapsedMilliseconds, extra: "attempted_unverified=true");
                    _navigator.BestEffortDismiss();
                    return new WallTransactionResult(0, "cancelled_post_confirm", Resource: selectedResource, CandidateMatchCount: candidateMatchCount, RequestedCount: actualSelectedCount);
                }

                bool dialogClosed = _inspector.IsConfirmDialogClosed(out double postConfirmBrightness, trigger, runId, cycle);
                WallLogger.LogInfo("confirm_dialog_closed", dialogClosed ? "ok" : "fail", reason: dialogClosed ? "dialog_closed" : "dialog_still_open", cycle: cycle, trigger: trigger, runId: runId, extra: $"dialog_brightness={postConfirmBrightness:F1} dialog_closed={dialogClosed}");

                if (!dialogClosed)
                {
                    Console.WriteLine($"[WALL RESULT] phase=confirm_verify cycle={_debug.Cycle} resource={selectedResource} status=unknown reason=dialog_still_open");
                    WallLogger.LogInfo("upgrade_batch", "unknown", reason: "dialog_still_open", cycle: cycle, trigger: trigger, runId: runId, elapsedMs: batchTimer.ElapsedMilliseconds, extra: "attempted_unverified=true");
                    _navigator.BestEffortDismiss();
                    return new WallTransactionResult(0, "outcome_unknown", Resource: selectedResource, CandidateMatchCount: candidateMatchCount, RequestedCount: actualSelectedCount);
                }

                // Re-read resources after confirming, polling up to 3 times (~250ms each) to allow
                // the game's animation to settle. Stop early as soon as the delta is verified.
                int resourceAfter = 0;
                long expectedSpend = (long)singleWallCost * actualSelectedCount;
                long actualSpend = 0;
                long tolerance = Math.Max(20_000, expectedSpend / 10);
                bool deltaOk = false;
                bool hudReadFailed = false;

                for (int poll = 0; poll < 3; poll++)
                {
                    (int goldAfter, int elixirAfter, _) = IsTarget.ExtractHomeResources(_adb, _vision);
                    int pollResourceAfter = selectedResource.Equals("gold", StringComparison.OrdinalIgnoreCase) ? goldAfter : elixirAfter;

                    if (pollResourceAfter <= 0)
                    {
                        // HUD read failed completely for this poll; mark and stop polling.
                        WallLogger.LogInfo("post_confirm_poll", "fail", reason: "hud_read_zero", cycle: cycle, trigger: trigger, runId: runId, extra: $"poll={poll + 1} resource_before={resourceBefore:N0} resource_after={pollResourceAfter} expected_spend={expectedSpend:N0}");
                        hudReadFailed = true;
                        break;
                    }

                    resourceAfter = pollResourceAfter;
                    actualSpend = (long)resourceBefore - resourceAfter;

                    if (WallCostPolicy.IsResourceDeltaVerified(resourceBefore, resourceAfter, expectedSpend))
                    {
                        deltaOk = true;
                        WallLogger.LogInfo("post_confirm_poll", "ok", cycle: cycle, trigger: trigger, runId: runId, extra: $"poll={poll + 1} resource_before={resourceBefore:N0} resource_after={resourceAfter:N0} expected_spend={expectedSpend:N0} actual_spend={actualSpend:N0} tolerance={tolerance:N0} delta_ok=true verified=true");
                        break;
                    }

                    WallLogger.LogInfo("post_confirm_poll", "pending", cycle: cycle, trigger: trigger, runId: runId, extra: $"poll={poll + 1} resource_before={resourceBefore:N0} resource_after={resourceAfter:N0} expected_spend={expectedSpend:N0} actual_spend={actualSpend:N0} tolerance={tolerance:N0} delta_ok=false");

                    // Only sleep if there are more polls to attempt.
                    if (poll < 2)
                    {
                        Thread.Sleep(250);
                    }
                }

                _navigator.BestEffortDismiss();

                if (deltaOk)
                {
                    int totalCost = actualSpend > 0 ? (int)actualSpend : (int)expectedSpend;
                    Console.WriteLine($"[WALL RESULT] phase=attempt_upgrade cycle={_debug.Cycle} resource={selectedResource} candidate_match_count={candidateMatchCount} requested_count={actualSelectedCount} verified_count={actualSelectedCount} cost={totalCost:N0} actual_spend={actualSpend:N0} expected_spend={expectedSpend:N0} status=upgraded reason=ok");
                    WallLogger.LogInfo("upgrade_batch", "upgraded", reason: "ok", cycle: cycle, trigger: trigger, runId: runId, elapsedMs: batchTimer.ElapsedMilliseconds, extra: $"resource={selectedResource} requested_count={actualSelectedCount} verified_count={actualSelectedCount} cost={totalCost:N0} actual_spend={actualSpend:N0} expected_spend={expectedSpend:N0} verified=true");
                    return WallTransactionResult.Verified(selectedResource, actualSelectedCount, totalCost, candidateMatchCount, actualSelectedCount);
                }
                else if (hudReadFailed)
                {
                    // Could not read HUD after confirm: outcome is unknown.
                    Console.WriteLine($"[WALL RESULT] phase=confirm_verify cycle={_debug.Cycle} resource={selectedResource} status=unknown reason=post_confirm_outcome_unknown before={resourceBefore:N0} expected_spend={expectedSpend:N0}");
                    WallLogger.LogInfo("upgrade_batch", "unknown", reason: "post_confirm_outcome_unknown", cycle: cycle, trigger: trigger, runId: runId, elapsedMs: batchTimer.ElapsedMilliseconds, extra: $"resource={selectedResource} requested_count={actualSelectedCount} verified_count=0 resource_before={resourceBefore:N0} resource_after=0 expected_spend={expectedSpend:N0} actual_spend=0 verified=false attempted_unverified=true");
                    return new WallTransactionResult(0, "post_confirm_outcome_unknown", Resource: selectedResource, CandidateMatchCount: candidateMatchCount, RequestedCount: actualSelectedCount);
                }
                else
                {
                    // HUD was readable but delta does not match: probable under-buy or OCR drift.
                    Console.WriteLine($"[WALL RESULT] phase=confirm_verify cycle={_debug.Cycle} resource={selectedResource} status=unknown reason=post_confirm_delta_mismatch before={resourceBefore:N0} after={resourceAfter:N0} expected_spend={expectedSpend:N0} actual_spend={actualSpend:N0} tolerance={tolerance:N0}");
                    WallLogger.LogInfo("upgrade_batch", "unknown", reason: "post_confirm_delta_mismatch", cycle: cycle, trigger: trigger, runId: runId, elapsedMs: batchTimer.ElapsedMilliseconds, extra: $"resource={selectedResource} requested_count={actualSelectedCount} verified_count=0 resource_before={resourceBefore:N0} resource_after={resourceAfter:N0} expected_spend={expectedSpend:N0} actual_spend={actualSpend:N0} tolerance={tolerance:N0} verified=false attempted_unverified=true");
                    return new WallTransactionResult(0, "post_confirm_delta_mismatch", Resource: selectedResource, CandidateMatchCount: candidateMatchCount, RequestedCount: actualSelectedCount);
                }
            }
            catch (Exception ex)
            {
                WallLogger.LogInfo("upgrade_batch", "fail", reason: "exception", cycle: cycle, trigger: trigger, runId: runId, elapsedMs: batchTimer.ElapsedMilliseconds, extra: $"ex_type=\"{ex.GetType().Name}\" ex_msg=\"{ex.Message}\"");
                throw;
            }
        }

        public void ResetSavedOffset()
        {
            _selector.ResetSavedOffset();
        }
    }
}
