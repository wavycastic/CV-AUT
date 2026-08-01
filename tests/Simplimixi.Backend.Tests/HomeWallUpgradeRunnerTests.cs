using System;
using System.Threading;
using CvAut;
using CvAut.Automation;
using CvAut.Configuration;
using Xunit;

namespace CvAut.Backend.Tests
{
    public class HomeWallUpgradeRunnerTests
    {
        private class StubConfigService : IConfigService
        {
            public WallUpgradeConfig WallConfig { get; set; } = new WallUpgradeConfig();
            
            public WallUpgradeConfig GetWallUpgradeConfig(int villageIndex) => WallConfig;
            
            // Dummy implementations for the rest
            public AutomationConfig Current => throw new NotImplementedException();
            public MainVillageConfig GetMainVillageConfig(int villageIndex) => throw new NotImplementedException();
            public TrainingConfig GetTrainingConfig(int villageIndex) => throw new NotImplementedException();
            public string GetAttackStrategy(int villageIndex) => throw new NotImplementedException();
            public BuilderBaseConfig GetBuilderBaseConfig(int villageIndex) => throw new NotImplementedException();
            public NightVillageDeployConfig GetBuilderBaseDeployConfig(int villageIndex) => throw new NotImplementedException();
        }

        [Fact]
        public void TryUpgradeWallsFromHome_WhenDisabled_ReturnsZero()
        {
            var configService = new StubConfigService { WallConfig = new WallUpgradeConfig() }; // enabled = false
            var runner = new HomeWallUpgradeRunner(null, configService, null);

            int result = runner.TryUpgradeWallsFromHome(1, 1, _ => true, CancellationToken.None, "test", 10);
            Assert.Equal(0, result);
        }

        [Fact]
        public void TryUpgradeWallsFromHome_WhenBudgetZero_ReturnsZero()
        {
            var configService = new StubConfigService { WallConfig = new WallUpgradeConfig { Enabled = true, BatchLimit = 5 } };
            var runner = new HomeWallUpgradeRunner(null, configService, null);

            int result = runner.TryUpgradeWallsFromHome(1, 1, _ => true, CancellationToken.None, "test", 0);
            Assert.Equal(0, result);
        }

        [Fact]
        public void TryUpgradeWallsFromHome_WhenBaseNotConfirmed_ReturnsZero()
        {
            var configService = new StubConfigService { WallConfig = new WallUpgradeConfig { Enabled = true, BatchLimit = 5 } };
            var runner = new HomeWallUpgradeRunner(null, configService, null);

            int result = runner.TryUpgradeWallsFromHome(1, 1, _ => false, CancellationToken.None, "test", 5);
            Assert.Equal(0, result);
        }
    }
}
