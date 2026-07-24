using System;
using System.IO;
using OpenCvSharp;

namespace CvAut;

internal sealed class TrainingVision
{
    private readonly VisionEngine _vision;
    private readonly string _root;

    public TrainingVision(VisionEngine vision, string root)
    {
        _vision = vision;
        _root = root;
    }

    public bool TryMatch(
        string subdirectory,
        string name,
        Mat image,
        double threshold,
        out Point center,
        Rect? roi = null)
    {
        center = default;
        string templateName = Path.Combine(subdirectory, name);
        if (image.Empty() || !TemplateAssetLoader.Exists(_root, templateName)) return false;
        using Mat template = TemplateAssetLoader.Load(_root, templateName, ImreadModes.Color);
        if (template.Empty()) return false;

        Rect searchRect = roi ?? new Rect(0, 0, image.Width, image.Height);
        searchRect = ImageUtils.ClampRect(searchRect, image.Width, image.Height);
        if (searchRect.Width < template.Width || searchRect.Height < template.Height) return false;
        using Mat searchArea = new(image, searchRect);
        using Mat result = new();
        Cv2.MatchTemplate(searchArea, template, result, TemplateMatchModes.CCoeffNormed);
        Cv2.MinMaxLoc(result, out _, out double score, out _, out Point maxLocation);
        center = new Point(
            searchRect.X + maxLocation.X + template.Width / 2,
            searchRect.Y + maxLocation.Y + template.Height / 2);
        return score >= threshold;
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
