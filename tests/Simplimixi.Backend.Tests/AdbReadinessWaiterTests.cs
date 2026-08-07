using System;
using System.Collections.Generic;
using System.Threading;
using CvAut;
using OpenCvSharp;
using Xunit;

namespace CvAut.Backend.Tests
{
    public class AdbReadinessWaiterTests
    {
        private sealed class BootStubAdbHelper : IADBHelper
        {
            private readonly string _bootCompleted;
            public List<string> ShellCommands { get; } = new();

            public BootStubAdbHelper(string bootCompleted)
            {
                _bootCompleted = bootCompleted;
            }

            public string Host => "127.0.0.1";
            public int Port => 5556;
            public string DeviceAddress => "127.0.0.1:5556";
            public FramePacer FramePacer { get; } = new FramePacer();
            public Func<bool>? BeforeInputAction { get; set; }

            public bool IsDeviceConnected() => true;
            public bool EnsureConnectedOnline(int timeoutSeconds = 30) => true;
            public string GetDeviceState() => "device";

            public string ExecuteShell(string command)
            {
                ShellCommands.Add(command);
                if (command.Contains("sys.boot_completed", StringComparison.Ordinal))
                {
                    return _bootCompleted;
                }
                return string.Empty;
            }

            public void Tap(int x, int y) { }
            public void TapSequence(IEnumerable<Point> points) { }
            public void TapSequenceSafeFast(IEnumerable<Point> points, int batchSize = 4, int batchDelayMs = 90) { }
            public void TapSequenceSafeFast(IEnumerable<Point> points, int batchSize, int batchDelayMs, CancellationToken token) { }
            public void Swipe(int startX, int startY, int endX, int endY, int durationMs = 300) { }
            public Mat? TakeScreenshot() => null;
            public void PinchIn(int centerX = 800, int centerY = 450) { }
            public bool PinchInZoomOut(int count = 5, int durationMs = 450, int intervalMs = 350) => true;
            public void Dispose() { }
        }

        [Fact]
        public void WaitForOnline_BootCompleted_ReturnsTrueWithoutDelay()
        {
            var adb = new BootStubAdbHelper("1");

            bool ready = AdbReadinessWaiter.WaitForOnline(adb, CancellationToken.None);

            Assert.True(ready);
            Assert.Contains(adb.ShellCommands, c => c.Contains("sys.boot_completed", StringComparison.Ordinal));
        }

        [Fact]
        public void WaitForOnline_AndroidStillBooting_DoesNotReturnTrue()
        {
            var adb = new BootStubAdbHelper("0");

            using var cts = new CancellationTokenSource(3000);
            bool ready = AdbReadinessWaiter.WaitForOnline(adb, cts.Token);

            Assert.False(ready);
        }
    }
}
