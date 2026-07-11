using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CvAut.Models;
using CvAut.Services.Emulators.Scanners;

namespace CvAut.Services.Emulators
{
    public interface IEmulatorDiscovery
    {
        Task<IReadOnlyList<Device>> DiscoverAsync(string? emulatorFilter = null, CancellationToken cancellationToken = default);
        Task<EmulatorDisplayInfo> GetDisplayInfoAsync(Device device);
    }

    public sealed class EmulatorDisplayInfo
    {
        private readonly string? _emulatorType;

        public EmulatorDisplayInfo(int width, int height, int densityDpi, string raw, string? emulatorType = null)
        {
            Width = width;
            Height = height;
            DensityDpi = densityDpi;
            Raw = raw;
            _emulatorType = emulatorType;
        }

        public int Width { get; }
        public int Height { get; }
        public int DensityDpi { get; }
        public string Raw { get; }
        public bool ResolutionOk => Width == 1600 && Height == 900;
        public bool DpiOk
        {
            get
            {
                if (!string.IsNullOrWhiteSpace(_emulatorType) && _emulatorType.Equals("BlueStacks", StringComparison.OrdinalIgnoreCase))
                {
                    return DensityDpi == 300;
                }
                return DensityDpi == 240;
            }
        }
    }

    /// <summary>
    /// Discovery orchestrator. Runs the registered <see cref="IDeviceScanner"/> strategies,
    /// collects raw candidates, merges duplicates by endpoint (combined source labels,
    /// best name, best status), performs best-effort <c>adb connect</c> for non-ADB
    /// candidates, resolves <see cref="DeviceStatus"/> via a final ADB probe, and returns
    /// a stable, normalized <see cref="Device"/> list. Scanner internals are not leaked to
    /// ViewModels — they only see <see cref="IEmulatorDiscovery"/>.
    /// </summary>
    public sealed class AdbEmulatorDiscovery : IEmulatorDiscovery
    {
        private readonly IReadOnlyList<IDeviceScanner> _scanners;

        public AdbEmulatorDiscovery()
            : this(DefaultScanners())
        {
        }

        public AdbEmulatorDiscovery(IEnumerable<IDeviceScanner> scanners)
        {
            _scanners = scanners.ToList();
        }

        public Task<IReadOnlyList<Device>> DiscoverAsync(string? emulatorFilter = null, CancellationToken cancellationToken = default)
        {
            return Task.Run<IReadOnlyList<Device>>(() =>
            {
                var devices = new Dictionary<string, Device>(StringComparer.OrdinalIgnoreCase);

                var activeScanners = _scanners;
                if (!string.IsNullOrWhiteSpace(emulatorFilter) && !string.Equals(emulatorFilter, "Tất cả", StringComparison.OrdinalIgnoreCase))
                {
                    activeScanners = _scanners.Where(s => IsScannerMatch(s, emulatorFilter)).ToList();
                }

                // Pass 1: collect candidates from every scanner, grouped by endpoint so
                // duplicate sources for the same host:port can be merged (item 6).
                var groups = new Dictionary<string, List<DeviceCandidate>>(StringComparer.OrdinalIgnoreCase);
                foreach (IDeviceScanner scanner in activeScanners)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    foreach (DeviceCandidate candidate in scanner.Scan(cancellationToken))
                    {
                        if (!groups.TryGetValue(candidate.Id, out List<DeviceCandidate>? list))
                        {
                            list = new List<DeviceCandidate>();
                            groups[candidate.Id] = list;
                        }
                        list.Add(candidate);
                    }
                }

                // Merge each endpoint group: combined source label (e.g. "ADB, BlueStacks"),
                // best name (vendor-specific > ADB serial > generic), best status hint,
                // first known serial, best emulator metadata. Produces one candidate per
                // endpoint.
                var candidates = new Dictionary<string, DeviceCandidate>(StringComparer.OrdinalIgnoreCase);
                foreach (KeyValuePair<string, List<DeviceCandidate>> g in groups)
                {
                    candidates[g.Key] = MergeCandidates(g.Value);
                }

                // Pass 2: candidates already visible to ADB carry a StatusHint — no
                // connect attempt needed. Status comes from the merged hint. Note: an
                // Installed hint is NOT an ADB-confirmed state, so it is handled in pass 3
                // (we still attempt adb connect for Installed candidates to upgrade them).
                foreach (DeviceCandidate c in candidates.Values.Where(c => c.StatusHint != null && c.StatusHint != DeviceStatus.Installed))
                {
                    devices[c.Id] = ToDevice(c, c.StatusHint!.Value);
                }

                // Pass 3: best-effort adb connect for candidates without an ADB-confirmed
                // status hint (this includes Installed candidates from vendor scanners).
                // A successful connect does NOT imply Ready — the device may still be
                // unauthorized or offline. Status is resolved in pass 4. When connect fails,
                // keep Installed candidates (vendor scanner found the emulator executable)
                // so a closed-but-installed emulator is still surfaced to the user instead
                // of being dropped — Start can auto-launch it via the bootstrapper.
                bool anyConnected = false;
                var connectTasks = candidates.Values
                    .Where(c => c.StatusHint == null || c.StatusHint == DeviceStatus.Installed)
                    .Select(async c =>
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        bool alreadyExisted;
                        lock (devices)
                        {
                            alreadyExisted = devices.ContainsKey(c.Id);
                        }

                        if (alreadyExisted)
                        {
                            return;
                        }

                        bool isInstalled = c.StatusHint == DeviceStatus.Installed || !string.IsNullOrWhiteSpace(c.EmulatorPath);
                        bool connected = await Task.Run(() => AdbConnector.TryConnect(c.Host, c.Port), cancellationToken);

                        lock (devices)
                        {
                            if (connected)
                            {
                                anyConnected = true;
                                devices[c.Id] = ToDevice(c, isInstalled ? DeviceStatus.Installed : DeviceStatus.Unknown);
                            }
                            else if (isInstalled)
                            {
                                devices[c.Id] = ToDevice(c, DeviceStatus.Installed);
                            }
                        }
                    })
                    .ToArray();

                Task.WhenAll(connectTasks).GetAwaiter().GetResult();

                // Pass 4: re-probe ADB after connect attempts. Devices that only became
                // visible after connect, or that are unauthorized/offline despite a
                // successful connect, get their real status here. Also upgrades ADB
                // candidates from pass 2 and Installed candidates from pass 3 if a richer
                // state is now available (Installed -> Ready / Offline / Unauthorized).
                // A probe miss leaves the pass-2/pass-3 status untouched (e.g. an Installed
                // emulator that connected but is still booting stays Installed until ADB
                // reports a concrete state).
                if (anyConnected || candidates.Values.Any(c => c.StatusHint != null))
                {
                    Dictionary<string, DeviceStatus> probe = BuildAdbStatusProbe();
                    foreach (DeviceCandidate c in candidates.Values)
                    {
                        if (probe.TryGetValue(c.Id, out DeviceStatus status))
                        {
                            devices[c.Id] = ToDevice(c, status);
                        }
                    }
                }

                // Stable ordering by endpoint so repeated detection yields the same display.
                return devices.Values.OrderBy(d => d.Id, StringComparer.OrdinalIgnoreCase).ToList();
            }, cancellationToken);
        }

        public Task<EmulatorDisplayInfo> GetDisplayInfoAsync(Device device)
        {
            return Task.Run(() =>
            {
                var info = BackendDiagnostics.GetEmulatorDisplayInfo(device.Host, device.Port, device.Serial);
                return new EmulatorDisplayInfo(info.Width, info.Height, info.DensityDpi, info.Raw, device.EmulatorType);
            });
        }

        private static Device ToDevice(DeviceCandidate c, DeviceStatus status)
        {
            // Prefer the ADB serial when known (e.g. "127.0.0.1:5556"), else the
            // scanner-provided name. Status is exposed separately on Device.Status, so
            // keep DisplayName free of status suffixes to avoid duplicated UI labels.
            string displayName = string.IsNullOrWhiteSpace(c.Serial) ? c.Name : c.Serial;
            return new Device(c.Host, c.Port, c.Name, c.Source, status, c.Serial, displayName, c.EmulatorType, c.EmulatorPath, c.EmulatorInstance);
        }

        /// <summary>
        /// Builds an endpoint-keyed map of real ADB device states by re-querying the
        /// ADB server. Used after connect attempts so the orchestrator can resolve
        /// Ready/Offline/Unauthorized accurately instead of assuming Ready.
        /// </summary>
        private static Dictionary<string, DeviceStatus> BuildAdbStatusProbe()
        {
            var probe = new Dictionary<string, DeviceStatus>(StringComparer.OrdinalIgnoreCase);
            foreach ((string serial, string state) in BackendDiagnostics.ListAdbDevicesWithStatus())
            {
                if (AdbEndpoint.TryParse(serial, out string host, out int port))
                {
                    probe[Device.MakeId(host, port)] = Scanners.AdbConnectedDeviceScanner.MapAdbState(state);
                }
            }
            return probe;
        }

        /// <summary>
        /// Merges duplicate candidates for the same endpoint into one: combines source
        /// labels (e.g. "ADB, BlueStacks"), picks the most informative name (vendor-
        /// specific over generic ADB), prefers a known ADB serial, and keeps the best
        /// status hint (Ready &gt; Unauthorized &gt; Offline &gt; Unknown). All candidates
        /// in <paramref name="group"/> share the same Id (host:port).
        /// </summary>
        private static DeviceCandidate MergeCandidates(List<DeviceCandidate> group)
        {
            DeviceCandidate first = group[0];

            // Source: join distinct labels alphabetically for a deterministic, readable
            // composite (e.g. "ADB, BlueStacks"). Drop the generic "CommonPort" noise when
            // a higher-rank source (vendor or ADB) already identifies the endpoint, so a
            // vendor-confirmed device shows "BlueStacks" / "LDPlayer" instead of
            // "BlueStacks, CommonPort". CommonPort still surfaces when it is the only
            // source (an unrecognized emulator on a common port).
            List<string> sources = group
                .Select(c => c.Source)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            int maxSourceRank = sources.Count > 0 ? sources.Max(SourceRank) : 0;
            if (maxSourceRank > 1)
            {
                sources.RemoveAll(s => SourceRank(s) == 1);
            }
            string mergedSource = string.Join(", ", sources.OrderBy(s => s, StringComparer.OrdinalIgnoreCase));

            // Name: prefer vendor-specific names over generic ADB serials over
            // CommonPort placeholders. Ties break alphabetically for stability.
            string name = group
                .OrderByDescending(c => SourceRank(c.Source))
                .ThenBy(c => c.Name, StringComparer.OrdinalIgnoreCase)
                .Select(c => c.Name)
                .FirstOrDefault() ?? first.Name;

            // Serial: prefer the first non-empty serial (typically from the ADB scanner).
            string? serial = group
                .Select(c => c.Serial)
                .FirstOrDefault(s => !string.IsNullOrWhiteSpace(s));

            // StatusHint: keep the best status if any source provided one. Installed is
            // kept here only as a hint — pass 3 still attempts adb connect to upgrade it.
            DeviceStatus? statusHint = group
                .Select(c => c.StatusHint)
                .Where(s => s != null)
                .OrderByDescending(s => StatusRank(s!.Value))
                .FirstOrDefault();

            // EmulatorType: prefer the vendor-specific source's type. Vendor scanners set
            // this to their source name; ADB/CommonPort candidates leave it null. Pick the
            // first non-null (ordered by source rank so vendor wins over ADB).
            string? emulatorType = group
                .OrderByDescending(c => SourceRank(c.Source))
                .Select(c => c.EmulatorType)
                .FirstOrDefault(t => !string.IsNullOrWhiteSpace(t));

            // EmulatorPath: prefer the first non-empty path. Vendor scanners that locate
            // the executable set this; keep it so auto-launch works post-merge.
            string? emulatorPath = group
                .OrderByDescending(c => SourceRank(c.Source))
                .Select(c => c.EmulatorPath)
                .FirstOrDefault(p => !string.IsNullOrWhiteSpace(p));

            // EmulatorInstance: vendor-specific instance key (e.g. BlueStacks "Rvc64").
            // Needed so cold-start launches and configures the exact instance.
            string? emulatorInstance = group
                .OrderByDescending(c => SourceRank(c.Source))
                .Select(c => c.EmulatorInstance)
                .FirstOrDefault(i => !string.IsNullOrWhiteSpace(i));

            return new DeviceCandidate(first.Host, first.Port, name, mergedSource, serial, statusHint, emulatorType, emulatorPath, emulatorInstance);
        }

        /// <summary>Source name quality: vendor-specific &gt; ADB &gt; CommonPort &gt; unknown.</summary>
        private static int SourceRank(string source)
        {
            return source.ToUpperInvariant() switch
            {
                "BLUESTACKS" => 3,
                "LDPLAYER" => 3,
                "MEMU" => 3,
                "ANDROID EMULATOR" => 3,
                "ADB" => 2,
                "COMMONPORT" => 1,
                _ => 0,
            };
        }

        /// <summary>Status quality: Ready &gt; Installed &gt; Unauthorized &gt; Offline &gt; Unknown.</summary>
        private static int StatusRank(DeviceStatus status)
        {
            return status switch
            {
                DeviceStatus.Ready => 4,
                DeviceStatus.Installed => 3,
                DeviceStatus.Unauthorized => 2,
                DeviceStatus.Offline => 1,
                DeviceStatus.Unknown => 0,
                _ => -1,
            };
        }

        private static bool IsScannerMatch(IDeviceScanner scanner, string filter)
        {
            string name = scanner.GetType().Name.ToUpperInvariant();
            if (name.Contains("ADBCONNECTED") || name.Contains("COMMONPORT"))
            {
                return true;
            }
            return filter.ToUpperInvariant() switch
            {
                "BLUESTACKS" => name.Contains("BLUESTACKS"),
                "LDPLAYER" => name.Contains("LDPLAYER"),
                "MEMU" => name.Contains("MEMU"),
                _ => true
            };
        }

        private static IEnumerable<IDeviceScanner> DefaultScanners()
        {
            return new IDeviceScanner[]
            {
                new Scanners.AdbConnectedDeviceScanner(),
                new Scanners.BlueStacksScanner(),
                new Scanners.BlueStacksInstallScanner(),
                new Scanners.LdPlayerScanner(),
                new Scanners.MemuScanner(),
                new Scanners.AndroidSdkEmulatorScanner(),
                new Scanners.CommonPortScanner(),
            };
        }
    }
}
