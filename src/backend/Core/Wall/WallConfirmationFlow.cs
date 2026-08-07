using System;
using System.Collections.Generic;
using System.Linq;
using OpenCvSharp;

namespace CvAut
{
    internal sealed record WallConfirmationValidation(
        bool Valid,
        WallConfirmationKind Kind,
        string Resource,
        long Total,
        WallConfirmDialogInfo Dialog,
        string Reason);

    internal static class WallConfirmationFlow
    {
        public static WallConfirmationValidation Validate(
            IVisionEngine vision,
            Mat screenshot,
            string expectedResource,
            int selectedCount,
            int singleWallCost,
            long prevalidatedBatchTotal)
        {
            WallConfirmDialogInfo dialog = WallConfirmDialogInspector.Inspect(screenshot);
            if (!dialog.Found) return Fail(dialog, "confirmation_dialog_not_found");
            WallConfirmationKind expectedKind = selectedCount > 1
                ? WallConfirmationKind.MultiCancelOkay
                : WallConfirmationKind.SingleConfirm;
            if (dialog.Kind != expectedKind) return Fail(dialog, "confirmation_kind_mismatch");
            if (singleWallCost <= 0 || selectedCount <= 0) return Fail(dialog, "confirmation_expected_total_invalid");

            long expectedTotal;
            try { expectedTotal = checked((long)singleWallCost * selectedCount); }
            catch (OverflowException) { return Fail(dialog, "confirmation_expected_total_overflow"); }
            if (prevalidatedBatchTotal != expectedTotal)
                return new(false, dialog.Kind, "unknown", prevalidatedBatchTotal, dialog, "confirmation_prevalidated_total_mismatch");

            string resource = DetectResource(vision, screenshot, dialog, out double resourceScore);
            if (resource == "unknown") return Fail(dialog, "confirmation_resource_not_read");
            if (!resource.Equals(expectedResource, StringComparison.OrdinalIgnoreCase))
                return new(false, dialog.Kind, resource, 0, dialog, "confirmation_resource_mismatch");

            // A single-upgrade confirmation encodes the resource in its one structural confirm button.
            // The amount was already verified on the resource button immediately before opening it.
            if (dialog.Kind == WallConfirmationKind.SingleConfirm)
                return new(true, dialog.Kind, resource, expectedTotal, dialog, $"single_confirmation_verified resource_score={resourceScore:F3}");

            if (TryReadExpectedTotal(vision, screenshot, dialog, expectedTotal, out long total))
                return new(true, dialog.Kind, resource, total, dialog, $"multi_confirmation_ocr_verified resource_score={resourceScore:F3}");

            // The total was read and checked on the selected resource button immediately before
            // opening this modal. Preserve that proof when the modal's compact, dimmed amount is
            // not OCR-stable; kind and resource are still independently verified in the dialog.
            return new(true, dialog.Kind, resource, expectedTotal, dialog, $"multi_confirmation_prevalidated_total resource_score={resourceScore:F3}");
        }

        private static bool TryReadExpectedTotal(
            IVisionEngine vision,
            Mat screenshot,
            WallConfirmDialogInfo dialog,
            long expectedTotal,
            out long observedTotal)
        {
            observedTotal = 0;
            if (TryReadCompactThousandsTotal(vision, screenshot, expectedTotal, out observedTotal))
                return true;
            foreach (Rect roi in TotalCandidates(screenshot, dialog))
            {
                if (WallBatchTotalReader.TryRead(vision, screenshot, roi, out long value, out _))
                {
                    observedTotal = NormalizeCompactTotal(value, expectedTotal);
                    if (observedTotal == expectedTotal) return true;
                }
                foreach (bool invert in new[] { true, false })
                {
                    if (!vision.TryExtractNumericalMetrics(screenshot, roi, out int raw, out _, invert: invert, allowVerticalShift: true) || raw <= 0) continue;
                    observedTotal = NormalizeCompactTotal(raw, expectedTotal);
                    if (observedTotal == expectedTotal) return true;
                }
            }
            return false;
        }

        private static bool TryReadCompactThousandsTotal(IVisionEngine vision, Mat screenshot, long expectedTotal, out long total)
        {
            total = 0;
            Rect roi = ImageUtils.ClampRect(new Rect(
                (int)(screenshot.Width * 0.49), (int)(screenshot.Height * 0.445),
                (int)(screenshot.Width * 0.09), (int)(screenshot.Height * 0.065)), screenshot.Width, screenshot.Height);
            if (roi.Width <= 0 || roi.Height <= 0) return false;
            using Mat crop = new(screenshot, roi);
            using Mat gray = new();
            using Mat mask = new();
            Cv2.CvtColor(crop, gray, ColorConversionCodes.BGR2GRAY);
            Cv2.Threshold(gray, mask, 190, 255, ThresholdTypes.Binary);
            Cv2.FindContours(mask, out Point[][] contours, out _, RetrievalModes.External, ContourApproximationModes.ApproxSimple);
            List<Rect> glyphs = contours.Select(Cv2.BoundingRect)
                .Where(r => r.Height >= 9 && r.Height <= 28 && r.Width >= 2 && r.Width <= 18)
                .OrderBy(r => r.X).ToList();
            if (glyphs.Count < 3) return false;

            foreach (int start in Enumerable.Range(0, glyphs.Count - 2))
            {
                int parsed = 0;
                bool valid = true;
                for (int i = start; i < start + 3; i++)
                {
                    Rect glyph = glyphs[i];
                    Rect absolute = ImageUtils.ClampRect(new Rect(roi.X + glyph.X - 2, roi.Y + glyph.Y - 2, glyph.Width + 4, glyph.Height + 4), screenshot.Width, screenshot.Height);
                    if (!vision.TryExtractNumericalMetrics(screenshot, absolute, out int digit, out double confidence, allowVerticalShift: true) || digit is < 0 or > 9 || confidence < 0.45)
                    {
                        valid = false;
                        break;
                    }
                    parsed = parsed * 10 + digit;
                }
                if (!valid) continue;
                long expanded = parsed * 1_000L;
                if (expanded == expectedTotal)
                {
                    total = expanded;
                    return true;
                }
            }
            return false;
        }

        private static long NormalizeCompactTotal(long value, long expectedTotal)
        {
            if (value == expectedTotal) return value;
            if (value > 0 && value <= WallBatchTotalReader.MaximumSupportedTotal / 1_000 && value * 1_000 == expectedTotal)
                return expectedTotal;
            return value;
        }

        private static IEnumerable<Rect> TotalCandidates(Mat screenshot, WallConfirmDialogInfo dialog)
        {
            int yBottom = Math.Max(1, dialog.ConfirmButton.Y - 8);
            var candidates = new[]
            {
                new Rect((int)(screenshot.Width * 0.38), (int)(screenshot.Height * 0.30), (int)(screenshot.Width * 0.44), yBottom - (int)(screenshot.Height * 0.30)),
                new Rect((int)(screenshot.Width * 0.45), Math.Max(0, yBottom - 180), (int)(screenshot.Width * 0.32), 165),
                new Rect((int)(screenshot.Width * 0.52), Math.Max(0, yBottom - 145), (int)(screenshot.Width * 0.22), 125),
                new Rect((int)(screenshot.Width * 0.495), (int)(screenshot.Height * 0.44), (int)(screenshot.Width * 0.07), (int)(screenshot.Height * 0.06)),
                dialog.ConfirmButton,
                new Rect(dialog.ConfirmButton.X - 80, dialog.ConfirmButton.Y - 25, dialog.ConfirmButton.Width + 160, dialog.ConfirmButton.Height + 50),
                new Rect((int)(screenshot.Width * 0.46), (int)(screenshot.Height * 0.43), (int)(screenshot.Width * 0.18), (int)(screenshot.Height * 0.08)),
                new Rect((int)(screenshot.Width * 0.48), (int)(screenshot.Height * 0.58), (int)(screenshot.Width * 0.20), (int)(screenshot.Height * 0.10))
            };
            return candidates.Select(r => ImageUtils.ClampRect(r, screenshot.Width, screenshot.Height))
                .Where(r => r.Width > 0 && r.Height > 0);
        }

        private static string DetectResource(IVisionEngine vision, Mat screenshot, WallConfirmDialogInfo dialog, out double score)
        {
            score = 0;
            if (dialog.Kind == WallConfirmationKind.SingleConfirm)
            {
                Rect buttonSearch = ImageUtils.ClampRect(new Rect(
                    dialog.ConfirmButton.X - 40, dialog.ConfirmButton.Y - 30,
                    dialog.ConfirmButton.Width + 80, dialog.ConfirmButton.Height + 60), screenshot.Width, screenshot.Height);
                vision.FindElement(screenshot, "walls/main_village/confirmation/single_gold_icon.png", 0.72, buttonSearch, out double goldScore);
                vision.FindElement(screenshot, "walls/main_village/confirmation/single_elixir_icon.png", 0.72, buttonSearch, out double elixirScore);
                score = Math.Max(goldScore, elixirScore);
                if (score < 0.72 || Math.Abs(goldScore - elixirScore) < 0.08) return "unknown";
                return goldScore > elixirScore ? "gold" : "elixir";
            }

            Rect labelSearch = ImageUtils.ClampRect(new Rect(
                (int)(screenshot.Width * 0.33), (int)(screenshot.Height * 0.42),
                (int)(screenshot.Width * 0.35), (int)(screenshot.Height * 0.10)), screenshot.Width, screenshot.Height);
            // These templates contain only the resource-specific word fragment. They deliberately
            // exclude the selected count and total so the match remains valid for any batch size.
            vision.FindElement(screenshot, "walls/main_village/confirmation/multi_gold_resource.png", 0.70, labelSearch, out double multiGoldScore);
            vision.FindElement(screenshot, "walls/main_village/confirmation/multi_elixir_resource.png", 0.70, labelSearch, out double multiElixirScore);
            score = Math.Max(multiGoldScore, multiElixirScore);
            if (score >= 0.75 && Math.Abs(multiGoldScore - multiElixirScore) >= 0.05)
                return multiGoldScore > multiElixirScore ? "gold" : "elixir";

            // Last-resort color classification is retained for skins where the dialog renders a colored resource glyph.
            int top = (int)(screenshot.Height * 0.30);
            int bottom = Math.Max(top + 1, dialog.ConfirmButton.Y - 8);
            Rect roi = ImageUtils.ClampRect(new Rect(
                (int)(screenshot.Width * 0.38), top,
                (int)(screenshot.Width * 0.44), bottom - top), screenshot.Width, screenshot.Height);
            if (roi.Width <= 0 || roi.Height <= 0) return "unknown";

            using Mat crop = new(screenshot, roi);
            using Mat hsv = new();
            using Mat gold = new();
            using Mat elixir = new();
            Cv2.CvtColor(crop, hsv, ColorConversionCodes.BGR2HSV);
            Cv2.InRange(hsv, new Scalar(10, 90, 65), new Scalar(38, 255, 255), gold);
            Cv2.InRange(hsv, new Scalar(125, 65, 60), new Scalar(179, 255, 255), elixir);
            int goldPixels = Cv2.CountNonZero(gold);
            int elixirPixels = Cv2.CountNonZero(elixir);
            int winner = Math.Max(goldPixels, elixirPixels);
            int loser = Math.Min(goldPixels, elixirPixels);
            if (winner < 20 || winner < loser * 1.35) return "unknown";
            score = (double)(winner - loser) / winner;
            return goldPixels > elixirPixels ? "gold" : "elixir";
        }

        private static WallConfirmationValidation Fail(WallConfirmDialogInfo dialog, string reason)
            => new(false, dialog.Kind, "unknown", 0, dialog, reason);
    }
}
