using System.Collections.Generic;
using System.Threading;
using CvAut.Models;

namespace CvAut.Services.Emulators
{
    /// <summary>
    /// One focused discovery strategy. A scanner only produces raw endpoint candidates
    /// (host/port/name/source); the <see cref="IEmulatorDiscovery"/> orchestrator is
    /// responsible for connect attempts, dedupe and status resolution.
    /// </summary>
    public interface IDeviceScanner
    {
        IEnumerable<DeviceCandidate> Scan(CancellationToken cancellationToken = default);
    }

    /// <summary>
    /// Raw device candidate emitted by a scanner. Status is not known here in general —
    /// the orchestrator decides <see cref="DeviceStatus"/> after ADB connect attempts.
    /// ADB-connected scanners may set <see cref="StatusHint"/> from the ADB device state
    /// so the orchestrator can distinguish ready vs unauthorized/offline without a probe.
    /// </summary>
    public sealed record DeviceCandidate(
        string Host,
        int Port,
        string Name,
        string Source,
        string? Serial = null,
        DeviceStatus? StatusHint = null,
        string? EmulatorType = null,
        string? EmulatorPath = null,
        string? EmulatorInstance = null)
    {
        public string Id => Device.MakeId(Host, Port);
    }
}
