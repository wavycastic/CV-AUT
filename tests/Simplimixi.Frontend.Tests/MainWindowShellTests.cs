using System;
using System.IO;
using CvAut;
using CvAut.Models;
using CvAut.Services;
using CvAut.Services.Configuration;
using CvAut.Services.Emulators;
using CvAut.Services.Sessions;
using CvAut.ViewModels;
using CvAut.ViewModels.Settings;
using Xunit;

namespace CvAut.Tests
{
    /// <summary>
    /// Covers app-scoped shell state that the window chrome binds to.
    ///
    /// Constructing MainWindowViewModel no longer starts a device scan: the startup scan is
    /// triggered by the shell view through StartInitialDeviceScan. These tests therefore never
    /// call that method, which keeps them off real ADB.
    /// </summary>
    public class MainWindowShellTests
    {
        private static MainWindowViewModel NewShell()
        {
            string root = Path.Combine(Path.GetTempPath(), "cvaut_shell_" + Guid.NewGuid().ToString("N"));
            var store = new ConfigStore(root);
            var settings = new SettingsViewModel(
                new MainVillageViewModel(store),
                new NightVillageViewModel(store),
                new ClanGamesViewModel(store),
                new ClanCapitalViewModel(store),
                store);

            return new MainWindowViewModel(
                new AppStateService(),
                new DeviceSessionManager(),
                store,
                new AdbEmulatorDiscovery(),
                new DashboardViewModel(settings, store),
                new LogsViewModel(),
                new LicenseViewModel(),
                settings,
                new AdvancedViewModel());
        }

        [Fact]
        public void ToggleGridMode_MirrorsTheFlagOntoTheDashboard()
        {
            MainWindowViewModel vm = NewShell();
            Assert.False(vm.IsGridMode);

            vm.ToggleGridModeCommand.Execute(null);
            Assert.True(vm.IsGridMode);
            Assert.True(vm.Dashboard.IsGridMode);

            vm.ToggleGridModeCommand.Execute(null);
            Assert.False(vm.IsGridMode);
            Assert.False(vm.Dashboard.IsGridMode);
        }

        [Fact]
        public void LicenseOverlay_OpensAndCloses()
        {
            MainWindowViewModel vm = NewShell();
            Assert.False(vm.IsLicenseOpen);

            vm.OpenLicenseCommand.Execute(null);
            Assert.True(vm.IsLicenseOpen);

            vm.CloseLicenseCommand.Execute(null);
            Assert.False(vm.IsLicenseOpen);
        }

        [Fact]
        public void Construction_WiresTheDetectCommand_WithoutRunningIt()
        {
            MainWindowViewModel vm = NewShell();

            Assert.Same(vm.DetectDevicesCommand, vm.Dashboard.DetectDevicesCommand);
            Assert.Equal(DashboardDeviceState.Idle, vm.Dashboard.State);
            Assert.Empty(vm.Devices);
            Assert.Null(vm.ActiveDevice);
        }
    }
}
