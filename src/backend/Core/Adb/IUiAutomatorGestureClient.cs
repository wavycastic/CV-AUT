using System;
using System.Threading;

namespace CvAut.Adb
{
    internal interface IUiAutomatorGestureClient : IDisposable
    {
        bool PinchIn(int count, int percent = 100, int steps = 20, int intervalMs = 350, CancellationToken token = default);
    }
}
