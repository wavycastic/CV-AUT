using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using CvAut.Models;

namespace CvAut
{
    /// <summary>
    /// JSON DOM config/profile store. AOT-safe: no reflection serializers, no assembly scanning.
    /// Preserves unknown backend fields while UI edits known Phase 2 fields.
    /// </summary>
    public sealed class ConfigStore : IConfigStore
    {
        private const string DefaultProfileName = "Default";
        private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
        private readonly string _profileRoot;
        private readonly List<BotProfile> _profiles = new();
        private string _activeProfileName = DefaultProfileName;

        public ConfigStore()
            : this(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AutoClashOfClan20206", "profiles"))
        {
        }

        /// <summary>Test/override ctor: profiles are stored under an explicit root directory.</summary>
        public ConfigStore(string profileRoot)
        {
            _profileRoot = profileRoot;
            Directory.CreateDirectory(_profileRoot);
            ReloadProfiles();
        }

        public string ActiveProfileName => _activeProfileName;
        public IReadOnlyList<BotProfile> Profiles => _profiles;

        public string ResolveActiveConfigPath()
        {
            BotProfile? profile = _profiles.Find(p => string.Equals(p.Name, _activeProfileName, StringComparison.OrdinalIgnoreCase));
            return profile?.ConfigPath ?? Path.Combine("Config", "test_config.json");
        }

        public JsonObject LoadActiveConfig()
        {
            string path = ResolveActiveConfigPath();
            try
            {
                if (File.Exists(path) && JsonNode.Parse(File.ReadAllText(path)) is JsonObject obj)
                {
                    ConfigSchemaDefaults.Apply(obj);
                    return obj;
                }
            }
            catch
            {
                // Fall through to defaults; caller can save if wanted.
            }

            return ConfigSchemaDefaults.CreateConfig();
        }

        public void SaveActiveConfig(JsonObject config)
        {
            string path = ResolveActiveConfigPath();
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
            File.WriteAllText(path, config.ToJsonString(JsonOptions));
            ReloadProfiles();
        }

        public void LoadProfile(string name)
        {
            if (_profiles.Exists(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase)))
            {
                _activeProfileName = name;
            }
        }

        public void SaveProfileAs(string name, JsonObject config)
        {
            string safeName = MakeSafeProfileName(name);
            if (string.IsNullOrWhiteSpace(safeName))
            {
                safeName = DefaultProfileName;
            }

            string path = Path.Combine(_profileRoot, safeName + ".json");
            File.WriteAllText(path, config.ToJsonString(JsonOptions));
            _activeProfileName = safeName;
            ReloadProfiles();
        }

        public string PrepareDeviceConfig(string deviceProfileKey, string host, int port, string? emulatorType = null, string? emulatorPath = null, string? emulatorInstance = null)
        {
            string safeName = MakeSafeProfileName(deviceProfileKey);
            if (string.IsNullOrWhiteSpace(safeName))
            {
                safeName = DefaultProfileName;
            }

            string path = Path.Combine(_profileRoot, safeName + ".json");

            // Seed from the device's existing profile if present, else the Default template, so the
            // user's per-device settings survive; only the connection endpoint is forced to this device.
            JsonObject config;
            if (File.Exists(path) && JsonNode.Parse(File.ReadAllText(path)) is JsonObject existing)
            {
                config = existing;
            }
            else
            {
                string defaultPath = Path.Combine("Config", "test_config.json");
                config = File.Exists(defaultPath) && JsonNode.Parse(File.ReadAllText(defaultPath)) is JsonObject def
                    ? def
                    : ConfigSchemaDefaults.CreateConfig();
            }

            ConfigSchemaDefaults.Apply(config);
            JsonObject device = GetOrCreateObject(config, "device_connection");
            device["host"] = host;
            device["port"] = port;
            if (!string.IsNullOrWhiteSpace(emulatorType))
            {
                device["emulator_type"] = emulatorType;
            }
            else
            {
                device.Remove("emulator_type");
            }

            if (!string.IsNullOrWhiteSpace(emulatorPath))
            {
                device["emulator_path"] = emulatorPath;
            }
            else
            {
                device.Remove("emulator_path");
            }

            if (!string.IsNullOrWhiteSpace(emulatorInstance))
            {
                device["emulator_instance"] = emulatorInstance;
            }
            else
            {
                device.Remove("emulator_instance");
            }

            File.WriteAllText(path, config.ToJsonString(JsonOptions));
            ReloadProfiles();
            return path;
        }

        /// <summary>
        /// Reads opt-in notification settings from the <c>notifications</c> object of the active
        /// profile config — the same object <see cref="CvAut.Services.ProfileConfigSnapshotProvider"/>
        /// and the backend snapshot reader consume, so the UI and the senders can never disagree.
        /// </summary>
        public NotificationSettings LoadNotificationSettings()
        {
            JsonObject notifications = GetOrCreateObject(LoadActiveConfig(), "notifications");
            return new NotificationSettings
            {
                Enabled = TryGetBool(notifications["enabled"], false),
                WebhookUrl = TryGetString(notifications["webhook_url"], string.Empty),
                NotifyOnError = TryGetBool(notifications["notify_on_error"], true),
                NotifyOnStopped = TryGetBool(notifications["notify_on_stopped"], false),
                NotifyOnStarted = TryGetBool(notifications["notify_on_started"], false),
            };
        }

        /// <summary>Persists notification settings into the active profile config.</summary>
        public void SaveNotificationSettings(NotificationSettings settings)
        {
            JsonObject config = LoadActiveConfig();
            JsonObject notifications = GetOrCreateObject(config, "notifications");
            notifications["enabled"] = settings.Enabled;
            notifications["webhook_url"] = settings.WebhookUrl;
            notifications["notify_on_error"] = settings.NotifyOnError;
            notifications["notify_on_stopped"] = settings.NotifyOnStopped;
            notifications["notify_on_started"] = settings.NotifyOnStarted;
            SaveActiveConfig(config);
        }

        public void DeleteProfile(string name)
        {
            if (string.Equals(name, DefaultProfileName, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            BotProfile? profile = _profiles.Find(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase));
            if (profile is not null && File.Exists(profile.ConfigPath))
            {
                File.Delete(profile.ConfigPath);
            }

            _activeProfileName = DefaultProfileName;
            ReloadProfiles();
        }

        private void ReloadProfiles()
        {
            _profiles.Clear();
            _profiles.Add(new BotProfile { Name = DefaultProfileName, ConfigPath = Path.Combine("Config", "test_config.json") });

            foreach (string file in Directory.GetFiles(_profileRoot, "*.json"))
            {
                string name = Path.GetFileNameWithoutExtension(file);
                if (string.Equals(name, DefaultProfileName, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                _profiles.Add(new BotProfile { Name = name, ConfigPath = file, UpdatedAt = File.GetLastWriteTime(file) });
            }

            _profiles.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));
            if (!_profiles.Exists(p => string.Equals(p.Name, _activeProfileName, StringComparison.OrdinalIgnoreCase)))
            {
                _activeProfileName = DefaultProfileName;
            }
        }

        private static string MakeSafeProfileName(string name)
        {
            foreach (char c in Path.GetInvalidFileNameChars())
            {
                name = name.Replace(c, '_');
            }

            return name.Trim();
        }

        public sealed class DeviceConnection
        {
            public string Host { get; init; } = "127.0.0.1";
            public int Port { get; init; } = 5556;
            public string Attack { get; init; } = string.Empty;
        }

        public static DeviceConnection Read(string configPath)
        {
            try
            {
                if (!File.Exists(configPath))
                {
                    return new DeviceConnection();
                }

                JsonNode? root = JsonNode.Parse(File.ReadAllText(configPath));
                JsonNode? device = root?["device_connection"];

                string host = device?["host"]?.GetValue<string>() ?? "127.0.0.1";
                int port = TryGetInt(device?["port"], 5556);
                string attack = root?["attack"]?.GetValue<string>() ?? string.Empty;

                return new DeviceConnection { Host = host, Port = port, Attack = attack };
            }
            catch
            {
                return new DeviceConnection();
            }
        }

        public static void Save(string configPath, string host, int port, string attack)
        {
            JsonObject rootObj = File.Exists(configPath)
                ? (JsonNode.Parse(File.ReadAllText(configPath)) as JsonObject ?? new JsonObject())
                : new JsonObject();

            JsonObject device = GetOrCreateObject(rootObj, "device_connection");
            device["host"] = host;
            device["port"] = port;
            rootObj["attack"] = attack;
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(configPath))!);
            File.WriteAllText(configPath, rootObj.ToJsonString(JsonOptions));
        }

        public static JsonObject GetOrCreateObject(JsonObject root, string key)
        {
            if (root[key] is JsonObject obj)
            {
                return obj;
            }

            obj = new JsonObject();
            root[key] = obj;
            return obj;
        }

        public static int TryGetInt(JsonNode? node, int fallback)
        {
            if (node is null)
            {
                return fallback;
            }

            try
            {
                return node.GetValue<int>();
            }
            catch
            {
                if (node is JsonValue value && value.TryGetValue(out string? text) && int.TryParse(text, out int parsed))
                {
                    return parsed;
                }

                return fallback;
            }
        }

        public static bool TryGetBool(JsonNode? node, bool fallback)
        {
            if (node is null)
            {
                return fallback;
            }

            try
            {
                return node.GetValue<bool>();
            }
            catch
            {
                if (node is JsonValue value && value.TryGetValue(out string? text) && bool.TryParse(text, out bool parsed))
                {
                    return parsed;
                }

                return fallback;
            }
        }

        public static string TryGetString(JsonNode? node, string fallback)
        {
            try
            {
                return node?.GetValue<string>() ?? fallback;
            }
            catch
            {
                return fallback;
            }
        }
    }
}
