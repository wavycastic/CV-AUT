using System;
using System.Text.Json;

namespace CvAut.Configuration;

internal sealed record DeviceConnectionConfig(
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
        JsonElement device = ConfigManager.GetObjectOrDefault(root, "device_connection");

        string host = NormalizeRequired(
            ConfigManager.GetStringOrDefault(device, "host", DeviceConnectionConfig.DefaultHost),
            DeviceConnectionConfig.DefaultHost);
        int port = NormalizePort(
            ConfigManager.GetIntOrDefault(device, "port", DeviceConnectionConfig.DefaultPort));

        return new DeviceConnectionConfig(
            Host: host,
            Port: port,
            Serial: NormalizeOptional(ConfigManager.GetStringOrDefault(device, "serial", string.Empty)),
            EmulatorType: NormalizeRequired(
                ConfigManager.GetStringOrDefault(device, "emulator_type", DeviceConnectionConfig.DefaultEmulatorType),
                DeviceConnectionConfig.DefaultEmulatorType),
            EmulatorPath: NormalizeRequired(
                ConfigManager.GetStringOrDefault(device, "emulator_path", string.Empty),
                string.Empty),
            EmulatorInstance: NormalizeRequired(
                ConfigManager.GetStringOrDefault(device, "emulator_instance", string.Empty),
                string.Empty));
    }

    private static int NormalizePort(int port)
        => port is >= 1 and <= 65535 ? port : DeviceConnectionConfig.DefaultPort;

    private static string NormalizeRequired(string value, string fallback)
        => string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();

    private static string? NormalizeOptional(string value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
