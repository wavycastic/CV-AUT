using System;
using System.Text.Json;

namespace CvAut.Configuration;

public sealed record DeviceConnectionConfig(
    string Host,
    int Port,
    string? Serial,
    string EmulatorType,
    string EmulatorPath,
    string EmulatorInstance)
{
    public const string DefaultHost = "127.0.0.1";
    public const int DefaultPort = 5556;
    public const string DefaultEmulatorType = "BlueStacks";
    public string Endpoint => $"{Host}:{Port}";
}

internal static class DeviceConnectionConfigReader
{
    /// <summary>
    /// Reads the device_connection section and returns a normalized config.
    /// Normalization is deliberate, not incidental, because <see cref="DeviceConnectionConfig.Endpoint"/>
    /// is passed straight to ADB and a padded, blank or out-of-range value yields an endpoint that
    /// can never connect:
    /// - Host is trimmed, and a blank host falls back to <see cref="DeviceConnectionConfig.DefaultHost"/>.
    /// - Port is clamped to the valid TCP range 1-65535, defaulting to <see cref="DeviceConnectionConfig.DefaultPort"/>.
    /// - Serial is trimmed, and a blank serial becomes null so callers can treat it as "not set".
    /// Note that this differs from the raw JSON surface it replaced, which passed these values through untouched.
    /// </summary>
    public static DeviceConnectionConfig Read(JsonElement root)
    {
        JsonConfigReader device = new JsonConfigReader(root).Section("device_connection");
        string host = device.String("host", DeviceConnectionConfig.DefaultHost).Trim();
        return new DeviceConnectionConfig(
            string.IsNullOrWhiteSpace(host) ? DeviceConnectionConfig.DefaultHost : host,
            device.Int("port", DeviceConnectionConfig.DefaultPort, 1, 65535),
            Optional(device.String("serial", string.Empty)),
            device.String("emulator_type", DeviceConnectionConfig.DefaultEmulatorType),
            device.String("emulator_path", string.Empty),
            device.String("emulator_instance", string.Empty));
    }

    private static string? Optional(string value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
