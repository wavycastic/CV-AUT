using OpenCvSharp;

namespace CvAut;

/// <summary>
/// Shared image processing utilities used across multiple components.
/// </summary>
public static class ImageUtils
{
    /// <summary>
    /// Clamps a rectangle to fit within image bounds.
    /// </summary>
    public static Rect ClampRect(Rect rect, int width, int height)
    {
        int left = Math.Clamp(rect.Left, 0, width);
        int top = Math.Clamp(rect.Top, 0, height);
        int right = Math.Clamp(rect.Right, left, width);
        int bottom = Math.Clamp(rect.Bottom, top, height);
        return Rect.FromLTRB(left, top, right, bottom);
    }
}
