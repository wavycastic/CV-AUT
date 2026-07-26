using System;
using System.Globalization;
using System.IO;
using OpenCvSharp;

namespace CvAut;

internal sealed class TrainingVision
{
    /// <summary>
    /// When set to 1, every failed icon match writes the searched region and the template it was
    /// compared against into <c>logs/train_match_debug</c>. Off by default: this writes two images
    /// per failure and is only meant to be switched on while investigating.
    /// </summary>
    private static readonly bool MatchDebugEnabled = string.Equals(
        Environment.GetEnvironmentVariable("CVAUT_TRAIN_MATCH_DEBUG"),
        "1",
        StringComparison.Ordinal);

    private readonly IVisionEngine _vision;
    private readonly string _root;

    public TrainingVision(IVisionEngine vision, string root)
    {
        _vision = vision;
        _root = root;
    }

    public bool TemplateExists(string subdirectory, string name)
        => TemplateAssetLoader.Exists(_root, Path.Combine(subdirectory, name));

    public bool TryMatch(
        string subdirectory,
        string name,
        Mat image,
        double threshold,
        out Point center,
        Rect? roi = null)
        => TryMatchWithScore(subdirectory, name, image, threshold, out center, out _, out _, roi);

    /// <summary>
    /// Same matching logic as <see cref="TryMatch"/>, but also reports the best correlation score
    /// and why the match failed.
    /// <para>
    /// Callers that turn a failure into a user-visible log line should use this method. A bare
    /// <c>false</c> cannot tell a missing template file apart from a search region that is smaller
    /// than the template or from a score that merely sat below the threshold, and naming the wrong
    /// cause sends debugging in the wrong direction.
    /// </para>
    /// </summary>
    public bool TryMatchWithScore(
        string subdirectory,
        string name,
        Mat image,
        double threshold,
        out Point center,
        out double score,
        out string diagnostic,
        Rect? roi = null)
    {
        center = default;
        score = 0;
        string templateName = Path.Combine(subdirectory, name);

        if (image.Empty())
        {
            diagnostic = "image_empty";
            return false;
        }

        if (!TemplateAssetLoader.Exists(_root, templateName))
        {
            diagnostic = "template_file_missing";
            return false;
        }

        using Mat template = TemplateAssetLoader.Load(_root, templateName, ImreadModes.Color);
        if (template.Empty())
        {
            diagnostic = "template_unreadable";
            return false;
        }

        Rect searchRect = roi ?? new Rect(0, 0, image.Width, image.Height);
        searchRect = ImageUtils.ClampRect(searchRect, image.Width, image.Height);
        if (searchRect.Width < template.Width || searchRect.Height < template.Height)
        {
            diagnostic = FormattableString.Invariant(
                $"roi_smaller_than_template roi={searchRect.Width}x{searchRect.Height} template={template.Width}x{template.Height}");
            return false;
        }

        using Mat searchArea = new(image, searchRect);
        using Mat result = new();
        Cv2.MatchTemplate(searchArea, template, result, TemplateMatchModes.CCoeffNormed);
        Cv2.MinMaxLoc(result, out _, out score, out _, out Point maxLocation);
        center = new Point(
            searchRect.X + maxLocation.X + template.Width / 2,
            searchRect.Y + maxLocation.Y + template.Height / 2);

        if (score >= threshold)
        {
            diagnostic = "matched";
            return true;
        }

        diagnostic = FormattableString.Invariant(
            $"score_below_threshold roi={searchArea.Width}x{searchArea.Height} template={template.Width}x{template.Height} at={maxLocation.X},{maxLocation.Y}{DescribeScaleSweep(searchArea, template)}");
        DumpDebugImages(templateName, searchArea, template);
        return false;
    }

    /// <summary>
    /// Retries the match with the template rescaled between 0.50x and 1.40x and reports the best
    /// result.
    /// <para>
    /// This distinguishes the two remaining causes of a very low score. A high score at a scale
    /// other than 1.00 means the templates were captured at a different resolution than the one
    /// being rendered now, and the matcher needs to be scale aware. A low score at every scale
    /// means the searched region is not showing the icon at all, and the region rectangle is what
    /// needs fixing. Runs only after a failure, so a healthy match does not pay for it.
    /// </para>
    /// </summary>
    private static string DescribeScaleSweep(Mat searchArea, Mat template)
    {
        double bestScore = -1;
        double bestScale = 0;

        for (int step = 10; step <= 28; step++)
        {
            double scale = step / 20.0;
            int width = (int)Math.Round(template.Width * scale);
            int height = (int)Math.Round(template.Height * scale);
            if (width < 8 || height < 8) continue;
            if (width > searchArea.Width || height > searchArea.Height) continue;

            using Mat resized = new();
            Cv2.Resize(template, resized, new Size(width, height), 0, 0, InterpolationFlags.Area);
            using Mat result = new();
            Cv2.MatchTemplate(searchArea, resized, result, TemplateMatchModes.CCoeffNormed);
            Cv2.MinMaxLoc(result, out _, out double scaleScore, out _, out _);
            if (scaleScore > bestScore)
            {
                bestScore = scaleScore;
                bestScale = scale;
            }
        }

        return bestScore < 0
            ? string.Empty
            : FormattableString.Invariant($" best_scale={bestScale:F2} best_scale_score={bestScore:F2}");
    }

    private static void DumpDebugImages(string templateName, Mat searchArea, Mat template)
    {
        if (!MatchDebugEnabled) return;

        try
        {
            string directory = Path.Combine("logs", "train_match_debug");
            Directory.CreateDirectory(directory);
            string stamp = DateTime.Now.ToString("yyyyMMdd_HHmmss_fff", CultureInfo.InvariantCulture);
            string safeName = templateName
                .Replace('/', '_')
                .Replace('\\', '_')
                .Replace(' ', '_');
            Cv2.ImWrite(Path.Combine(directory, $"{stamp}_{safeName}_searched.png"), searchArea);
            Cv2.ImWrite(Path.Combine(directory, $"{stamp}_{safeName}_template.png"), template);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[TRAIN] phase=match_debug status=fail reason=\"{ex.Message}\"");
        }
    }

    public bool TryMatchRoot(
        string templateName,
        Mat image,
        double threshold,
        out Point center,
        Rect? roi = null)
        => TryMatch(string.Empty, templateName, image, threshold, out center, roi);

    public bool TryReadFraction(Mat image, Rect roi, out int current, out int capacity)
        => TryReadFraction(image, roi, out current, out capacity, out _);

    /// <summary>
    /// Reads a "current/capacity" indicator, also reporting what the OCR produced and why the
    /// reading was rejected.
    /// <para>
    /// The engine returns the whole region as one integer, so the digits of both numbers arrive
    /// concatenated and are split down the middle. That split is only meaningful when both numbers
    /// have the same digit count, which is why an odd digit count is rejected outright. A caller
    /// that turns this rejection into a decision as consequential as wiping the training queue
    /// needs to know which of these cases it hit, so every rejection names itself.
    /// </para>
    /// </summary>
    public bool TryReadFraction(Mat image, Rect roi, out int current, out int capacity, out string diagnostic)
    {
        current = 0;
        capacity = 0;
        if (!_vision.TryExtractNumericalMetrics(image, roi, out int value, out double confidence, useRgbThresh: true)
            && !_vision.TryExtractNumericalMetrics(image, roi, out value, out confidence))
        {
            diagnostic = "ocr_no_result";
            return false;
        }

        string digits = value.ToString(CultureInfo.InvariantCulture);

        if (confidence < 0.50)
        {
            diagnostic = FormattableString.Invariant(
                $"ocr_low_confidence confidence={confidence:F2} digits={digits}");
            return false;
        }

        if (digits.Length < 2)
        {
            diagnostic = FormattableString.Invariant(
                $"ocr_too_few_digits confidence={confidence:F2} digits={digits}");
            return false;
        }

        if (digits.Length % 2 != 0)
        {
            diagnostic = FormattableString.Invariant(
                $"ocr_odd_digit_count confidence={confidence:F2} digits={digits}");
            return false;
        }

        int half = digits.Length / 2;
        if (!int.TryParse(digits[..half], out current) || !int.TryParse(digits[half..], out capacity))
        {
            diagnostic = FormattableString.Invariant($"split_failed confidence={confidence:F2} digits={digits}");
            return false;
        }

        diagnostic = FormattableString.Invariant(
            $"read confidence={confidence:F2} digits={digits} current={current} capacity={capacity}");
        return true;
    }

    public int? ReadNumber(Mat image, Rect roi, int minimum = 0)
    {
        if (_vision.TryExtractNumericalMetrics(image, roi, out int value, out _, useRgbThresh: true)
            && value >= minimum)
        {
            return value;
        }
        return null;
    }

    public static Mat Crop(Mat image, Rect roi)
        => new(image, ImageUtils.ClampRect(roi, image.Width, image.Height));
}
