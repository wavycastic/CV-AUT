using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using CvAut.Models;

namespace CvAut
{
    public sealed class BotProfile
    {
        public string Name { get; init; } = "Default";
        public string ConfigPath { get; init; } = Path.Combine("Config", "test_config.json");
        public DateTimeOffset UpdatedAt { get; init; } = DateTimeOffset.Now;

        public override string ToString() => Name;
    }

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

        /// <summary>Loads opt-in notification settings (disabled/empty by default).</summary>
        NotificationSettings LoadNotificationSettings();

        /// <summary>Persists notification settings.</summary>
        void SaveNotificationSettings(NotificationSettings settings);
    }

    /// <summary>
    /// JSON DOM config/profile store. AOT-safe: no reflection serializers, no assembly scanning.
    /// Preserves unknown backend fields while UI edits known Phase 2 fields.
    /// </summary>
    public sealed class ConfigStore : IConfigStore
    {
        private static bool s_loggedLegacyWallConfigMigration;

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
                    EnsureDefaults(obj);
                    return obj;
                }
            }
            catch
            {
                // Fall through to defaults; caller can save if wanted.
            }

            JsonObject fallback = DefaultConfig();
            EnsureDefaults(fallback);
            return fallback;
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
                    : DefaultConfig();
            }

            EnsureDefaults(config);
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

        public NotificationSettings LoadNotificationSettings()
        {
            string path = Path.Combine(_profileRoot, "notifications.json");
            try
            {
                if (File.Exists(path) && JsonNode.Parse(File.ReadAllText(path)) is JsonObject o)
                {
                    return new NotificationSettings
                    {
                        Enabled = TryGetBool(o["enabled"], false),
                        WebhookUrl = TryGetString(o["webhook_url"], string.Empty),
                        NotifyOnError = TryGetBool(o["notify_on_error"], true),
                        NotifyOnStopped = TryGetBool(o["notify_on_stopped"], false),
                        NotifyOnStarted = TryGetBool(o["notify_on_started"], false),
                    };
                }
            }
            catch
            {
                // Fall through to defaults.
            }

            return new NotificationSettings();
        }

        public void SaveNotificationSettings(NotificationSettings settings)
        {
            var o = new JsonObject
            {
                ["enabled"] = settings.Enabled,
                ["webhook_url"] = settings.WebhookUrl,
                ["notify_on_error"] = settings.NotifyOnError,
                ["notify_on_stopped"] = settings.NotifyOnStopped,
                ["notify_on_started"] = settings.NotifyOnStarted,
            };
            File.WriteAllText(Path.Combine(_profileRoot, "notifications.json"), o.ToJsonString(JsonOptions));
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

        private static void EnsureDefaults(JsonObject root)
        {
            JsonObject device = GetOrCreateObject(root, "device_connection");
            device["host"] ??= "127.0.0.1";
            device["port"] ??= 5556;

            JsonObject thresholds = GetOrCreateObject(root, "farming_thresholds");
            thresholds["gold_threshold"] ??= 650000;
            thresholds["elixir_threshold"] ??= 650000;
            thresholds["dark_elixir_threshold"] ??= 1000;
            thresholds["total_resource_threshold"] ??= 1300000;
            thresholds["target_logic"] ??= "total";

            root["attack"] ??= "Dragon_Attack";
            root["enable_stats"] ??= true;
            root["upgrade_wall"] ??= false;
            root["wall_level"] ??= 14;
            MigrateLegacyWallConfig(root);
            root["wall_gold_threshold"] ??= 5000000;
            root["wall_elixir_threshold"] ??= 5000000;
            root["wall_gold_reserve"] ??= 100000;
            root["wall_elixir_reserve"] ??= 0;
            root["wall_batch_limit"] ??= 1;
            root["wall_debug_screenshots"] ??= false;
            root["attack_mode"] ??= "attack";
            root["use_electro_dragon"] ??= false;
            root["request_troops"] ??= false;
            root["request_troops_message"] ??= "Rồng + rage";

            JsonObject smart = GetOrCreateObject(root, "smart_surrender");
            smart["enabled"] ??= false;
            smart["after_seconds_enabled"] ??= true;
            smart["after_seconds"] ??= 60;
            smart["low_resources_enabled"] ??= false;
            smart["low_resources_threshold"] ??= 100000;

            JsonObject night = GetOrCreateObject(root, "night_village");
            night["farm_mode"] ??= "auto";
            night["min_cups"] ??= 0;
            night["max_cups"] ??= 5000;
            night["attack_count"] ??= 1;
            night["attack_count_mode"] ??= "fixed";
            night["stop_when_loot_unavailable"] ??= true;
            night["enable_attack"] ??= true;
            night["boost_clock_tower"] ??= false;
            night["upgrade_wall"] ??= false;
            night["army_management"] ??= true;
            night["fill_army"] ??= true;
            night["army_formation"] ??= "auto";
            night["wait_for_heroes"] ??= true;
            night["hero_wait_seconds"] ??= 90;
            night["custom_drop_order_enabled"] ??= false;
            night["drop_order"] ??= "BattleMachine|Bomber|PowerPekka|BabyDragon|CannonCart|NightWitch|RagedBarbarian";
            night["next_troop_delay_ms"] ??= 600;
            night["same_troop_delay_ms"] ??= 180;
            night["handle_bomber"] ??= true;
            night["loop_hero_ability"] ??= true;
            night["enable_stage2"] ??= true;
            night["clean_yard"] ??= false;
            night["suggested_upgrades"] ??= false;
            night["place_new_buildings"] ??= false;
            night["ignore_gold_upgrades"] ??= false;
            night["ignore_elixir_upgrades"] ??= false;
            night["ignore_hall_upgrades"] ??= true;
            night["ignore_wall_upgrades"] ??= true;
            night["star_laboratory"] ??= false;
            night["star_laboratory_troop"] ??= "auto";
            night["upgrade_battle_machine"] ??= false;
            night["upgrade_battle_copter"] ??= false;
            night["bob_upgrades"] ??= false;

            JsonObject clanGames = GetOrCreateObject(root, "clan_games");
            clanGames["village"] ??= "main_village";
            clanGames["mission_filter"] ??= "resources,walls,stars";
            clanGames["filter_set_name"] ??= "Default";

            JsonObject capital = GetOrCreateObject(root, "clan_capital");
            capital["enabled"] ??= true;
            capital["attack_mode"] ??= "auto";

            JsonObject advanced = GetOrCreateObject(root, "advanced");
            advanced["search_delay_ms"] ??= 800;
            advanced["deploy_delay_ms"] ??= 120;
            advanced["return_home_delay_ms"] ??= 1500;
        }

        private static void MigrateLegacyWallConfig(JsonObject root)
        {
            bool migrated = false;
            if (root["wall_upgrade_threshold"] is JsonNode upgradeThreshold)
            {
                root["wall_gold_threshold"] ??= ConfigStore.TryGetInt(upgradeThreshold, 5000000);
                root["wall_elixir_threshold"] ??= ConfigStore.TryGetInt(upgradeThreshold, 5000000);
                root.Remove("wall_upgrade_threshold");
                migrated = true;
            }

            if (root["wall_reserve_threshold"] is JsonNode reserveThreshold)
            {
                root["wall_gold_reserve"] ??= ConfigStore.TryGetInt(reserveThreshold, 100000);
                root["wall_elixir_reserve"] ??= ConfigStore.TryGetInt(reserveThreshold, 0);
                root.Remove("wall_reserve_threshold");
                migrated = true;
            }

            if (migrated && !s_loggedLegacyWallConfigMigration)
            {
                Console.WriteLine("[CONFIG] event=legacy_config_migrated scope=wall");
                s_loggedLegacyWallConfigMigration = true;
            }
        }

        private static JsonObject DefaultConfig()
        {
            var root = new JsonObject();
            EnsureDefaults(root);
            return root;
        }
    }

    public static class AttackCatalog
    {
        public static IReadOnlyList<string> Discover()
        {
            var names = new List<string>();
            try
            {
                string dir = Path.Combine(AppContext.BaseDirectory, "assets", "Templates", "attacks");
                if (Directory.Exists(dir))
                {
                    foreach (string file in Directory.GetFiles(dir, "*.txt"))
                    {
                        names.Add(Path.GetFileNameWithoutExtension(file));
                    }
                }
            }
            catch
            {
                // Best effort — empty catalog means user types attack name.
            }

            names.Sort(StringComparer.OrdinalIgnoreCase);
            return names;
        }
    }
}
