using System.Text.Json;
using CvAut;
using Xunit;

namespace CvAut.Backend.Tests
{
    public class CVAutomationFrameworkTests
    {
        [Fact]
        public void AutomationRoiConstants_HasValidCoordinates()
        {
            Assert.True(AutomationRoiConstants.GameSettingHomeRoi.Width > 0);
            Assert.True(AutomationRoiConstants.NextButtonRoi.Width > 0);
            Assert.True(AutomationRoiConstants.ConnectionPopupRoi.Width > 0);
            Assert.True(AutomationRoiConstants.TreasureHuntRoi.Width > 0);
        }

        [Fact]
        public void AutomationThresholds_HasValidThresholds()
        {
            Assert.True(AutomationThresholds.HomeTemplateThreshold > 0.5 && AutomationThresholds.HomeTemplateThreshold <= 1.0);
            Assert.True(AutomationThresholds.ConnectionPopupThreshold > 0.5 && AutomationThresholds.ConnectionPopupThreshold <= 1.0);
            Assert.Contains(@"ui\Connection_lost.png", AutomationThresholds.ConnectionPopupTemplates);
        }

        [Fact]
        public void AccountManager_ParsesDefaultAccountConfigs()
        {
            var manager = new AccountManager();
            using var doc = JsonDocument.Parse("{}");
            
            AccountConfig[] accounts = manager.GetConfiguredAccounts(doc.RootElement);

            Assert.Single(accounts);
            Assert.Equal("acc_1", accounts[0].Id);
            Assert.Equal(1, accounts[0].ProfileVillage);
        }

        [Fact]
        public void AccountManager_ParsesMultipleSelectedVillages()
        {
            var manager = new AccountManager();
            string json = "{\"selected_villages\": [1, 3, 5]}";
            using var doc = JsonDocument.Parse(json);

            AccountConfig[] accounts = manager.GetConfiguredAccounts(doc.RootElement);

            Assert.Equal(3, accounts.Length);
            Assert.Equal(1, accounts[0].ProfileVillage);
            Assert.Equal(3, accounts[1].ProfileVillage);
            Assert.Equal(5, accounts[2].ProfileVillage);
        }

        [Fact]
        public void AccountManager_ClampsVillageIndex()
        {
            var manager = new AccountManager();
            manager.CurrentVillageIdx = 10;
            Assert.Equal(5, manager.CurrentVillageIdx);

            manager.CurrentVillageIdx = -2;
            Assert.Equal(1, manager.CurrentVillageIdx);
        }

        [Fact]
        public void MatchmakingEngine_EvaluatesTotalTargetLogic()
        {
            var engine = new MatchmakingEngine();
            var config = new FarmingTargetConfig(500000, 500000, 1000, 1000000, TargetSelectionLogic.Total);
            var resources = new ScoutedResources(600000, 500000, 1000);

            bool accepted = engine.ShouldAcceptTarget(resources, config, out string reason);

            Assert.True(accepted);
            Assert.Contains("total_resource_satisfied", reason);
        }

        [Fact]
        public void WallUpgradeDecider_CalculatesValidDecision()
        {
            var input = new WallUpgradeDecisionInput(
                WallCost: 2000000,
                Gold: 6000000,
                Elixir: 4000000,
                GoldStartThreshold: 5000000,
                ElixirStartThreshold: 5000000,
                GoldReserve: 100000,
                ElixirReserve: 0);

            var decision = WallUpgradeDecider.Decide(input);

            Assert.Equal(WallUpgradeResource.Gold, decision.Resource);
            Assert.True(decision.RequestedCount > 0);
        }
    }
}
