using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace CvAut
{
    /// <summary>
    /// Reads and writes the few config fields the UI exposes (device host/port, selected
    /// attack) without deserializing the whole config into POCOs. Uses the JsonNode DOM so
    /// it survives Native AOT trimming and preserves every field the UI does not touch.
    /// </summary>
    public static class ConfigStore
    {
        public sealed class DeviceConnection
        {
            public string Host { get; init; } = "127.0.0.1";
            public int Port { get; init; } = 5556;
            public string Attack { get; init; } = string.Empty;
        }

        /// <summary>Reads host/port/attack from the config file. Returns defaults if missing or unreadable.</summary>
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

        /// <summary>
        /// Writes host/port/attack back into the config file, preserving all other fields.
        /// Throws on I/O or parse failure so the caller can surface the error.
        /// </summary>
        public static void Save(string configPath, string host, int port, string attack)
        {
            JsonNode root = File.Exists(configPath)
                ? JsonNode.Parse(File.ReadAllText(configPath)) ?? new JsonObject()
                : new JsonObject();

            JsonObject rootObj = root.AsObject();

            if (rootObj["device_connection"] is not JsonObject device)
            {
                device = new JsonObject();
                rootObj["device_connection"] = device;
            }

            device["host"] = host;
            device["port"] = port;
            rootObj["attack"] = attack;

            var options = new JsonSerializerOptions { WriteIndented = true };
            File.WriteAllText(configPath, rootObj.ToJsonString(options));
        }

        private static int TryGetInt(JsonNode? node, int fallback)
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
    }

    /// <summary>
    /// Discovers the available attack templates by scanning the deployed templates folder
    /// for the *.txt army definitions the backend loads by the "attack" config value.
    /// </summary>
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
                // Best effort — an empty catalog just means the user types the attack name.
            }

            names.Sort(StringComparer.OrdinalIgnoreCase);
            return names;
        }
    }
}
