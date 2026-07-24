using System;
using OpenCvSharp;

namespace CvAut;

public interface IVisionEngine
{
    string TemplatesPath { get; }
    Point? FindElement(Mat screenshot, string templateName, double threshold, Rect roi, out double maxVal);
    bool ContainsElement(Mat screenshot, string templateName, double threshold, Rect roi);
    int OcrReadNumber(Mat croppedImage);
    (int Gold, int Elixir, int DarkElixir) ExtractScoutedLoot(Mat screenshot);
}
