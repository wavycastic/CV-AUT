using System.Threading;
using OpenCvSharp;

namespace CvAut.Adb
{
    internal interface IAdbScreenCapturer
    {
        Mat? Capture(string deviceAddress, FramePacer framePacer, CancellationToken token = default);
    }
}
