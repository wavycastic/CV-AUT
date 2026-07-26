using System;
using System.IO;
using OpenCvSharp;

namespace CvAut;

internal sealed class TrainingVision
{
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
            diagnostic = $"roi_smaller_than_template roi={searchRect.Width}x{searchRect.Height} template={template.Width}x{template.Height}";
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

        diagnostic = "score_below_threshold";
        return false;
    }

    public bool TryMatchRoot(
        string templateName,
        Mat image,
        double threshold,
        out Point center,
        Rect? roi = null)
        => TryMatch(string.Empty, templateName, image, threshold, out center, roi);

    public bool TryReadFraction(Mat image, Rect roi, out int current, out int capacity)
    {
        current = 0;
        capacity = 0;
        if (!_vision.TryExtractNumericalMetrics(image, roi, out int value, out double confidence, useRgbThresh: true)
            && !_vision.TryExtractNumericalMetrics(image, roi, out value, out confidence))
        {
            return false;
        }

        string digits = value.ToString();
        if (confidence < 0.50 || digits.Length < 2 || digits.Length % 2 != 0) return false;
        int half = digits.Length / 2;
        return int.TryParse(digits[..half], out current)
            && int.TryParse(digits[half..], out capacity);
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
