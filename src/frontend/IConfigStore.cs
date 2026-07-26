using System.Collections.Generic;
using System.Text.Json.Nodes;
using CvAut.Models;

namespace CvAut
{
    public interface IConfigStore
    {
        string ActiveProfileName { get; }
        IReadOnlyList<BotProfile> Profiles { get; }
        JsonObject LoadActiveConfig();
        void SaveActiveConfig(JsonObject config);
        void LoadProfile(string name);
        void SaveProfileAs(string name, JsonObject config);
        void DeleteProfile(string name);
        string ResolveActiveConfigPath();

        /// <summary>Ensures a per-device config file exists whose device_connection points at this device,
        /// then returns its path. Does not change the active profile — safe to call per device before Start
        /// so concurrent devices each load their own host/port (Phase 3 multi-device).</summary>
        string PrepareDeviceConfig(string deviceProfileKey, string host, int port, string? emulatorType = null, string? emulatorPath = null, string? emulatorInstance = null);

        /// <summary>Loads opt-in notification settings (disabled/empty by default) from the
        /// <c>notifications</c> object of the active profile config.</summary>
        NotificationSettings LoadNotificationSettings();

        /// <summary>Persists notification settings into the active profile config.</summary>
        void SaveNotificationSettings(NotificationSettings settings);
    }
}
