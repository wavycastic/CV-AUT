using System;
using System.Collections.Generic;
using System.Linq;
using OpenCvSharp;

namespace CvAut
{
    internal enum WallSelectionMode { Unknown, Single, Multi }

    internal sealed record WallHeaderInfo(bool Found, int Level, WallSelectionMode Mode, int SelectedCount, double Confidence, Rect Roi, string Reason);

    /// <summary>Reads the wall title. Multi mode is accepted only when a separate right-hand count is verified.</summary>
    internal static class WallHeaderInspector
    {
        public static WallHeaderInfo Inspect(IVisionEngine vision, Mat screenshot)
        {
            if (screenshot == null || screenshot.Empty()) return Fail("screenshot_invalid");
            Rect search = ImageUtils.ClampRect(new Rect(
                (int)(screenshot.Width * 0.30), (int)(screenshot.Height * 0.43),
                (int)(screenshot.Width * 0.40), (int)(screenshot.Height * 0.23)), screenshot.Width, screenshot.Height);
            if (search.Width <= 0 || search.Height <= 0) return Fail("header_roi_invalid");

            var reads = new List<(int Value, double Confidence, Rect Roi)>();
            int minW = Math.Max(24, screenshot.Width / 80);
            int maxW = Math.Max(minW + 1, screenshot.Width / 10);
            int stepX = Math.Max(8, screenshot.Width / 160);
            int[] widths = { minW, minW + stepX * 2, maxW };
            int sliceH = Math.Max(28, screenshot.Height / 25);
            int stepY = Math.Max(8, sliceH / 3);

            for (int y = search.Y; y + sliceH <= search.Bottom; y += stepY)
            for (int x = search.X; x < search.Right; x += stepX)
            foreach (int width in widths)
            {
                Rect roi = ImageUtils.ClampRect(new Rect(x, y, width, sliceH), screenshot.Width, screenshot.Height);
                if (roi.Right > search.Right || roi.Width < minW) continue;
                if (vision.TryExtractNumericalMetrics(screenshot, roi, out int number, out double conf, allowVerticalShift: true) &&
                    number is >= 1 and <= 255 && conf >= 0.55)
                    reads.Add((number, conf, roi));
            }

            if (reads.Count == 0) return new(false, 0, WallSelectionMode.Unknown, 0, 0, search, "header_numbers_unreadable");

            var clusters = new List<List<(int Value, double Confidence, Rect Roi)>>();
            foreach (var read in reads.OrderBy(r => r.Roi.X))
            {
                double cx = read.Roi.X + read.Roi.Width / 2.0;
                double cy = read.Roi.Y + read.Roi.Height / 2.0;
                var cluster = clusters.FirstOrDefault(c => c[0].Value == read.Value &&
                    Math.Abs((c.Average(v => v.Roi.X + v.Roi.Width / 2.0)) - cx) <= 45 &&
                    Math.Abs((c.Average(v => v.Roi.Y + v.Roi.Height / 2.0)) - cy) <= 18);
                if (cluster == null) clusters.Add(new() { read }); else cluster.Add(read);
            }

            var stable = clusters.Where(c => c.Count >= 3)
                .Select(c => new
                {
                    Value = c[0].Value,
                    X = c.Average(v => v.Roi.X + v.Roi.Width / 2.0),
                    Y = c.Average(v => v.Roi.Y + v.Roi.Height / 2.0),
                    Confidence = c.Max(v => v.Confidence),
                    Support = c.Count
                })
                .OrderBy(c => c.X).ToList();
            if (stable.Count == 0) return new(false, 0, WallSelectionMode.Unknown, 0, 0, search, "header_consensus_failed");

            var levelRead = stable.Where(c => c.Value <= 18).OrderBy(c => c.X).ThenByDescending(c => c.Support).FirstOrDefault();
            if (levelRead == null) return new(false, 0, WallSelectionMode.Unknown, 0, 0, search, "wall_level_unreadable");
            string diagnostic = string.Join(",", stable.Select(c => $"{c.Value}@{c.X:F0},{c.Y:F0}#{c.Support}"));
            if (TryInferCountFromPanelTotals(vision, screenshot, levelRead.Value, out int inferredCount, out double inferredConfidence))
            {
                if (inferredCount > 1)
                    return new(true, levelRead.Value, WallSelectionMode.Multi, inferredCount, Math.Min(levelRead.Confidence, inferredConfidence), search, $"multi_header_total_crosscheck reads={diagnostic}");
                return new(true, levelRead.Value, WallSelectionMode.Single, 1, Math.Min(levelRead.Confidence, inferredConfidence), search, $"single_header_total_crosscheck reads={diagnostic}");
            }
            if (TryVerifySinglePanelTotal(vision, screenshot, out double singlePanelConfidence))
                return new(true, levelRead.Value, WallSelectionMode.Single, 1, Math.Min(levelRead.Confidence, singlePanelConfidence), search, $"single_header_plausible_total reads={diagnostic}");
            if (TryReadRightHandCount(vision, screenshot, levelRead.X, levelRead.Y, out int structuralCount, out double structuralConfidence))
                return new(true, levelRead.Value, WallSelectionMode.Multi, structuralCount, Math.Min(levelRead.Confidence, structuralConfidence), search, $"multi_header_structural reads={diagnostic}");

            var count = stable.Where(c => c.X >= levelRead.X + 65 && Math.Abs(c.Y - levelRead.Y) <= 12 && c.Value != levelRead.Value)
                .OrderByDescending(c => c.Support).ThenByDescending(c => c.Confidence).FirstOrDefault();
            if (count != null)
                return new(true, levelRead.Value, WallSelectionMode.Multi, count.Value, Math.Min(levelRead.Confidence, count.Confidence), search, $"multi_header_verified support={count.Support} reads={diagnostic}");
            return new(true, levelRead.Value, WallSelectionMode.Single, 1, levelRead.Confidence, search, $"single_header_verified support={levelRead.Support} reads={diagnostic}");
        }

        private static bool TryInferCountFromPanelTotals(IVisionEngine vision, Mat screenshot, int wallLevel, out int count, out double confidence)
        {
            count = 0;
            confidence = 0;
            if (!TryGetBaseCostForLevel(wallLevel, out int singleCost)) return false;
            WallPanelLocalizationResult panel = WallDynamicLocalizer.LocalizePanelAndButtons(vision, screenshot);
            var candidates = new[] { panel.GoldInfo, panel.ElixirInfo }.Where(b => b.Found).ToList();
            var valid = new List<(int Count, double Confidence)>();
            foreach (WallResourceButtonInfo button in candidates)
            {
                if (!WallBatchTotalReader.TryRead(vision, screenshot, button.CostRoi, out long total, out double totalConfidence) || total <= 0) continue;
                if (total % singleCost != 0) continue;
                long ratio = total / singleCost;
                if (ratio is >= 1 and <= 255 && totalConfidence >= 0.65)
                    valid.Add(((int)ratio, totalConfidence));
            }
            if (valid.Count == 0) return false;
            var best = valid.OrderByDescending(v => v.Count > 1).ThenByDescending(v => v.Confidence).First();
            count = best.Count;
            confidence = best.Confidence;
            return true;
        }

        private static bool TryGetBaseCostForLevel(int level, out int cost)
        {
            cost = level switch
            {
                1 => 1_000, 2 => 5_000, 3 => 10_000, 4 => 20_000, 5 => 30_000,
                6 => 50_000, 7 => 75_000, 8 => 100_000, 9 => 200_000, 10 => 500_000,
                11 => 1_000_000, 12 => 1_500_000, 13 => 2_000_000, 14 => 3_000_000,
                15 => 4_000_000, 16 => 5_000_000, 17 => 7_000_000, 18 => 10_000_000,
                _ => 0
            };
            return cost > 0;
        }

        private static bool TryVerifySinglePanelTotal(IVisionEngine vision, Mat screenshot, out double confidence)
        {
            confidence = 0;
            WallPanelLocalizationResult panel = WallDynamicLocalizer.LocalizePanelAndButtons(vision, screenshot);
            foreach (WallResourceButtonInfo button in new[] { panel.GoldInfo, panel.ElixirInfo }.Where(b => b.Found))
            {
                if (!WallBatchTotalReader.TryRead(vision, screenshot, button.CostRoi, out long total, out double readConfidence)) continue;
                if (total <= int.MaxValue &&
                    (WallCostPolicy.IsPlausibleGoldOnlyWallCost((int)total) || WallCostPolicy.IsPlausibleWallCost((int)total)))
                {
                    confidence = readConfidence;
                    return true;
                }
            }
            return false;
        }

        private static bool TryReadRightHandCount(IVisionEngine vision, Mat screenshot, double levelX, double levelY, out int count, out double confidence)
        {
            count = 0;
            confidence = 0;
            int x = Math.Clamp((int)levelX + 80, 0, screenshot.Width - 1);
            int y = Math.Clamp((int)levelY - 28, 0, screenshot.Height - 1);
            Rect roi = ImageUtils.ClampRect(new Rect(x, y, Math.Min(320, screenshot.Width - x), 58), screenshot.Width, screenshot.Height);
            if (roi.Width <= 0 || roi.Height <= 0) return false;
            using Mat crop = new(screenshot, roi);
            using Mat gray = new();
            using Mat mask = new();
            Cv2.CvtColor(crop, gray, ColorConversionCodes.BGR2GRAY);
            Cv2.Threshold(gray, mask, 205, 255, ThresholdTypes.Binary);
            Cv2.FindContours(mask, out Point[][] contours, out _, RetrievalModes.External, ContourApproximationModes.ApproxSimple);
            List<Rect> glyphs = contours.Select(Cv2.BoundingRect)
                .Where(r => r.Height >= 10 && r.Height <= 35 && r.Width >= 2 && r.Width <= 24)
                .OrderBy(r => r.X).ToList();
            if (glyphs.Count < 2) return false;

            var digits = new List<(int X, int Digit, double Confidence)>();
            foreach (Rect glyph in glyphs)
            {
                Rect absolute = ImageUtils.ClampRect(new Rect(roi.X + glyph.X - 2, roi.Y + glyph.Y - 2, glyph.Width + 4, glyph.Height + 4), screenshot.Width, screenshot.Height);
                if (vision.TryExtractNumericalMetrics(screenshot, absolute, out int digit, out double conf, allowVerticalShift: true) && digit is >= 0 and <= 9 && conf >= 0.50)
                    digits.Add((absolute.X, digit, conf));
            }
            if (digits.Count == 0) return false;
            var sequence = new List<(int X, int Digit, double Confidence)> { digits[^1] };
            for (int i = digits.Count - 2; i >= 0; i--)
            {
                if (sequence[0].X - digits[i].X > 28) break;
                sequence.Insert(0, digits[i]);
            }
            if (sequence.Count > 3) sequence = sequence.TakeLast(3).ToList();
            int parsed = 0;
            foreach (var digit in sequence) parsed = parsed * 10 + digit.Digit;
            if (parsed is < 1 or > 255) return false;
            count = parsed;
            confidence = sequence.Min(d => d.Confidence);
            return true;
        }

        private static WallHeaderInfo Fail(string reason) => new(false, 0, WallSelectionMode.Unknown, 0, 0, default, reason);
    }
}
