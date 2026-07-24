using System;
using System.Collections.Generic;
using System.Threading;
using CvAut.Adb;
using OpenCvSharp;
using SharpAdbClient;
using Xunit;

namespace CvAut.Backend.Tests
{
    public class AdbHelperFacadeTests
    {
        [Fact]
        public void TapSequenceSafeFast_SplitsPointsIntoConfiguredBatches()
        {
            var shell = new RecordingShellExecutor();
            using var facade = CreateFacade(shell);

            facade.TapSequenceSafeFast(
                new[]
                {
                    new Point(1, 1),
                    new Point(2, 2),
                    new Point(3, 3),
                    new Point(4, 4),
                    new Point(5, 5)
                },
                batchSize: 2,
                batchDelayMs: 0);

            Assert.Equal(3, shell.Commands.Count);
            Assert.Equal("input tap 1 1; input tap 2 2", shell.Commands[0]);
            Assert.Equal("input tap 3 3; input tap 4 4", shell.Commands[1]);
            Assert.Equal("input tap 5 5", shell.Commands[2]);
        }

        [Fact]
        public void TapSequenceSafeFast_CancelledToken_SendsNothing()
        {
            var shell = new RecordingShellExecutor();
            using var facade = CreateFacade(shell);
            using var cancellation = new CancellationTokenSource();
            cancellation.Cancel();

            facade.TapSequenceSafeFast(
                new[] { new Point(1, 1) },
                token: cancellation.Token);

            Assert.Empty(shell.Commands);
        }

        [Fact]
        public void BeforeInputAction_IsForwardedToCoordinator()
        {
            var shell = new RecordingShellExecutor();
            using var facade = CreateFacade(shell);
            facade.BeforeInputAction = () => true;

            facade.Swipe(1, 2, 3, 4);

            Assert.Empty(shell.Commands);
        }

        private static AdbHelperFacade CreateFacade(RecordingShellExecutor shell)
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
                new NullGestureClient());
            return new AdbHelperFacade(coordinator);
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

        private sealed class NullGestureClient : IUiAutomatorGestureClient
        {
            public bool PinchIn(
                int count,
                int percent = 100,
                int steps = 20,
                int intervalMs = 350,
                CancellationToken token = default)
                => false;

            public void Dispose()
            {
            }
        }
    }
}
