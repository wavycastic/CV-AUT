namespace CvAut.Adb
{
    /// <summary>
    /// Translates device input operations into ADB commands.
    /// </summary>
    internal sealed class AdbInputController
    {
        private readonly IAdbCommandRunner _runner;

        public AdbInputController(IAdbCommandRunner runner)
        {
            _runner = runner ?? throw new System.ArgumentNullException(nameof(runner));
        }

        public void Tap(string deviceAddress, int x, int y)
        {
            _runner.RunAdbCommand(deviceAddress, $"shell input tap {x} {y}");
        }

        public void Swipe(string deviceAddress, int x1, int y1, int x2, int y2, int durationMs = 300)
        {
            _runner.RunAdbCommand(
                deviceAddress,
                $"shell input swipe {x1} {y1} {x2} {y2} {durationMs}");
        }
    }
}
