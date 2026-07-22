using System;
using System.Threading;

namespace CvAut.Adb
{
    /// <summary>
    /// Chuyên xử lý gửi các thao tác chạm (Tap), vuốt (Swipe) và cử chỉ tương tác ngầm với giả lập Android.
    /// </summary>
    internal class AdbInputController
    {
        private readonly AdbProcessRunner _runner;

        public AdbInputController(AdbProcessRunner runner)
        {
            _runner = runner;
        }

        public void Tap(string deviceAddress, int x, int y)
        {
            _runner.RunAdbCommand(deviceAddress, $"shell input tap {x} {y}");
        }

        public void Swipe(string deviceAddress, int x1, int y1, int x2, int y2, int durationMs = 300)
        {
            _runner.RunAdbCommand(deviceAddress, $"shell input swipe {x1} {y1} {x2} {y2} {durationMs}");
        }
    }
}
