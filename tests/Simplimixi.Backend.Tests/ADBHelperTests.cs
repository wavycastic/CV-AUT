using System;
using System.Collections.Generic;
using System.Threading;
using CvAut.Adb;
using OpenCvSharp;
using SharpAdbClient;
using Xunit;

namespace CvAut.Backend.Tests
{
    public class ADBHelperTests
    {
        [Fact]
        public void Facade_ExposesCoordinatorConnectionMetadata()
        {
            using var adb = CreateHelper(new RecordingShellExecutor(), new RecordingGestureClient());

            Assert.Equal("127.0.0.1", adb.Host);
            Assert.Equal(5556, adb.Port);
            Assert.Equal("127.0.0.1:5556", adb.DeviceAddress);
            Assert.Equal("device", adb.GetDeviceState());
            Assert.True(adb.IsDeviceConnected());
        }

        [Fact]
        public void Tap_DelegatesToInputCapability()
        {
            var shell = new RecordingShellExecutor();
            using var adb = CreateHelper(shell, new RecordingGestureClient());

            adb.Tap(120, 340);

            Assert.Equal("input tap 120 340", Assert.Single(shell.Commands));
        }

        [Fact]
        public void BeforeInputAction_BlocksDelegatedInput()
        {
            var shell = new RecordingShellExecutor();
            using var adb = CreateHelper(shell, new RecordingGestureClient());
            adb.BeforeInputAction = () => true;

            adb.Swipe(1, 2, 3, 4);

            Assert.Empty(shell.Commands);
        }

        [Fact]
        public void TapSequenceSafeFast_PreservesBatchingBehavior()
        {
            var shell = new RecordingShellExecutor();
            using var adb = CreateHelper(shell, new RecordingGestureClient());

            adb.TapSequenceSafeFast(
                new[] { new Point(1, 1), new Point(2, 2), new Point(3, 3) },
                batchSize: 2,
                batchDelayMs: 0);

            Assert.Equal(2, shell.Commands.Count);
            Assert.Equal("input tap 1 1; input tap 2 2", shell.Commands[0]);
            Assert.Equal("input tap 3 3", shell.Commands[1]);
        }

        [Fact]
        public void Dispose_IsIdempotentAndDisposesGestureCapability()
        {
            var gesture = new RecordingGestureClient();
            var adb = CreateHelper(new RecordingShellExecutor(), gesture);

            adb.Dispose();
            adb.Dispose();

            Assert.Equal(1, gesture.DisposeCount);
        }

        private static ADBHelper CreateHelper(
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
            var coordinator = new AdbCapabilityCoordinator(
                connection,
                shell,
                new AdbInputController(shell),
                new NullScreenCapturer(),
                gesture);
            return new ADBHelper(coordinator);
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
            public int DisposeCount { get; private set; }

            public bool PinchIn(
                int count,
                int percent = 100,
                int steps = 20,
                int intervalMs = 350,
                CancellationToken token = default)
                => false;

            public void Dispose() => DisposeCount++;
        }
    }
}
