using System;
using System.Globalization;

namespace CvAut.Services.Emulators
{
    /// <summary>
    /// Parses an ADB serial like "127.0.0.1:5556" or "emulator-5554" into a
    /// host/port endpoint. "emulator-NNNN" maps to 127.0.0.1:NNNN. Shared by
    /// <see cref="Scanners.AdbConnectedDeviceScanner"/> and the discovery
    /// orchestrator so both resolve endpoint identity identically.
    /// </summary>
    public static class AdbEndpoint
    {
        public static bool TryParse(string serial, out string host, out int port)
        {
            host = "127.0.0.1";
            port = 0;
            if (string.IsNullOrWhiteSpace(serial))
            {
                return false;
            }

            int sep = serial.LastIndexOf(':');
            if (sep > 0 && int.TryParse(serial.AsSpan(sep + 1), NumberStyles.Integer, CultureInfo.InvariantCulture, out port))
            {
                host = serial[..sep];
                return true;
            }

            const string emulatorPrefix = "emulator-";
            if (serial.StartsWith(emulatorPrefix, StringComparison.OrdinalIgnoreCase) &&
                int.TryParse(serial.AsSpan(emulatorPrefix.Length), NumberStyles.Integer, CultureInfo.InvariantCulture, out port))
            {
                return true;
            }

            return false;
        }
    }
}
