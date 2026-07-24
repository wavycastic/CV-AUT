using System;
using OpenCvSharp;

namespace CvAut;

public interface IADBHelper : IDisposable
{
    string Host { get; }
    int Port { get; }
    bool IsDeviceConnected();
    void Tap(int x, int y);
    void Swipe(int startX, int startY, int endX, int endY, int durationMs = 300);
    Mat? TakeScreenshot();
    void PinchIn(int centerX = 800, int centerY = 450);
}
