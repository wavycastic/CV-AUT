using System;
using System.Linq;
using OpenCvSharp;

namespace CvAut;

internal delegate Point? FindElementDelegate(Mat screenshot, string templateName, double threshold, Rect? roi, out double score);

internal static class TemplateSearch
{
    public static Point? FindFirst(Mat screenshot, FindElementDelegate findElement,
        string[] templates, double threshold, Rect? roi, out string matched, out double score)
    {
        matched = string.Empty;
        score = 0;

        foreach (string template in templates.Where(t => !string.IsNullOrWhiteSpace(t)))
        {
            Point? center = findElement(screenshot, template, threshold, roi, out double s);
            if (center == null) continue;
            matched = template;
            score = s;
            return center;
        }

        return null;
    }

    public static bool TryFindFirst(Mat screenshot, FindElementDelegate findElement,
        string[] templates, double threshold, Rect? roi,
        out string matched, out double score, out Point center)
    {
        Point? found = FindFirst(screenshot, findElement, templates, threshold, roi, out matched, out score);
        if (found == null)
        {
            center = default;
            return false;
        }
        center = found.Value;
        return true;
    }

    public static bool IsAnyVisible(Mat screenshot, FindElementDelegate findElement,
        string[] templates, double threshold, Rect? roi)
    {
        return FindFirst(screenshot, findElement, templates, threshold, roi, out _, out _) != null;
    }

    public static bool TryTapFirst(Mat screenshot, FindElementDelegate findElement,
        string[] templates, double threshold, Rect? roi, Action<int, int> tap,
        out string matched, out double score, out Point center)
    {
        if (!TryFindFirst(screenshot, findElement, templates, threshold, roi, out matched, out score, out center))
            return false;

        tap(center.X, center.Y);
        return true;
    }
}
