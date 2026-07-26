using System;
using System.IO;
using System.Text.Json.Nodes;
using CvAut;
using CvAut.Models;
using CvAut.Services.Configuration;
using CvAut.ViewModels;
using CvAut.ViewModels.Settings;
using Xunit;

namespace CvAut.Tests
{
    /// <summary>
    /// Covers the settings page seams: the tab strip that reacts to the play mode, the
    /// instance-mode dialog contract the dashboard relies on, and the webhook form.
    /// </summary>
    public class SettingsViewModelTests
    {
        private static (SettingsViewModel Vm, ConfigStore Store) NewSettings()
        {
            string root = Path.Combine(Path.GetTempPath(), "cvaut_settings_" + Guid.NewGuid().ToString("N"));
            var store = new ConfigStore(root);

            // The Default profile resolves to the relative Config/test_config.json, which is shared
            // by every ConfigStore in the process regardless of its profile root. Activate a profile
            // inside this test's own root first, so saving settings cannot race other test classes.
            store.SaveProfileAs("isolated", new JsonObject());

            var vm = new SettingsViewModel(
                new MainVillageViewModel(store),
                new NightVillageViewModel(store),
                new ClanGamesViewModel(store),
                new ClanCapitalViewModel(store),
                store);
            return (vm, store);
        }

        [Fact]
        public void FreshPage_ShowsAllFourVillageTabs()
        {
            var (vm, _) = NewSettings();
            Assert.Equal(4, vm.Tabs.Count);
            Assert.True(vm.HasTabs);
            Assert.Same(vm.Tabs[0], vm.SelectedTab);
        }

        [Fact]
        public void InstanceMode_PlayModeChange_CollapsesTabsToSelectedMode()
        {
            var (vm, _) = NewSettings();
            vm.IsInstanceMode = true;
            vm.SelectedPlayMode = PlayMode.NightVillageLabel;

            Assert.Single(vm.Tabs);
            Assert.Equal("Làng đêm", vm.Tabs[0].Title);
            Assert.Same(vm.Tabs[0], vm.SelectedTab);
        }

        [Fact]
        public void PageMode_PlayModeChange_LeavesTabStripAlone()
        {
            var (vm, _) = NewSettings();
            vm.SelectedPlayMode = PlayMode.ClanGamesLabel;

            Assert.Equal(4, vm.Tabs.Count);
        }

        [Fact]
        public void InstanceCommands_RaiseTheEventsTheHostSubscribesTo()
        {
            var (vm, _) = NewSettings();
            bool saveRequested = false;
            bool cancelRequested = false;
            vm.InstanceSaveRequested += () => saveRequested = true;
            vm.InstanceCancelRequested += () => cancelRequested = true;

            vm.InstanceSaveCommand.Execute(null);
            vm.InstanceCancelCommand.Execute(null);

            Assert.True(saveRequested);
            Assert.True(cancelRequested);
        }

        [Fact]
        public void LoadSelectedProfileDirectly_SwitchesToTheDevicePlayModeTab()
        {
            var (vm, _) = NewSettings();
            vm.IsInstanceMode = true;
            vm.LoadSelectedProfileDirectly("device_127.0.0.1_5556", PlayMode.ClanCapitalLabel);

            Assert.Single(vm.Tabs);
            Assert.Equal("Kinh đô hội", vm.Tabs[0].Title);
            Assert.Equal(PlayMode.ClanCapitalLabel, vm.SelectedPlayMode);
            Assert.Equal("device_127.0.0.1_5556", vm.ProfileName);
            Assert.Equal("Đã tải cấu hình device_127.0.0.1_5556", vm.Status);
        }

        [Fact]
        public void SaveNotifications_TrimsUrl_AndPersistsToTheActiveProfile()
        {
            var (vm, store) = NewSettings();
            vm.NotifyEnabled = true;
            vm.WebhookUrl = "  https://discord.com/api/webhooks/1/abc  ";
            vm.NotifyOnStopped = true;

            vm.SaveNotificationsCommand.Execute(null);

            NotificationSettings loaded = store.LoadNotificationSettings();
            Assert.True(loaded.Enabled);
            Assert.Equal("https://discord.com/api/webhooks/1/abc", loaded.WebhookUrl);
            Assert.True(loaded.NotifyOnStopped);
            Assert.Equal("Đã lưu — thông báo bật.", vm.NotifyStatus);
        }

        [Fact]
        public void SaveNotifications_EnabledWithoutHttpsUrl_ReportsItIsNotActionable()
        {
            var (vm, _) = NewSettings();
            vm.NotifyEnabled = true;
            vm.WebhookUrl = "http://insecure.example";

            vm.SaveNotificationsCommand.Execute(null);

            Assert.Equal("Đã lưu — cần URL webhook https hợp lệ.", vm.NotifyStatus);
        }

        [Fact]
        public void SaveNotifications_Disabled_ReportsNotificationsOff()
        {
            var (vm, _) = NewSettings();
            vm.NotifyEnabled = false;

            vm.SaveNotificationsCommand.Execute(null);

            Assert.Equal("Đã lưu — thông báo tắt.", vm.NotifyStatus);
        }
    }
}
