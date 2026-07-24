using System;
using System.Collections.Generic;
using CvAut.Adb;
using Xunit;

namespace CvAut.Backend.Tests
{
    public class AdbInputControllerTests
    {
        [Fact]
        public void Constructor_RejectsNullRunner()
        {
            Assert.Throws<ArgumentNullException>(() => new AdbInputController(null!));
        }

        [Fact]
        public void Tap_EmitsExpectedDeviceCommand()
        {
            var runner = new RecordingAdbCommandRunner();
            var controller = new AdbInputController(runner);

            controller.Tap("127.0.0.1:5556", 120, 340);

            Assert.Equal(
                new AdbInvocation("127.0.0.1:5556", "shell input tap 120 340"),
                Assert.Single(runner.DeviceCommands));
        }

        [Fact]
        public void Swipe_EmitsExpectedDeviceCommand()
        {
            var runner = new RecordingAdbCommandRunner();
            var controller = new AdbInputController(runner);

            controller.Swipe("emulator-5554", 10, 20, 30, 40, 450);

            Assert.Equal(
                new AdbInvocation("emulator-5554", "shell input swipe 10 20 30 40 450"),
                Assert.Single(runner.DeviceCommands));
        }

        private sealed class RecordingAdbCommandRunner : IAdbCommandRunner
        {
            public string AdbExePath => "adb.exe";

            public List<AdbInvocation> DeviceCommands { get; } = new();

            public string RunAdbCommand(string deviceAddress, string arguments)
            {
                DeviceCommands.Add(new AdbInvocation(deviceAddress, arguments));
                return string.Empty;
            }

            public string RunRawAdbCommand(string arguments)
                => throw new NotSupportedException();
        }

        private sealed record AdbInvocation(string DeviceAddress, string Arguments);
    }
}
