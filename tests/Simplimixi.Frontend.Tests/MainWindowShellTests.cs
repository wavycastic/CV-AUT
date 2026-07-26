using System;
using System.IO;
using CvAut;
using CvAut.Services;
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
    /// Note: constructing MainWindowViewModel starts a real device scan, because assigning
    /// DetectDevicesCommand to the dashboard executes it from the setter. These tests therefore
    /// stay on state that scan never touches — asserting on ActiveDevice or the dashboard state
    /// here would race with it.
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
    }
}
