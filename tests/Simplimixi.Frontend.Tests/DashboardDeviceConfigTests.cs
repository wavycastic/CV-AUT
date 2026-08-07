using System;
using System.IO;
using System.Linq;
using CvAut;
using CvAut.Models;
using CvAut.Services.Configuration;
using CvAut.ViewModels;
using CvAut.ViewModels.Settings;
using Xunit;

namespace CvAut.Tests
{
    /// <summary>
    /// Covers the per-device config panel workflow on the dashboard: opening the shared settings
    /// view model for one device, the profile it derives from that device, and cancelling back.
    /// </summary>
    public class DashboardDeviceConfigTests
    {
        private const string DeviceProfileKey = "device_127.0.0.1_5556";

        private static (DashboardViewModel Vm, ConfigStore Store) NewDashboard()
        {
            string root = Path.Combine(Path.GetTempPath(), "cvaut_devcfg_" + Guid.NewGuid().ToString("N"));
            var store = new ConfigStore(root);
            var settings = new SettingsViewModel(
                new MainVillageViewModel(store),
                new NightVillageViewModel(store),
                new ClanGamesViewModel(store),
                new ClanCapitalViewModel(store),
                store);
            return (new DashboardViewModel(settings, store), store);
        }

        private static DeviceViewModel Dev(ConfigStore store)
            => new DeviceViewModel(new Device("127.0.0.1", 5556, "Mock", "Mock", DeviceStatus.Ready), null, store);

        [Fact]
        public void ConfigureDevice_OpensThePanel_AndActivatesADeviceProfile()
        {
            var (d, store) = NewDashboard();
            DeviceViewModel dev = Dev(store);

            d.ConfigureDeviceCommand.Execute(dev);

            Assert.Same(dev, d.SelectedDeviceForConfig);
            Assert.Equal(DashboardDeviceState.ConfiguringDevice, d.State);
            Assert.True(d.ShowConfiguringPanel);
            Assert.False(d.ShowGridPane);
            Assert.Contains(store.Profiles, p => p.Name == DeviceProfileKey);
            Assert.Equal(DeviceProfileKey, store.ActiveProfileName);
        }

        [Fact]
        public void ConfigureDevice_Twice_ReusesTheSameDeviceProfile()
        {
            var (d, store) = NewDashboard();
            DeviceViewModel dev = Dev(store);

            d.ConfigureDeviceCommand.Execute(dev);
            d.CancelDeviceConfigCommand.Execute(null);
            d.ConfigureDeviceCommand.Execute(dev);

            Assert.Equal(1, store.Profiles.Count(p => p.Name == DeviceProfileKey));
        }

        [Fact]
        public void CancelDeviceConfig_ClosesThePanel_WithoutClearingTheProfile()
        {
            var (d, store) = NewDashboard();
            DeviceViewModel dev = Dev(store);

            d.ConfigureDeviceCommand.Execute(dev);
            d.CancelDeviceConfigCommand.Execute(null);

            Assert.Null(d.SelectedDeviceForConfig);
            Assert.False(d.ShowConfiguringPanel);
            Assert.Equal(DashboardDeviceState.DeviceSelected, d.State);
            Assert.Contains(store.Profiles, p => p.Name == DeviceProfileKey);
        }

        [Fact]
        public void InstanceSaveButton_PersistsMainVillageSettingsToTheDeviceProfile()
        {
            var (dashboard, store) = NewDashboard();
            DeviceViewModel device = Dev(store);
            dashboard.ConfigureDeviceCommand.Execute(device);

            var mainVillage = Assert.IsType<MainVillageViewModel>(dashboard.SettingsViewModel.SelectedTab!.Page);
            mainVillage.GoldThreshold = 321000;
            mainVillage.ElixirThreshold = 654000;
            mainVillage.DarkElixirThreshold = 4321;
            mainVillage.TotalResourceThreshold = 975000;
            mainVillage.TargetLogic = "Tất cả điều kiện";
            mainVillage.AttackName = "Rồng điện + Balloon";

            // This is the exact command used by the yellow "Lưu" button in the device dialog.
            dashboard.SettingsViewModel.InstanceSaveCommand.Execute(null);

            var persisted = store.LoadActiveConfig();
            var thresholds = Assert.IsType<System.Text.Json.Nodes.JsonObject>(persisted["farming_thresholds"]);
            Assert.Equal(321000, thresholds["gold_threshold"]!.GetValue<int>());
            Assert.Equal(654000, thresholds["elixir_threshold"]!.GetValue<int>());
            Assert.Equal(4321, thresholds["dark_elixir_threshold"]!.GetValue<int>());
            Assert.Equal(975000, thresholds["total_resource_threshold"]!.GetValue<int>());
            Assert.Equal("and", thresholds["target_logic"]!.GetValue<string>());
            Assert.Equal("ElectroDragon_Attack", persisted["attack"]!.GetValue<string>());
            Assert.Equal(DashboardDeviceState.DeviceSelected, dashboard.State);
            Assert.Null(dashboard.SelectedDeviceForConfig);
        }
    }
}
