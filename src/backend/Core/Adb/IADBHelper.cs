using System;
using System.Collections.Generic;
using System.Threading;
using OpenCvSharp;

namespace CvAut;

public interface IADBHelper : IDisposable
{
    string Host { get; }
    int Port { get; }
    string DeviceAddress { get; }
    FramePacer FramePacer { get; }
    Func<bool>? BeforeInputAction { get; set; }

    bool IsDeviceConnected();
    bool EnsureConnectedOnline(int timeoutSeconds = 30);
    string GetDeviceState();
    string ExecuteShell(string command);

    void Tap(int x, int y);
    void TapSequence(IEnumerable<Point> points);
    void TapSequenceSafeFast(IEnumerable<Point> points, int batchSize = 4, int batchDelayMs = 90);
    void TapSequenceSafeFast(
        IEnumerable<Point> points,
        int batchSize,
        int batchDelayMs,
        CancellationToken token);
    void Swipe(int startX, int startY, int endX, int endY, int durationMs = 300);

    Mat? TakeScreenshot();
    void PinchIn(int centerX = 800, int centerY = 450);
    bool PinchInZoomOut(int count = 5, int durationMs = 450, int intervalMs = 350);
}
