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
