using System;
using System.Collections.Generic;
using System.Threading;
using CvAut.Adb;
using OpenCvSharp;
using SharpAdbClient;
using Xunit;

namespace CvAut.Backend.Tests
{
    public class AdbCapabilityCoordinatorTests
    {
        [Fact]
        public void InputGuard_BlocksTapBeforeShellExecution()
        {
            var shell = new RecordingShellExecutor();
            using var coordinator = CreateCoordinator(shell, new RecordingGestureClient());
            coordinator.BeforeInputAction = () => true;

            bool sent = coordinator.Tap(10, 20);

            Assert.False(sent);
            Assert.Empty(shell.Commands);
        }

        [Fact]
        public void Tap_DelegatesToInputCapability()
        {
            var shell = new RecordingShellExecutor();
            using var coordinator = CreateCoordinator(shell, new RecordingGestureClient());

            bool sent = coordinator.Tap(10, 20);

            Assert.True(sent);
            Assert.Equal("input tap 10 20", Assert.Single(shell.Commands));
        }

        [Fact]
        public void PinchInZoomOut_WhenRpcFails_UsesShellFallback()
        {
            var shell = new RecordingShellExecutor();
            using var coordinator = CreateCoordinator(
                shell,
                new RecordingGestureClient { Result = false });

            bool sent = coordinator.PinchInZoomOut(count: 1, durationMs: 450, intervalMs: 0);

            Assert.True(sent);
            string command = Assert.Single(shell.Commands);
            Assert.Contains("input swipe 360 450 790 450 450", command, StringComparison.Ordinal);
            Assert.Contains("input swipe 1240 450 810 450 450", command, StringComparison.Ordinal);
        }

        [Fact]
        public void Dispose_DisposesGestureCapability()
        {
            var gesture = new RecordingGestureClient();
            var coordinator = CreateCoordinator(new RecordingShellExecutor(), gesture);

            coordinator.Dispose();

            Assert.True(gesture.IsDisposed);
        }

        private static AdbCapabilityCoordinator CreateCoordinator(
            RecordingShellExecutor shell,
            RecordingGestureClient gesture)
        {
            var device = new DeviceData { Serial = "127.0.0.1:5556" };
            var connection = new AdbDeviceConnection(
                "127.0.0.1",
                5556,
                device.Serial,
                device,
                true);
            return new AdbCapabilityCoordinator(
                connection,
                shell,
                new AdbInputController(shell),
                new NullScreenCapturer(),
                gesture);
        }

        private sealed class RecordingShellExecutor : IAdbShellExecutor
        {
            public List<string> Commands { get; } = new();

            public string Execute(string command)
            {
                Commands.Add(command);
                return string.Empty;
            }
        }

        private sealed class NullScreenCapturer : IAdbScreenCapturer
        {
            public Mat? Capture(string deviceAddress, FramePacer framePacer, CancellationToken token = default)
                => null;
        }

        private sealed class RecordingGestureClient : IUiAutomatorGestureClient
        {
            public bool Result { get; init; }
            public bool IsDisposed { get; private set; }

            public bool PinchIn(
                int count,
                int percent = 100,
                int steps = 20,
                int intervalMs = 350,
                CancellationToken token = default)
                => Result;

            public void Dispose() => IsDisposed = true;
        }
    }
}
