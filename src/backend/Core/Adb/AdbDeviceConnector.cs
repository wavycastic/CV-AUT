using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using SharpAdbClient;

namespace CvAut.Adb
{
    internal sealed class AdbDeviceConnector : IAdbDeviceConnector
    {
        private static readonly int[] DefaultFallbackPorts = { 5556, 5555, 5557, 5554, 5565 };
        private readonly IAdbCommandRunner _runner;
        private readonly Func<IEnumerable<int>> _discoverBlueStacksPorts;

        public AdbDeviceConnector(IAdbCommandRunner runner)
            : this(runner, DiscoverBlueStacksPorts)
        {
        }

        internal AdbDeviceConnector(IAdbCommandRunner runner, Func<IEnumerable<int>> discoverBlueStacksPorts)
        {
            _runner = runner ?? throw new ArgumentNullException(nameof(runner));
            _discoverBlueStacksPorts = discoverBlueStacksPorts
                ?? throw new ArgumentNullException(nameof(discoverBlueStacksPorts));
        }

        public AdbDeviceConnection Connect(string host, int port, string? preferredSerial = null)
        {
            if (string.IsNullOrWhiteSpace(host))
                throw new ArgumentException("ADB host is required.", nameof(host));
            if (port is < 1 or > 65535)
                throw new ArgumentOutOfRangeException(nameof(port));

            StartServer();
            string normalizedHost = host.Trim();
            string configuredAddress = $"{normalizedHost}:{port}";
            string? preferred = string.IsNullOrWhiteSpace(preferredSerial) ? null : preferredSerial.Trim();

            if (preferred is not null && TrySelectExistingDevice(preferred, out DeviceData preferredDevice))
                return CreateConnection(normalizedHost, port, preferredDevice, true);

            if (TryConnectAndSelectDevice(normalizedHost, port, out DeviceData configuredDevice))
                return CreateConnection(normalizedHost, port, configuredDevice, true);

            foreach (int fallbackPort in BuildFallbackPorts(port, _discoverBlueStacksPorts(), DefaultFallbackPorts))
            {
                if (TryConnectAndSelectDevice(normalizedHost, fallbackPort, out DeviceData fallbackDevice))
                    return CreateConnection(normalizedHost, fallbackPort, fallbackDevice, true);
            }

            try
            {
                DeviceData? activeDevice = AdbClient.Instance.GetDevices().FirstOrDefault();
                if (activeDevice is not null)
                {
                    string activeHost = normalizedHost;
                    int activePort = port;
                    if (TryParseEndpointSerial(activeDevice.Serial, out string parsedHost, out int parsedPort))
                    {
                        activeHost = parsedHost;
                        activePort = parsedPort;
                    }
                    return CreateConnection(activeHost, activePort, activeDevice, true);
                }
            }
            catch (Exception ex)
            {
                Log("get_devices", ex.Message);
            }

            var disconnectedDevice = new DeviceData { Serial = preferred ?? configuredAddress };
            return CreateConnection(normalizedHost, port, disconnectedDevice, false);
        }

        internal static IReadOnlyList<int> BuildFallbackPorts(
            int configuredPort,
            IEnumerable<int> discoveredPorts,
            IEnumerable<int> defaultPorts)
        {
            var result = new List<int>();
            foreach (int candidate in discoveredPorts.Concat(defaultPorts))
            {
                if (candidate is < 1 or > 65535 || candidate == configuredPort || result.Contains(candidate))
                    continue;
                result.Add(candidate);
            }
            return result;
        }

        internal static bool TryParseEndpointSerial(string serial, out string host, out int port)
        {
            host = "127.0.0.1";
            port = 5556;
            if (string.IsNullOrWhiteSpace(serial)) return false;

            int separator = serial.LastIndexOf(':');
            if (separator <= 0 || separator >= serial.Length - 1) return false;
            if (!int.TryParse(serial[(separator + 1)..], out int parsedPort) || parsedPort is < 1 or > 65535)
                return false;

            host = serial[..separator];
            port = parsedPort;
            return true;
        }

        private void StartServer()
        {
            try
            {
                new AdbServer().StartServer(_runner.AdbExePath, restartServerIfNewer: false);
            }
            catch (Exception ex)
            {
                Log("start_server", ex.Message);
            }
        }

        private static bool TrySelectExistingDevice(string serial, out DeviceData device)
        {
            try
            {
                DeviceData? match = AdbClient.Instance.GetDevices().FirstOrDefault(candidate =>
                    string.Equals(candidate.Serial, serial, StringComparison.OrdinalIgnoreCase));
                if (match is not null)
                {
                    device = match;
                    return true;
                }
            }
            catch (Exception ex)
            {
                Log("list_devices", ex.Message);
            }

            device = new DeviceData { Serial = serial };
            return false;
        }

        private static bool TryConnectAndSelectDevice(string host, int port, out DeviceData device)
        {
            string serial = $"{host}:{port}";
            try
            {
                AdbClient.Instance.Connect(new IPEndPoint(ResolveAddress(host), port));
            }
            catch (Exception ex)
            {
                Log("connect", ex.Message);
            }
            return TrySelectExistingDevice(serial, out device);
        }

        private static IPAddress ResolveAddress(string host)
        {
            if (IPAddress.TryParse(host, out IPAddress? address)) return address;
            return Dns.GetHostAddresses(host).First(candidate =>
                candidate.AddressFamily is System.Net.Sockets.AddressFamily.InterNetwork
                    or System.Net.Sockets.AddressFamily.InterNetworkV6);
        }

        private static AdbDeviceConnection CreateConnection(
            string host,
            int port,
            DeviceData device,
            bool connected)
            => new(host, port, device.Serial, device, connected);

        private static IEnumerable<int> DiscoverBlueStacksPorts()
        {
            string configPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                @"BlueStacks_nxt\bluestacks.conf");
            if (!File.Exists(configPath)) yield break;

            string[] lines;
            try
            {
                lines = File.ReadAllLines(configPath);
            }
            catch (Exception ex)
            {
                Log("read_bluestacks_config", ex.Message);
                yield break;
            }

            foreach (string line in lines)
            {
                if (!line.Contains(".status.adb_port=", StringComparison.OrdinalIgnoreCase)) continue;
                int separator = line.IndexOf('=');
                if (separator <= 0) continue;
                string value = line[(separator + 1)..].Trim(' ', '"', '\'', ';');
                if (int.TryParse(value, out int discoveredPort)) yield return discoveredPort;
            }
        }

        private static void Log(string action, string reason)
            => Console.WriteLine($"[ADB WARNING] phase=connect status=pending action={action} reason=\"{reason}\"");
    }
}
