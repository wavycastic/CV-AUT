using System;
using System.IO;
using System.Text.Json.Nodes;
using CvAut;
using CvAut.Configuration;
using CvAut.Models;
using CvAut.Services;
using CvAut.Services.Configuration;
using Xunit;

namespace CvAut.Tests
{
    /// <summary>
    /// Locks the single source of truth for notification settings: the <c>notifications</c> object
    /// inside the active profile config, which both the UI and the snapshot readers consume.
    /// </summary>
    public class NotificationConfigSourceTests
    {
        private static ConfigStore NewIsolatedStore(string profileName)
        {
            var store = new ConfigStore(Path.Combine(Path.GetTempPath(), "cvaut_notifcfg_" + Guid.NewGuid().ToString("N")));
            store.SaveProfileAs(profileName, new JsonObject());
            return store;
        }

        [Fact]
        public void SaveNotificationSettings_WritesIntoActiveConfig()
        {
            ConfigStore store = NewIsolatedStore("device_127.0.0.1_5556");

            store.SaveNotificationSettings(new NotificationSettings
            {
                Enabled = true,
                WebhookUrl = "https://discord.com/api/webhooks/1/abc",
                NotifyOnStarted = true,
            });

            var notifications = Assert.IsType<JsonObject>(store.LoadActiveConfig()["notifications"]);
            Assert.True(ConfigStore.TryGetBool(notifications["enabled"], false));
            Assert.Equal("https://discord.com/api/webhooks/1/abc", ConfigStore.TryGetString(notifications["webhook_url"], string.Empty));
            Assert.True(ConfigStore.TryGetBool(notifications["notify_on_started"], false));
        }

        [Fact]
        public void SnapshotProvider_SeesSettingsSavedByTheUi()
        {
            ConfigStore store = NewIsolatedStore("device_127.0.0.1_5558");

            store.SaveNotificationSettings(new NotificationSettings
            {
                Enabled = true,
                WebhookUrl = "https://discord.com/api/webhooks/2/def",
                NotifyOnStopped = true,
            });

            NotificationConfig notifications = new ProfileConfigSnapshotProvider(store).LoadActive().Notifications;

            Assert.True(notifications.Enabled);
            Assert.Equal("https://discord.com/api/webhooks/2/def", notifications.WebhookUrl);
            Assert.True(notifications.NotifyOnStopped);
            Assert.True(notifications.IsActionable);
        }

        [Fact]
        public void NotificationSettings_RoundTripThroughActiveConfig()
        {
            ConfigStore store = NewIsolatedStore("device_127.0.0.1_5560");

            store.SaveNotificationSettings(new NotificationSettings
            {
                Enabled = true,
                WebhookUrl = "https://discord.com/api/webhooks/3/ghi",
                NotifyOnError = false,
                NotifyOnStopped = true,
            });

            NotificationSettings loaded = store.LoadNotificationSettings();

            Assert.True(loaded.Enabled);
            Assert.Equal("https://discord.com/api/webhooks/3/ghi", loaded.WebhookUrl);
            Assert.False(loaded.NotifyOnError);
            Assert.True(loaded.NotifyOnStopped);
        }
    }
}
