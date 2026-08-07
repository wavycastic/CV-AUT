using System.Diagnostics;
using Xunit;

namespace CvAut.Backend.Tests
{
    public class EmulatorProcessLauncherTests
    {
        [Fact]
        public void BuildStartInfo_BlueStacks_InstanceSelectorReachesArguments()
        {
            ProcessStartInfo info = EmulatorProcessLauncher.BuildStartInfo(
                @"C:\Program Files\BlueStacks_nxt\HD-Player.exe",
                "BlueStacks",
                "Pie64");

            Assert.True(info.UseShellExecute);
            Assert.Equal("--instance \"Pie64\"", info.Arguments);
        }

        [Fact]
        public void BuildStartInfo_NoInstance_KeepsArgumentsEmpty()
        {
            ProcessStartInfo info = EmulatorProcessLauncher.BuildStartInfo(@"C:\BlueStacks\HD-Player.exe", "BlueStacks");

            Assert.True(info.UseShellExecute);
            Assert.Equal(string.Empty, info.Arguments);
        }

        [Fact]
        public void BuildStartInfo_LDPlayer_NumericIndex_UsesIndexSyntax()
        {
            ProcessStartInfo info = EmulatorProcessLauncher.BuildStartInfo(@"C:\LDPlayer\dnplayer.exe", "LDPlayer", "2");

            Assert.Equal("index=2", info.Arguments);
        }

        [Fact]
        public void BuildStartInfo_LDPlayer_NamedInstance_UsesNameSyntax()
        {
            ProcessStartInfo info = EmulatorProcessLauncher.BuildStartInfo(@"C:\LDPlayer\dnplayer.exe", "LDPlayer", "Main");

            Assert.Equal("name=\"Main\"", info.Arguments);
        }

        [Fact]
        public void BuildStartInfo_MEmu_InstanceSelectorReachesArguments()
        {
            ProcessStartInfo info = EmulatorProcessLauncher.BuildStartInfo(@"C:\MEmu\MEmu.exe", "MEmu", "0");

            Assert.Equal("index=0", info.Arguments);
        }
    }
}
