using System;
using System.Collections.Generic;
using System.Linq;
using OpenCvSharp;

namespace CvAut
{
    internal sealed record WallCostRoiPairLocalization(
        bool Found,
        bool OcrVerified,
        Rect GoldRoi,
        Rect ElixirRoi,
        int GoldCost,
        int ElixirCost,
        double Score,
        string Method,
        string FailureReason);

    internal sealed record WallCostRoiSingleLocalization(
        bool Found,
        bool OcrVerified,
        Rect CostRoi,
        int Cost,
        double Score,
        string Method,
        string FailureReason);

    /// <summary>
    /// Locates the two wall-cost labels as a pair. Candidates are derived from the already-validated
    /// resource buttons, scored by digit geometry, then verified by cross-resource OCR agreement.
    /// </summary>
    internal static class WallCostRoiLocator
    {
        private sealed record CandidatePair(Rect GoldRoi, Rect ElixirRoi, double GeometryScore, string Method);
        private sealed record CandidateSingle(Rect Roi, double GeometryScore, string Method);

        internal static WallCostRoiPairLocalization LocalizePair(
            IVisionEngine vision,
            Mat screenshot,
            Rect goldButton,
            Rect elixirButton)
        {
            if (screenshot == null || screenshot.Empty()) return Failed("screenshot_invalid");

            List<CandidatePair> candidates = GeneratePairs(screenshot, goldButton, elixirButton);
            if (candidates.Count == 0) return Failed("cost_roi_candidates_empty");

            WallCostRoiPairLocalization? best = null;
            foreach (CandidatePair candidate in candidates.OrderByDescending(c => c.GeometryScore))
            {
                bool goldRead = WallUpdater.TryReadWallUpgradeCost(
                    vision, screenshot, candidate.GoldRoi, out int goldCost, out double goldConfidence);
                bool elixirRead = WallUpdater.TryReadWallUpgradeCost(
                    vision, screenshot, candidate.ElixirRoi, out int elixirCost, out double elixirConfidence);

                WallCostValidationResult validation = WallCostPolicy.ValidateWallCosts(goldCost, elixirCost);
                bool verified = goldRead &&
                    elixirRead &&
                    validation.IsValid;
                double ocrScore = verified
                    ? 2.0 + Math.Min(goldConfidence, elixirConfidence) + (goldCost == elixirCost ? 0.50 : 0.0)
                    : 0.0;
                double finalScore = candidate.GeometryScore + ocrScore;

                var result = new WallCostRoiPairLocalization(
                    true,
                    verified,
                    candidate.GoldRoi,
                    candidate.ElixirRoi,
                    goldRead ? goldCost : 0,
                    elixirRead ? elixirCost : 0,
                    finalScore,
                    candidate.Method,
                    verified ? string.Empty : "wall_cost_pair_not_verified");

                if (best == null ||
                    (result.OcrVerified && !best.OcrVerified) ||
                    (result.OcrVerified == best.OcrVerified && result.Score > best.Score))
                {
                    best = result;
                }
            }

            return best is { OcrVerified: true }
                ? best
                : Failed("wall_cost_pair_not_verified");
        }

        internal static WallCostRoiSingleLocalization LocalizeSingle(
            IVisionEngine vision,
            Mat screenshot,
            Rect goldButton)
        {
            if (screenshot == null || screenshot.Empty())
            {
                return new(false, false, default, 0, 0, string.Empty, "screenshot_invalid");
            }

            Rect button = ImageUtils.ClampRect(goldButton, screenshot.Width, screenshot.Height);
            if (button.Width < 40 || button.Height < 40)
            {
                return new(false, false, default, 0, 0, string.Empty, "gold_button_invalid");
            }

            // 1. Search band: the cost label sits inside the upper part of the Gold button.
            //    A small strip above the button is included so the button's own top border
            //    never becomes the search-region edge used by the touch checks below.
            Rect search = BuildGoldCostSearchRegion(screenshot, button);
            if (search.Width < 60 || search.Height < 30)
            {
                return new(false, false, default, 0, 0, string.Empty, "cost_search_region_invalid");
            }

            // 2-6. Per-digit contours -> icon/border rejection -> digit cluster bounding box
            //      -> border-touch rejection -> fixed safety margin.
            List<CandidateSingle> candidates = BuildDigitClusterCandidates(screenshot, search);
            // Red unaffordable labels are intentionally dark in grayscale and can disappear from
            // the contour search above. Add the same button-relative grid used by paired costs;
            // exact Gold-only price validation keeps this fallback fail-closed.
            candidates.AddRange(GenerateSingleGridCandidates(screenshot, button));
            if (candidates.Count == 0)
            {
                return new(false, false, default, 0, 0, string.Empty, "cost_digit_cluster_not_found");
            }

            // 7. OCR on the normalized ROIs only.
            WallCostRoiSingleLocalization? best = null;
            foreach (CandidateSingle candidate in candidates.OrderByDescending(c => c.GeometryScore))
            {
                bool read = WallUpdater.TryReadWallUpgradeCost(
                    vision, screenshot, candidate.Roi, out int cost, out double confidence);
                WallCostValidationResult validation = WallCostPolicy.ValidateGoldOnlyCost(cost);
                bool verified = read && validation.IsValid;
                double score = candidate.GeometryScore + (verified ? 2.0 + confidence : 0.0);
                var current = new WallCostRoiSingleLocalization(
                    true,
                    verified,
                    candidate.Roi,
                    read ? cost : 0,
                    score,
                    candidate.Method,
                    verified ? string.Empty : "wall_gold_cost_not_verified");
                if (best == null ||
                    (current.OcrVerified && !best.OcrVerified) ||
                    (current.OcrVerified == best.OcrVerified && current.Score > best.Score))
                {
                    best = current;
                }
            }

            // 8. Fail-closed when no safe, OCR-verified bounding box exists.
            return best is { OcrVerified: true }
                ? best
                : new(false, false, default, 0, 0, string.Empty, "wall_gold_cost_not_verified");
        }

        private static Rect BuildGoldCostSearchRegion(Mat screenshot, Rect button)
        {
            int above = Math.Max(4, (int)Math.Round(button.Height * 0.12));
            int side = Math.Max(2, (int)Math.Round(button.Width * 0.02));
            return ImageUtils.ClampRect(
                new Rect(button.X - side, button.Y - above, button.Width + (side * 2), button.Height + above),
                screenshot.Width,
                screenshot.Height);
        }

        /// <summary>
        /// Detects the individual cost digits inside the search region, merges them into a single
        /// bounding box and pads it with a fixed safety margin. Contours that touch the region border,
        /// that sit inside the resource-badge corner, or that are wider than tall (icons, button edges)
        /// are rejected, so a clipped or shifted crop can never win just because OCR happened to read
        /// a whitelisted value.
        /// </summary>
        private static List<CandidateSingle> BuildDigitClusterCandidates(Mat screenshot, Rect search)
        {
            var output = new List<CandidateSingle>();
            double[] thresholds = { WallUiLayout.WallCostOcrThreshold, WallUiLayout.WallCostConsensusThreshold };
            double[] marginRatios = { 0.35, 0.60, 0.90 };

            using Mat crop = new(screenshot, search);
            using Mat gray = new();
            Cv2.CvtColor(crop, gray, ColorConversionCodes.BGR2GRAY);

            foreach (double threshold in thresholds)
                foreach (bool invert in new[] { false, true })
                {
                    using Mat binary = new();
                    // The cost label is light-on-dark on most panels, but the capsule variant renders
                    // dark digits, so both polarities are evaluated and scored independently.
                    Cv2.Threshold(gray, binary, threshold, 255, invert ? ThresholdTypes.BinaryInv : ThresholdTypes.Binary);
                    // RETR_LIST is required: the button border forms one enclosing contour, so an
                    // external-only retrieval would hide every digit inside the button.
                    Cv2.FindContours(binary, out Point[][] contours, out _, RetrievalModes.List, ContourApproximationModes.ApproxSimple);

                    List<Rect> glyphs = FilterDigitGlyphs(contours, search);
                    if (glyphs.Count < 3) continue;

                    foreach (List<Rect> cluster in GroupDigitClusters(glyphs))
                    {
                        if (cluster.Count < 3) continue;

                        int left = cluster.Min(r => r.X);
                        int top = cluster.Min(r => r.Y);
                        int right = cluster.Max(r => r.X + r.Width);
                        int bottom = cluster.Max(r => r.Y + r.Height);

                        // 5. The whole digit group must stay clear of the search-region border.
                        if (left <= 2 || top <= 2 || right >= search.Width - 2 || bottom >= search.Height - 2) continue;

                        double medianHeight = Median(cluster.Select(r => (double)r.Height));
                        if (medianHeight <= 0) continue;

                        foreach (double marginRatio in marginRatios)
                        {
                            int padX = Math.Max(6, (int)Math.Round(medianHeight * marginRatio));
                            int padY = Math.Max(4, (int)Math.Round(medianHeight * marginRatio * 0.7));

                            int x = Math.Max(search.X + 1, search.X + left - padX);
                            int y = Math.Max(search.Y + 1, search.Y + top - padY);
                            int x2 = Math.Min(search.X + search.Width - 1, search.X + right + padX);
                            int y2 = Math.Min(search.Y + search.Height - 1, search.Y + bottom + padY);
                            if (x2 - x < 24 || y2 - y < 14) continue;

                            Rect roi = ImageUtils.ClampRect(
                                new Rect(x, y, x2 - x, y2 - y),
                                screenshot.Width,
                                screenshot.Height);
                            if (roi.Width < 24 || roi.Height < 14) continue;

                            double countScore = cluster.Count is >= 4 and <= 8 ? 0.35 : 0.15;
                            // 6b. Prefer the tightest safety margin that still verifies, so a needlessly
                            //     padded crop can never outrank a snug one on OCR luck alone.
                            double marginPenalty = marginRatio * 0.20;
                            double geometryScore = ScoreDigitGeometry(screenshot, roi) + countScore - marginPenalty;
                            output.Add(new CandidateSingle(
                                roi,
                                geometryScore,
                                $"digit_cluster_t{(int)threshold}{(invert ? "_inv" : string.Empty)}_m{(int)Math.Round(marginRatio * 100)}"));
                        }
                    }
                }

            return output
                .GroupBy(c => c.Roi)
                .Select(g => g.OrderByDescending(c => c.GeometryScore).First())
                .ToList();
        }

        private static List<Rect> FilterDigitGlyphs(Point[][] contours, Rect search)
        {
            int minDigitHeight = Math.Max(8, (int)Math.Round(search.Height * 0.07));
            int maxDigitHeight = Math.Max(minDigitHeight + 1, (int)Math.Round(search.Height * 0.34));

            // 3a. Reject the button frame, wide banners and anything that touches the search
            //     border, because a glyph clipped by the region edge is never a complete digit.
            var raw = new List<Rect>();
            foreach (Point[] contour in contours)
            {
                Rect r = Cv2.BoundingRect(contour);
                if (r.Height < minDigitHeight || r.Height > maxDigitHeight) continue;
                if (r.Width < 2 || r.Width > r.Height * 1.60) continue;

                // 3a-bis. Reject thin open arcs: the resource-coin rim and panel decorations
                //         produce contours that barely fill their bounding box (f < 0.20),
                //         whereas every real digit fills roughly half of it. Without this, a
                //         coin-rim fragment joins the digit cluster and drags the merged
                //         bounding box over the badge.
                double fillRatio = Cv2.ContourArea(contour) / Math.Max(1.0, r.Width * (double)r.Height);
                if (fillRatio < 0.20) continue;

                if (r.X <= 1 || r.Y <= 1 ||
                    r.X + r.Width >= search.Width - 1 ||
                    r.Y + r.Height >= search.Height - 1) continue;
                raw.Add(r);
            }

            if (raw.Count == 0) return raw;

            // 3b. RETR_LIST returns both the outline and the inner hole of glyphs such as 0 and 8;
            //     keep only the outermost rectangle of each nested group.
            var deduped = new List<Rect>();
            foreach (Rect r in raw.OrderByDescending(r => r.Width * r.Height))
            {
                bool nested = deduped.Any(kept =>
                    r.X >= kept.X - 2 && r.Y >= kept.Y - 2 &&
                    r.X + r.Width <= kept.X + kept.Width + 2 &&
                    r.Y + r.Height <= kept.Y + kept.Height + 2);
                if (!nested) deduped.Add(r);
            }

            // 3c. Digits of one label share a height. The Gold coin badge and other decorations
            //     differ from the digit median, so drop everything outside a tight height band.
            double medianHeight = Median(deduped.Select(r => (double)r.Height));
            List<Rect> glyphs = deduped
                .Where(r => r.Height >= medianHeight * 0.65 &&
                            r.Height <= medianHeight * 1.45 &&
                            r.Width <= r.Height * 1.35)
                .ToList();

            glyphs.Sort((a, b) => a.X.CompareTo(b.X));
            return glyphs;
        }

        /// <summary>Groups glyphs that share a baseline and are horizontally adjacent into one label.</summary>
        private static List<List<Rect>> GroupDigitClusters(List<Rect> glyphs)
        {
            double medianHeight = Median(glyphs.Select(r => (double)r.Height));
            double baselineTolerance = Math.Max(3.0, medianHeight * 0.45);
            double maxGap = Math.Max(6.0, medianHeight * 1.30);

            var clusters = new List<List<Rect>>();
            foreach (Rect g in glyphs)
            {
                List<Rect>? target = null;
                foreach (List<Rect> cluster in clusters)
                {
                    double baseline = cluster.Average(r => r.Y + r.Height);
                    double rightEdge = cluster.Max(r => r.X + r.Width);
                    if (Math.Abs(baseline - (g.Y + g.Height)) <= baselineTolerance &&
                        g.X - rightEdge <= maxGap)
                    {
                        target = cluster;
                        break;
                    }
                }

                if (target == null)
                {
                    clusters.Add(new List<Rect> { g });
                }
                else
                {
                    target.Add(g);
                }
            }

            return clusters;
        }

        private static double Median(IEnumerable<double> values)
        {
            double[] sorted = values.OrderBy(v => v).ToArray();
            if (sorted.Length == 0) return 0;
            int mid = sorted.Length / 2;
            return sorted.Length % 2 == 1
                ? sorted[mid]
                : (sorted[mid - 1] + sorted[mid]) / 2.0;
        }

        private static List<CandidateSingle> GenerateSingleGridCandidates(Mat screenshot, Rect button)
        {
            int screenWidth = screenshot.Width;
            int screenHeight = screenshot.Height;
            int baseWidth = Math.Max(95, (int)Math.Round(screenWidth * (135.0 / 1600.0)));
            int baseHeight = Math.Max(25, (int)Math.Round(screenHeight * (35.0 / 900.0)));
            int[] xOffsets = { 0, 4, 10, 16, 22 };
            int[] dyValues = { -8, 0, 8 };
            int maxWidth = button.Width - 8;
            int[] widthValues = { baseWidth, maxWidth };
            var output = new List<CandidateSingle>();

            foreach ((string Name, bool IsBottom) layout in new[] { ("top", false), ("bottom", true) })
            {
                int baseY = layout.IsBottom
                    ? (int)(screenHeight * (90.0 / 900.0))
                    : button.Height <= (int)(screenHeight * (165.0 / 900.0))
                        ? 6
                        : (int)(screenHeight * (28.0 / 900.0));

                foreach (int xOffset in xOffsets)
                    foreach (int dy in dyValues)
                        foreach (int width in widthValues.Where(v => v >= 80).Distinct())
                        {
                            Rect roi = BuildRoi(screenshot, button, xOffset, baseY + dy, width, baseHeight);
                            if (!IsUsable(roi)) continue;
                            double widthCoverage = roi.Width / (double)Math.Max(1, maxWidth);
                            double score = ScoreDigitGeometry(screenshot, roi) + (widthCoverage * 0.15);
                            output.Add(new CandidateSingle(
                                roi,
                                score,
                                $"single_grid_{layout.Name}_x{xOffset}_dy{dy}_w{width}"));
                        }
            }

            return output;
        }

        private static List<CandidatePair> GeneratePairs(Mat screenshot, Rect goldButton, Rect elixirButton)
        {
            int screenWidth = screenshot.Width;
            int screenHeight = screenshot.Height;
            int baseWidth = Math.Max(95, (int)Math.Round(screenWidth * (135.0 / 1600.0)));
            int baseHeight = Math.Max(25, (int)Math.Round(screenHeight * (35.0 / 900.0)));
            int[] xOffsets = { 0, 4, 10, 16, 22 };
            int[] dyValues = { -8, 0, 8 };
            int maxPairWidth = Math.Min(goldButton.Width, elixirButton.Width) - 8;
            int[] widthValues = { baseWidth, maxPairWidth };
            var output = new List<CandidatePair>();

            foreach ((string Name, bool IsBottom) layout in new[]
            {
                ("top", false),
                ("bottom", true)
            })
            {
                int goldBaseY = layout.IsBottom
                    ? (int)(screenHeight * (90.0 / 900.0))
                    : goldButton.Height <= (int)(screenHeight * (165.0 / 900.0))
                        ? 6
                        : (int)(screenHeight * (28.0 / 900.0));
                int elixirBaseY = layout.IsBottom
                    ? (int)(screenHeight * (90.0 / 900.0))
                    : elixirButton.Height <= (int)(screenHeight * (165.0 / 900.0))
                        ? 6
                        : (int)(screenHeight * (28.0 / 900.0));

                foreach (int xOffset in xOffsets)
                    foreach (int dy in dyValues)
                        foreach (int width in widthValues.Where(v => v >= 80).Distinct())
                        {
                            Rect goldRoi = BuildRoi(screenshot, goldButton, xOffset, goldBaseY + dy, width, baseHeight);
                            Rect elixirRoi = BuildRoi(screenshot, elixirButton, xOffset, elixirBaseY + dy, width, baseHeight);
                            if (!IsUsable(goldRoi) || !IsUsable(elixirRoi)) continue;

                            double goldScore = ScoreDigitGeometry(screenshot, goldRoi);
                            double elixirScore = ScoreDigitGeometry(screenshot, elixirRoi);
                            double pairBalance = 1.0 - Math.Min(1.0, Math.Abs(goldScore - elixirScore));
                            double widthCoverage = (goldRoi.Width + elixirRoi.Width) / (2.0 * maxPairWidth);
                            double score = ((goldScore + elixirScore) / 2.0) +
                                (pairBalance * 0.20) +
                                (widthCoverage * 0.15);
                            output.Add(new CandidatePair(goldRoi, elixirRoi, score, $"pair_grid_{layout.Name}_x{xOffset}_dy{dy}_w{width}"));
                        }
            }

            return output;
        }

        private static Rect BuildRoi(Mat screenshot, Rect button, int offsetX, int offsetY, int width, int height)
        {
            Rect buttonBounded = ImageUtils.ClampRect(button, screenshot.Width, screenshot.Height);
            Rect candidate = ImageUtils.ClampRect(
                new Rect(button.X + offsetX, button.Y + offsetY, width, height),
                screenshot.Width,
                screenshot.Height);
            return Intersect(candidate, buttonBounded);
        }

        private static Rect Intersect(Rect left, Rect right)
        {
            int x = Math.Max(left.X, right.X);
            int y = Math.Max(left.Y, right.Y);
            int rightEdge = Math.Min(left.X + left.Width, right.X + right.Width);
            int bottomEdge = Math.Min(left.Y + left.Height, right.Y + right.Height);
            return rightEdge > x && bottomEdge > y
                ? new Rect(x, y, rightEdge - x, bottomEdge - y)
                : default;
        }

        private static bool IsUsable(Rect roi) => roi.Width >= 80 && roi.Height >= 22;

        internal static double ScoreDigitGeometry(Mat screenshot, Rect roi)
        {
            using Mat crop = new(screenshot, roi);
            using Mat gray = new();
            using Mat binary = new();
            Cv2.CvtColor(crop, gray, ColorConversionCodes.BGR2GRAY);
            Cv2.Threshold(gray, binary, WallUiLayout.WallCostConsensusThreshold, 255, ThresholdTypes.Binary);

            using Mat redBinary = Mat.Zeros(crop.Size(), MatType.CV_8UC1);
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
                        redBinary.Set(y, x, (byte)255);
                    }
                }
            }

            return Math.Max(ScoreDigitMask(binary, roi), ScoreDigitMask(redBinary, roi));
        }

        private static double ScoreDigitMask(Mat binary, Rect roi)
        {
            Cv2.FindContours(binary, out Point[][] contours, out _, RetrievalModes.External, ContourApproximationModes.ApproxSimple);

            List<Rect> digits = contours
                .Select(Cv2.BoundingRect)
                .Where(r => r.Height >= 8 && r.Height < roi.Height && r.Width > 1 && r.Width < 35)
                .OrderBy(r => r.X)
                .ToList();
            if (digits.Count < 2) return 0;

            double countScore = digits.Count is >= 4 and <= 8 ? 1.0 : digits.Count <= 10 ? 0.55 : 0.15;
            double meanHeight = digits.Average(r => r.Height);
            double heightSpread = digits.Max(r => r.Height) - digits.Min(r => r.Height);
            double heightScore = Math.Max(0, 1.0 - (heightSpread / Math.Max(1.0, meanHeight)));
            int[] baselines = digits.Select(r => r.Y + r.Height).ToArray();
            double baselineScore = Math.Max(0, 1.0 - ((baselines.Max() - baselines.Min()) / Math.Max(1.0, meanHeight)));
            double edgePenalty = digits.Any(r => r.X <= 1 || r.X + r.Width >= roi.Width - 1) ? 0.35 : 0.0;
            double foregroundRatio = Cv2.CountNonZero(binary) / (double)(roi.Width * roi.Height);
            double foregroundScore = foregroundRatio is >= 0.03 and <= 0.70 ? 1.0 : 0.2;

            return Math.Max(0,
                (countScore * 0.30) +
                (baselineScore * 0.30) +
                (heightScore * 0.25) +
                (foregroundScore * 0.15) -
                edgePenalty);
        }

        private static bool IsPlausibleWallCost(int value)
            => WallCostPolicy.IsPlausibleWallCost(value);

        private static WallCostRoiPairLocalization Failed(string reason)
            => new(false, false, default, default, 0, 0, 0, string.Empty, reason);
    }
}
