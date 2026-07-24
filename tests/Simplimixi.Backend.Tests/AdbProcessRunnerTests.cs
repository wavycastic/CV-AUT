using System;
using CvAut.Adb;
using Xunit;

namespace CvAut.Backend.Tests
{
    public class AdbProcessRunnerTests
    {
        [Fact]
        public void Constructor_RejectsInvalidConfiguration()
        {
            Assert.Throws<ArgumentException>(() => new AdbProcessRunner(string.Empty));
            Assert.Throws<ArgumentOutOfRangeException>(() => new AdbProcessRunner("adb.exe", 0));
        }

        [Fact]
        public void RunRawAdbCommand_ReturnsStandardOutputOnSuccess()
        {
            var runner = new AdbProcessRunner(GetCommandProcessor());

            string result = runner.RunRawAdbCommand("/d /c echo adb-runner-ok");

            Assert.Equal("adb-runner-ok", result);
        }

        [Fact]
        public void RunRawAdbCommand_ReturnsErrorForNonZeroExitCode()
        {
            var runner = new AdbProcessRunner(GetCommandProcessor());

            string result = runner.RunRawAdbCommand("/d /c \"echo command-failed 1>&2 & exit /b 7\"");

            Assert.StartsWith("Error:", result, StringComparison.Ordinal);
            Assert.Contains("command-failed", result, StringComparison.Ordinal);
        }

        [Fact]
        public void RunRawAdbCommand_KillsTimedOutProcess()
        {
            var runner = new AdbProcessRunner(GetCommandProcessor(), timeoutMs: 50);

            string result = runner.RunRawAdbCommand("/d /c ping 127.0.0.1 -n 3 > nul");

            Assert.Equal("Error: ADB command timed out after 50 ms", result);
        }

        private static string GetCommandProcessor()
            => Environment.GetEnvironmentVariable("ComSpec") ?? "cmd.exe";
    }
}
