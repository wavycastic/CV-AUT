using System;
using System.Threading;
using CvAut;
using CvAut.Automation;
using CvAut.Configuration;
using Xunit;
using NSubstitute;

namespace CvAut.Backend.Tests
{
    public class HomeWallUpgradeRunnerTests
    {
        [Fact]
        public void TryUpgradeWallsFromHome_WhenDisabled_ReturnsZero()
        {
            var configService = Substitute.For<IConfigService>();
            var wallConfig = new WallUpgradeConfig(); // enabled = false by default
            configService.GetWallUpgradeConfig(Arg.Any<int>()).Returns(wallConfig);

            var runner = new HomeWallUpgradeRunner(null, configService, null);

            int result = runner.TryUpgradeWallsFromHome(1, 1, _ => true, CancellationToken.None, "test", 10);
            Assert.Equal(0, result);
        }

        [Fact]
        public void TryUpgradeWallsFromHome_WhenBudgetZero_ReturnsZero()
        {
            var configService = Substitute.For<IConfigService>();
            var wallConfig = new WallUpgradeConfig { Enabled = true, BatchLimit = 5 };
            configService.GetWallUpgradeConfig(Arg.Any<int>()).Returns(wallConfig);

            var runner = new HomeWallUpgradeRunner(null, configService, null);

            int result = runner.TryUpgradeWallsFromHome(1, 1, _ => true, CancellationToken.None, "test", 0);
            Assert.Equal(0, result);
        }

        [Fact]
        public void TryUpgradeWallsFromHome_WhenBaseNotConfirmed_ReturnsZero()
        {
            var configService = Substitute.For<IConfigService>();
            var wallConfig = new WallUpgradeConfig { Enabled = true, BatchLimit = 5 };
            configService.GetWallUpgradeConfig(Arg.Any<int>()).Returns(wallConfig);

            var runner = new HomeWallUpgradeRunner(null, configService, null);

            int result = runner.TryUpgradeWallsFromHome(1, 1, _ => false, CancellationToken.None, "test", 5);
            Assert.Equal(0, result);
        }
    }
}
