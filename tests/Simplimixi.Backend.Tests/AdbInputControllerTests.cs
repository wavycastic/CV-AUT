using System;
using System.Collections.Generic;
using CvAut.Adb;
using OpenCvSharp;
using Xunit;

namespace CvAut.Backend.Tests
{
    public class AdbInputControllerTests
    {
        [Fact]
        public void Constructor_RejectsNullShellExecutor()
        {
            Assert.Throws<ArgumentNullException>(() => new AdbInputController(null!));
        }

        [Fact]
        public void Tap_EmitsExpectedShellCommand()
        {
            var shell = new RecordingShellExecutor();
            var controller = new AdbInputController(shell);

            string result = controller.Tap(120, 340);

            Assert.Equal("ok", result);
            Assert.Equal("input tap 120 340", Assert.Single(shell.Commands));
        }

        [Fact]
        public void Swipe_EmitsExpectedShellCommand()
        {
            var shell = new RecordingShellExecutor();
            var controller = new AdbInputController(shell);

            controller.Swipe(10, 20, 30, 40, 450);

            Assert.Equal("input swipe 10 20 30 40 450", Assert.Single(shell.Commands));
        }

        [Fact]
        public void TapSequence_JoinsCommandsInOneShellCall()
        {
            var shell = new RecordingShellExecutor();
            var controller = new AdbInputController(shell);

            controller.TapSequence(new[] { new Point(1, 2), new Point(3, 4) });

            Assert.Equal("input tap 1 2; input tap 3 4", Assert.Single(shell.Commands));
        }

        [Fact]
        public void TapSequence_EmptyCollection_DoesNotCallShell()
        {
            var shell = new RecordingShellExecutor();
            var controller = new AdbInputController(shell);

            string result = controller.TapSequence(Array.Empty<Point>());

            Assert.Equal(string.Empty, result);
            Assert.Empty(shell.Commands);
        }

        private sealed class RecordingShellExecutor : IAdbShellExecutor
        {
            public List<string> Commands { get; } = new();

            public string Execute(string command)
            {
                Commands.Add(command);
                return "ok";
            }
        }
    }
}
