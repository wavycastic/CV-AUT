using System;
using OpenCvSharp;

namespace CvAut;

public interface IVisionEngine
{
    string TemplatesPath { get; }

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
        bool invert = false);

    int OcrReadNumber(Mat croppedImage);

    (int Gold, int Elixir, int DarkElixir) ExtractScoutedLoot(Mat screenshot);
}
