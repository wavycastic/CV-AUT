using System;
using OpenCvSharp;

namespace CvAut;

public interface IVisionEngine
{
    string TemplatesPath { get; }
    string TemplatesDirectory => TemplatesPath;

    Point? FindElement(Mat screenshot, string templateName, double threshold, Rect roi, out double maxVal);

    Point? FindElement(Mat screenshot, string templateName, double threshold, Rect? roi, out double score);

    bool ContainsElement(Mat screenshot, string templateName, double threshold, Rect roi);

    bool TryFindTemplate(Mat source, string templatePath, Rect? roi, double threshold, out Point center, out double score);

    bool TryExtractNumericalMetrics(
        Mat screenshot,
        Rect roi,
        out int value,
        out double confidence,
        bool isOffline = false,
        bool useRgbThresh = false,
        bool invert = false,
        bool allowVerticalShift = false);

    int ExtractNumericalMetrics(
        Mat screenshot,
        Rect roi,
        bool isOffline = false,
        bool useRgbThresh = false,
        bool invert = false,
        bool allowVerticalShift = false) => TryExtractNumericalMetrics(screenshot, roi, out int value, out _, isOffline, useRgbThresh, invert, allowVerticalShift) ? value : 0;

    int OcrReadNumber(Mat croppedImage);

    (int Gold, int Elixir, int DarkElixir) ExtractScoutedLoot(Mat screenshot);
}
