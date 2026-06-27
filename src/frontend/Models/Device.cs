namespace CvAut.Models
{
    /// <summary>
    /// A target emulator/device. <see cref="Id"/> (host:port or ADB serial) is the runtime
    /// scope key for all state per the "device-scoped by default" architecture.
    /// </summary>
    public sealed class Device
    {
        public Device(string id, string host, int port, string? serial = null, string? displayName = null)
        {
            Id = id;
            Host = host;
            Port = port;
            Serial = serial;
            DisplayName = displayName ?? id;
        }

        /// <summary>Runtime scope key. Derived from serial or "host:port".</summary>
        public string Id { get; }

        public string Host { get; }

        public int Port { get; }

        /// <summary>ADB serial if known (e.g. "127.0.0.1:5556").</summary>
        public string? Serial { get; }

        public string DisplayName { get; }

        /// <summary>Builds the canonical device id from an endpoint.</summary>
        public static string MakeId(string host, int port) => host + ":" + port;
    }
}
