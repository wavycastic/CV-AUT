using System.Collections.Generic;
using System.Threading;
using CvAut.Models;

namespace CvAut.Services.Emulators.Scanners
{
    /// <summary>
    /// Scans devices already visible to the local ADB server via
    /// <c>BackendDiagnostics.ListAdbDevicesWithStatus()</c>. Emits candidates with a
    /// <see cref="DeviceStatus"/> <c>StatusHint</c> derived from the ADB device state
    /// (device/offline/unauthorized) so the orchestrator can distinguish ready vs
    /// unusable devices without a separate probe. Parses serials like "127.0.0.1:5556"
    /// and "emulator-5554" into endpoint candidates.
    /// </summary>
    public sealed class AdbConnectedDeviceScanner : IDeviceScanner
    {
        public IEnumerable<DeviceCandidate> Scan(CancellationToken cancellationToken = default)
        {
            foreach ((string serial, string state) in BackendDiagnostics.ListAdbDevicesWithStatus())
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!AdbEndpoint.TryParse(serial, out string host, out int port))
                {
                    continue;
                }

                yield return new DeviceCandidate(host, port, serial, "ADB", serial, MapAdbState(state));
            }
        }

        /// <summary>Maps an ADB state string ("Device"/"Offline"/"Unauthorized"/...) to a <see cref="DeviceStatus"/>.</summary>
        internal static DeviceStatus MapAdbState(string state)
        {
            if (string.IsNullOrEmpty(state))
            {
                return DeviceStatus.Unknown;
            }

            return state.ToUpperInvariant() switch
            {
                "DEVICE" => DeviceStatus.Ready,
                "ONLINE" => DeviceStatus.Ready,
                "OFFLINE" => DeviceStatus.Offline,
                "UNAUTHORIZED" => DeviceStatus.Unauthorized,
                _ => DeviceStatus.Unknown,
            };
        }
    }
}
