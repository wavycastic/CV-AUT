using System.Collections.Generic;
using OpenCvSharp;

namespace CvAut.Adb
{
    internal interface IAdbInputController
    {
        string Tap(int x, int y);
        string Swipe(int x1, int y1, int x2, int y2, int durationMs = 300);
        string TapSequence(IEnumerable<Point> points);
    }
}
