using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using OpenCvSharp;

namespace CvAut;

internal sealed class TroopCountReader
{
    private readonly IADBHelper _adb;
    private readonly IVisionEngine _vision;

    public TroopCountReader(IADBHelper adb, IVisionEngine vision)
    {
        _adb = adb;
        _vision = vision;
    }

    public int Read(
        string troopKey,
        IReadOnlyDictionary<string, Point> tabs,
        int maximumExpected,
        out double confidence,
        out string diagnostic,
        bool captureDebug = false)
    {
        using Mat? screenshot = _adb.TakeScreenshot();
        if (screenshot == null || screenshot.Empty())
        {
            confidence = 0;
            diagnostic = $"reason=screenshot_empty max_expected={maximumExpected}";
            return -1;
        }
        return Read(screenshot, troopKey, tabs, maximumExpected, out confidence, out diagnostic, captureDebug);
    }

    public int Read(
        Mat screenshot,
        string troopKey,
        IReadOnlyDictionary<string, Point> tabs,
        int maximumExpected,
        out double confidence,
        out string diagnostic,
        bool captureDebug = false)
    {
        confidence = 0;
        diagnostic = string.Empty;
        if (!tabs.TryGetValue(troopKey, out Point tab))
        {
            diagnostic = $"reason=tab_missing max_expected={maximumExpected}";
            return -1;
        }
        if (screenshot == null || screenshot.Empty())
        {
            diagnostic = $"reason=screenshot_empty tab={tab.X},{tab.Y} max_expected={maximumExpected}";
            return -1;
        }

        bool spellBadge = troopKey.Equals("rage", StringComparison.OrdinalIgnoreCase)
            || troopKey.Equals("freeze", StringComparison.OrdinalIgnoreCase);
        var candidates = new List<Rect>();
        bool quantityBadgeFound = TryBuildQuantityRoi(
            screenshot,
            tab,
            maximumExpected,
            out Rect quantityRoi);
        if (quantityBadgeFound)
        {
            candidates.Add(quantityRoi);
        }

        if (!quantityBadgeFound && !spellBadge)
        {
            diagnostic = $"reason=quantity_badge_absent tab={tab.X},{tab.Y} max_expected={maximumExpected}";
            return -1;
        }
        if (spellBadge)
        {
            candidates.AddRange(new[]
            {
                // Spell buttons show the spell level immediately before the quantity: level 6
                // Rage x4 and Freeze x3 were read as 64/63. Crop the rightmost quantity glyph.
                Rect.FromLTRB(tab.X + 30, tab.Y - 94, tab.X + 72, tab.Y - 42),
                Rect.FromLTRB(tab.X + 29, tab.Y - 96, tab.X + 74, tab.Y - 40),
                Rect.FromLTRB(tab.X + 31, tab.Y - 92, tab.X + 72, tab.Y - 42)
            });
        }
        var samples = new List<string>();
        for (int candidateIndex = 0; candidateIndex < candidates.Count; candidateIndex++)
        {
            Rect roi = ImageUtils.ClampRect(candidates[candidateIndex], screenshot.Width, screenshot.Height);
            if (roi.Width <= 0 || roi.Height <= 0)
            {
                samples.Add($"c{candidateIndex + 1}:invalid_roi");
                continue;
            }

            if (TryReadCandidate(screenshot, roi, candidateIndex + 1, true, maximumExpected, samples, out int value, out confidence)
                || TryReadCandidate(screenshot, roi, candidateIndex + 1, false, maximumExpected, samples, out value, out confidence))
            {
                diagnostic = $"reason=accepted tab={tab.X},{tab.Y} max_expected={maximumExpected} samples={string.Join('|', samples)}";
                return value;
            }
        }

        string debug = captureDebug
            ? DumpFailureCrops(troopKey, screenshot, candidates)
            : "not_captured";
        diagnostic = $"reason=no_candidate_accepted tab={tab.X},{tab.Y} max_expected={maximumExpected} samples={string.Join('|', samples)} debug={debug}";
        return -1;
    }

    internal static bool TryBuildQuantityRoi(
        Mat screenshot,
        Point tab,
        int maximumExpected,
        out Rect quantityRoi)
    {
        quantityRoi = default;
        if (screenshot == null || screenshot.Empty()) return false;

        Rect badgeBand = ImageUtils.ClampRect(
            Rect.FromLTRB(tab.X - 10, tab.Y - 75, tab.X + 85, tab.Y - 38),
            screenshot.Width,
            screenshot.Height);
        if (badgeBand.Width <= 0 || badgeBand.Height <= 0) return false;

        using Mat crop = new(screenshot, badgeBand);
        using Mat threshold = new();
        Cv2.InRange(crop, new Scalar(180, 180, 180), new Scalar(255, 255, 255), threshold);
        Cv2.FindContours(
            threshold,
            out Point[][] contours,
            out _,
            RetrievalModes.External,
            ContourApproximationModes.ApproxSimple);

        List<Rect> glyphs = contours
            .Select(Cv2.BoundingRect)
            .Where(rect => rect.Height >= 13
                && rect.Height < 30
                && rect.Width > 2
                && rect.Width < 30)
            .OrderBy(rect => rect.X)
            .ToList();
        if (glyphs.Count < 2) return false;

        int maxDigits = Math.Max(
            1,
            Math.Max(0, maximumExpected).ToString(CultureInfo.InvariantCulture).Length);
        List<Rect> digits = glyphs.Skip(1).TakeLast(maxDigits).ToList();
        if (digits.Count == 0) return false;

        int left = digits.Min(rect => rect.Left);
        int top = digits.Min(rect => rect.Top);
        int right = digits.Max(rect => rect.Right);
        int bottom = digits.Max(rect => rect.Bottom);
        quantityRoi = ImageUtils.ClampRect(
            Rect.FromLTRB(
                badgeBand.X + left - 2,
                badgeBand.Y + top - 3,
                badgeBand.X + right + 2,
                badgeBand.Y + bottom + 3),
            screenshot.Width,
            screenshot.Height);
        return quantityRoi.Width > 0 && quantityRoi.Height > 0;
    }

    private bool TryReadCandidate(
        Mat screenshot,
        Rect roi,
        int candidateIndex,
        bool useRgbThreshold,
        int maximumExpected,
        List<string> samples,
        out int value,
        out double confidence)
    {
        bool hasResult = _vision.TryExtractNumericalMetrics(
            screenshot,
            roi,
            out value,
            out confidence,
            useRgbThresh: useRgbThreshold);
        string reason = ClassifySample(hasResult, value, confidence, maximumExpected);
        string method = useRgbThreshold ? "rgb" : "gray";
        samples.Add(FormattableString.Invariant(
            $"c{candidateIndex}:{method}:value={value}:confidence={confidence:F2}:reason={reason}:roi={roi.X},{roi.Y},{roi.Width},{roi.Height}"));
        return reason == "accepted";
    }

    internal static string ClassifySample(bool hasResult, int value, double confidence, int maximumExpected)
    {
        if (!hasResult) return "no_result";
        if (confidence < 0.55) return "low_confidence";
        if (value < 0 || value > Math.Max(0, maximumExpected)) return "out_of_range";
        return "accepted";
    }

    private static string DumpFailureCrops(string troopKey, Mat screenshot, IReadOnlyList<Rect> candidates)
    {
        try
        {
            string directory = Path.Combine("logs", "attack_ocr_debug");
            Directory.CreateDirectory(directory);
            string stamp = DateTime.Now.ToString("yyyyMMdd_HHmmss_fff", CultureInfo.InvariantCulture);
            string safeKey = troopKey.Replace('/', '_').Replace('\\', '_').Replace(' ', '_');
            var paths = new List<string>();
            for (int index = 0; index < candidates.Count; index++)
            {
                Rect roi = ImageUtils.ClampRect(candidates[index], screenshot.Width, screenshot.Height);
                if (roi.Width <= 0 || roi.Height <= 0) continue;
                using Mat crop = new(screenshot, roi);
                string path = Path.Combine(directory, $"{stamp}_{safeKey}_c{index + 1}.png");
                if (Cv2.ImWrite(path, crop)) paths.Add(path.Replace('\\', '/'));
            }
            return paths.Count == 0 ? "capture_empty" : string.Join(',', paths);
        }
        catch (Exception ex)
        {
            return $"capture_failed:{ex.GetType().Name}";
        }
    }

    internal static bool IsPlausible(int value, double confidence, int maximumExpected)
        => ClassifySample(true, value, confidence, maximumExpected) == "accepted";
}
