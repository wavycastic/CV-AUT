using System;
using System.IO;
using System.Text.Json.Nodes;
using CvAut.Configuration;
using CvAut.Models;

namespace CvAut.Services
{
    public interface IAppPreferences
    {
        string LoadSelectedEmulatorFilter();
        void SaveSelectedEmulatorFilter(string filter);
    }

    public sealed class JsonAppPreferences : IAppPreferences
    {
        private const string DefaultEmulatorFilter = "BlueStacks";
        private readonly string _path;

        public JsonAppPreferences()
            : this(Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "AutoClashOfClan20206",
                "app_settings.json"))
        {
        }

        public JsonAppPreferences(string path)
        {
            _path = path;
        }

        public string LoadSelectedEmulatorFilter()
        {
            try
            {
                if (File.Exists(_path)
                    && JsonNode.Parse(File.ReadAllText(_path)) is JsonObject obj
                    && obj.TryGetPropertyValue("SelectedEmulatorFilter", out JsonNode? value)
                    && value is not null)
                {
                    string filter = value.ToString();
                    return string.IsNullOrWhiteSpace(filter) ? DefaultEmulatorFilter : filter;
                }
            }
            catch
            {
                // Corrupt preferences fall back to the safe default.
            }
            return DefaultEmulatorFilter;
        }

        public void SaveSelectedEmulatorFilter(string filter)
        {
            try
            {
                string? directory = Path.GetDirectoryName(Path.GetFullPath(_path));
                if (!string.IsNullOrWhiteSpace(directory)) Directory.CreateDirectory(directory);
                File.WriteAllText(_path, new JsonObject
                {
                    ["SelectedEmulatorFilter"] = filter
                }.ToJsonString());
            }
            catch
            {
                // Preferences are best-effort.
            }
        }
    }

    public sealed record ProfileConfigSnapshot(
        DeviceConnectionConfig Device,
        VillagePlayMode PlayMode,
        NotificationConfig Notifications);

    public interface IProfileConfigSnapshotProvider
    {
        ProfileConfigSnapshot LoadActive();
        VillagePlayMode LoadPlayMode(string profileName);
        void SavePlayMode(string profileName, VillagePlayMode playMode);
        void ApplyPlayMode(string configPath, VillagePlayMode playMode);
        string PrepareDevice(Device device, VillagePlayMode playMode);
    }

    /// <summary>
    /// Frontend JSON boundary. View models can work with typed profile values while
    /// JsonObject remains confined to this adapter and the legacy ConfigStore persistence.
    /// </summary>
    public sealed class ProfileConfigSnapshotProvider : IProfileConfigSnapshotProvider
    {
        private readonly IConfigStore _store;

        public ProfileConfigSnapshotProvider(IConfigStore store)
        {
            _store = store;
        }

        public ProfileConfigSnapshot LoadActive()
        {
            JsonObject root = _store.LoadActiveConfig();
            JsonObject device = ConfigStore.GetOrCreateObject(root, "device_connection");
            return new ProfileConfigSnapshot(
                new DeviceConnectionConfig(
                    ConfigStore.TryGetString(device["host"], DeviceConnectionConfig.DefaultHost),
                    ConfigStore.TryGetInt(device["port"], DeviceConnectionConfig.DefaultPort),
                    ConfigStore.TryGetString(device["serial"], string.Empty) is string serial && !string.IsNullOrWhiteSpace(serial) ? serial : null,
                    ConfigStore.TryGetString(device["emulator_type"], DeviceConnectionConfig.DefaultEmulatorType),
                    ConfigStore.TryGetString(device["emulator_path"], string.Empty),
                    ConfigStore.TryGetString(device["emulator_instance"], string.Empty)),
                ParsePlayMode(ConfigStore.TryGetString(root["play_mode"], "main_village")),
                ReadNotifications(root));
        }

        public VillagePlayMode LoadPlayMode(string profileName)
        {
            _store.LoadProfile(profileName);
            return LoadActive().PlayMode;
        }

        public void SavePlayMode(string profileName, VillagePlayMode playMode)
        {
            if (!_store.Profiles.Any(profile => profile.Name.Equals(profileName, StringComparison.OrdinalIgnoreCase)))
            {
                _store.LoadProfile("Default");
                _store.SaveProfileAs(profileName, _store.LoadActiveConfig());
            }
            _store.LoadProfile(profileName);
            JsonObject root = _store.LoadActiveConfig();
            root["play_mode"] = ToToken(playMode);
            _store.SaveActiveConfig(root);
        }

        public void ApplyPlayMode(string configPath, VillagePlayMode playMode)
        {
            try
            {
                JsonObject root = File.Exists(configPath)
                    && JsonNode.Parse(File.ReadAllText(configPath)) is JsonObject existing
                        ? existing
                        : new JsonObject();
                root["play_mode"] = ToToken(playMode);
                File.WriteAllText(configPath, root.ToJsonString(new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
            }
            catch
            {
                // Device start keeps the previously persisted mode if writing fails.
            }
        }

        public string PrepareDevice(Device device, VillagePlayMode playMode)
        {
            string path = _store.PrepareDeviceConfig(
                device.ProfileKey,
                device.Host,
                device.Port,
                device.EmulatorType,
                device.EmulatorPath,
                device.EmulatorInstance);
            ApplyPlayMode(path, playMode);
            return path;
        }

        private static NotificationConfig ReadNotifications(JsonObject root)
        {
            JsonObject notifications = ConfigStore.GetOrCreateObject(root, "notifications");
            return new NotificationConfig(
                ConfigStore.TryGetBool(notifications["enabled"], false),
                ConfigStore.TryGetString(notifications["webhook_url"], string.Empty),
                ConfigStore.TryGetBool(notifications["notify_on_error"], true),
                ConfigStore.TryGetBool(notifications["notify_on_stopped"], false),
                ConfigStore.TryGetBool(notifications["notify_on_started"], false));
        }

        public static VillagePlayMode ParsePlayMode(string token) => token switch
        {
            "night_village" => VillagePlayMode.NightVillage,
            "clan_games" => VillagePlayMode.ClanGames,
            "clan_capital" => VillagePlayMode.ClanCapital,
            _ => VillagePlayMode.MainVillage
        };

        public static string ToToken(VillagePlayMode mode) => mode switch
        {
            VillagePlayMode.NightVillage => "night_village",
            VillagePlayMode.ClanGames => "clan_games",
            VillagePlayMode.ClanCapital => "clan_capital",
            _ => "main_village"
        };
    }
}
