using System;
using System.Collections.Generic;
using OpenCvSharp;

namespace CvAut;

internal sealed class TroopCountReader
{
    private readonly IADBHelper _adb;
    private readonly VisionEngine _vision;

    public TroopCountReader(IADBHelper adb, VisionEngine vision)
    {
        _adb = adb;
        _vision = vision;
    }

    public int Read(string troopKey, IReadOnlyDictionary<string, Point> tabs, out double confidence)
    {
        confidence = 0;
        if (!tabs.TryGetValue(troopKey, out Point tab)) return -1;
        using Mat? screenshot = _adb.TakeScreenshot();
        if (screenshot == null || screenshot.Empty()) return -1;

        Rect[] candidates =
        {
            Rect.FromLTRB(tab.X - 5, tab.Y - 94, tab.X + 72, tab.Y - 42),
            Rect.FromLTRB(tab.X + 22, tab.Y - 96, tab.X + 78, tab.Y - 50),
            Rect.FromLTRB(tab.X - 20, tab.Y - 98, tab.X + 78, tab.Y - 40)
        };
        foreach (Rect candidate in candidates)
        {
            Rect roi = ImageUtils.ClampRect(candidate, screenshot.Width, screenshot.Height);
            if (roi.Width <= 0 || roi.Height <= 0) continue;
            if (_vision.TryExtractNumericalMetrics(screenshot, roi, out int value, out confidence, useRgbThresh: true)
                && IsPlausible(value, confidence)) return value;
            if (_vision.TryExtractNumericalMetrics(screenshot, roi, out value, out confidence)
                && IsPlausible(value, confidence)) return value;
        }
        return -1;
    }

    private static bool IsPlausible(int value, double confidence)
        => confidence >= 0.55 && value is >= 0 and <= 99;
}
