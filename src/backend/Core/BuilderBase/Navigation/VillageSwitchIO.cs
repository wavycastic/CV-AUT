using OpenCvSharp;
using Point = OpenCvSharp.Point;

namespace CvAut
{
    /// <summary>
    /// Narrow IO surface used by the Builder Base navigation code so flows can be unit tested
    /// without a real emulator.
    /// </summary>
    internal interface IVillageSwitchIO
    {
        Mat? TakeScreenshot();
        Point? FindElement(Mat screenshot, string templateName, double threshold, Rect? roi, out double score);
        void Tap(int x, int y);
        void PinchInZoomOut(int count, int durationMs, int intervalMs);
    }

    /// <summary>
    /// Default implementation backed by the real ADB helper and vision engine.
    /// </summary>
    internal sealed class VillageSwitchIO : IVillageSwitchIO
    {
        private readonly IADBHelper _adb;
        private readonly IVisionEngine _vision;

        internal VillageSwitchIO(IADBHelper adb, IVisionEngine vision)
        {
            _adb = adb;
            _vision = vision;
        }

        public Mat? TakeScreenshot() => _adb.TakeScreenshot();

        public Point? FindElement(Mat screenshot, string templateName, double threshold, Rect? roi, out double score)
        {
            return _vision.FindElement(screenshot, templateName, threshold, roi, out score);
        }

        public void Tap(int x, int y) => _adb.Tap(x, y);

        public void PinchInZoomOut(int count, int durationMs, int intervalMs)
        {
            _adb.PinchInZoomOut(count, durationMs, intervalMs);
        }
    }
}
