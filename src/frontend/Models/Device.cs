namespace CvAut.Models
{
    public enum DeviceStatus
    {
        Ready,
        Offline,
        Unauthorized,
        Unknown,
        /// <summary>Emulator install detected but not running / ADB not online yet. Start allowed.</summary>
        Installed,
    }

    /// <summary>
    /// A detected target emulator/device. <see cref="Id"/> is the canonical endpoint
    /// key used for UI selection and runtime state.
    /// </summary>
    public sealed class Device
    {
        public Device(
            string host,
            int port,
            string? name = null,
            string? source = null,
            DeviceStatus status = DeviceStatus.Unknown,
            string? serial = null,
            string? displayName = null,
            string? emulatorType = null,
            string? emulatorPath = null,
            string? emulatorInstance = null)
        {
            Host = host;
            Port = port;
            Id = MakeId(host, port);
            Name = string.IsNullOrWhiteSpace(name) ? Id : name;
            Source = string.IsNullOrWhiteSpace(source) ? "Không xác định" : source;
            Status = status;
            Serial = serial;
            DisplayName = string.IsNullOrWhiteSpace(displayName) ? BuildDisplayName(Name, Source, Status) : displayName;
            EmulatorType = emulatorType;
            EmulatorPath = string.IsNullOrWhiteSpace(emulatorPath) ? null : emulatorPath;
            EmulatorInstance = string.IsNullOrWhiteSpace(emulatorInstance) ? null : emulatorInstance;
        }

        /// <summary>Canonical endpoint identity in "host:port" form.</summary>
        public string Id { get; }

        public string Name { get; }

        public string Host { get; }

        public int Port { get; }

        public string Source { get; }

        public DeviceStatus Status { get; }

        /// <summary>ADB serial if known (e.g. "127.0.0.1:5556").</summary>
        public string? Serial { get; }

        public string DisplayName { get; }

        /// <summary>Vendor emulator type when known (e.g. "BlueStacks", "LDPlayer"). Backend uses this to pick the right launch path.</summary>
        public string? EmulatorType { get; }

        /// <summary>Absolute path to the emulator executable when known. Enables auto-start for <see cref="DeviceStatus.Installed"/> devices.</summary>
        public string? EmulatorPath { get; }

        /// <summary>Vendor instance key when known (e.g. BlueStacks "Pie64" / "Rvc64"). Lets backend launch and configure the exact instance during cold-start.</summary>
        public string? EmulatorInstance { get; }

        /// <summary>True when the emulator executable path is known and Start can auto-launch it.</summary>
        public bool CanAutoStart => !string.IsNullOrWhiteSpace(EmulatorPath);

        /// <summary>Stable per-device config profile key, derived from the canonical <see cref="Id"/>
        /// (host:port) so it never drifts when the display name changes.</summary>
        public string ProfileKey => MakeProfileKey(Id);

        /// <summary>Builds the per-device profile key from a device id (host:port).</summary>
        public static string MakeProfileKey(string id) => "device_" + id.Replace(':', '_').Replace(' ', '_');

        /// <summary>Builds the canonical device id from an endpoint.</summary>
        public static string MakeId(string host, int port) => host + ":" + port;

        private static string BuildDisplayName(string name, string source, DeviceStatus status)
        {
            string label = string.IsNullOrWhiteSpace(name) ? source : name;
            if (status == DeviceStatus.Ready)
            {
                return label;
            }
            string statusStr = status switch
            {
                DeviceStatus.Offline => "Ngoại tuyến",
                DeviceStatus.Unauthorized => "Chưa ủy quyền",
                DeviceStatus.Unknown => "Không xác định",
                DeviceStatus.Installed => "Chưa chạy",
                _ => status.ToString()
            };
            return label + " (" + statusStr + ")";
        }
    }
}
