using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using CvAut.Models;

namespace CvAut.Services.Emulators.Scanners
{
    /// <summary>
    /// Base for vendor install-path scanners: if the emulator's install directory exists,
    /// emit candidates for each common ADB port. When the emulator executable is also
    /// found in one of the install paths, candidates are tagged with
    /// <see cref="DeviceStatus.Installed"/>, the vendor <c>EmulatorType</c> and the
    /// <c>EmulatorPath</c> so the orchestrator can auto-launch the emulator even when it
    /// is not running. Subclasses only declare vendor name, candidate install paths and the
    /// executable filename(s) to probe. Adding a new emulator = one small subclass, no
    /// orchestrator or ViewModel changes.
    /// </summary>
    public abstract class EmulatorInstallScanner : IDeviceScanner
    {
        private readonly string _name;
        private readonly string[] _installPaths;
        private readonly int[] _ports;
        private readonly string[] _exeNames;

        protected EmulatorInstallScanner(string name, string[] installPaths, int[] ports, string[]? exeNames = null)
        {
            _name = name;
            _installPaths = installPaths;
            _ports = ports;
            _exeNames = exeNames ?? Array.Empty<string>();
        }

        public virtual IEnumerable<DeviceCandidate> Scan(CancellationToken cancellationToken = default)
        {
            // Locate the emulator executable by probing each install path × each exe name.
            // The first hit wins (install paths are ordered by likelihood by subclasses).
            string? exePath = null;
            if (_exeNames.Length > 0)
            {
                foreach (string installPath in _installPaths)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (!Directory.Exists(installPath))
                    {
                        continue;
                    }

                    foreach (string exeName in _exeNames)
                    {
                        string candidate = Path.Combine(installPath, exeName);
                        if (File.Exists(candidate))
                        {
                            exePath = candidate;
                            break;
                        }
                    }

                    if (exePath is not null)
                    {
                        break;
                    }
                }
            }

            // If no exe was found, fall back to the legacy behaviour: still emit port
            // candidates when the install directory exists (so an already-running emulator
            // on a common port can still be discovered by the orchestrator's adb connect).
            bool installDirExists = _installPaths.Any(Directory.Exists);
            if (exePath is null && !installDirExists)
            {
                yield break;
            }

            // Installed (exe found) candidates carry the Installed status hint + metadata
            // so they survive even when adb connect fails. Legacy (dir only) candidates
            // stay status-less and rely on the orchestrator's connect attempt.
            DeviceStatus? statusHint = exePath is not null ? DeviceStatus.Installed : null;
            foreach (int port in _ports)
            {
                cancellationToken.ThrowIfCancellationRequested();
                yield return new DeviceCandidate(
                    "127.0.0.1",
                    port,
                    $"{_name} {port}",
                    _name,
                    null,
                    statusHint,
                    _name,
                    exePath);
            }
        }
    }

    /// <summary>
    /// LDPlayer scanner. Locates <c>dnplayer.exe</c> across a broad set of install paths
    /// (covering <c>LDPlayer9</c>, <c>LDPlayer4</c>, <c>LDPlayer</c> variants on drives
    /// C–F including <c>Download</c> subfolders) and enumerates the real LDPlayer
    /// instances via <c>ldconsole.exe list2</c>, emitting one
    /// <see cref="DeviceStatus.Installed"/> candidate per instance with its actual ADB
    /// port (Android emulator convention: instance index <c>i</c> -> adb port
    /// <c>5555 + 2*i</c>, so instance 0 -> 5555). Each candidate is tagged with the
    /// emulator type/path so the orchestrator can auto-launch LDPlayer even when it is
    /// not running. When <c>ldconsole.exe</c> is missing or fails to enumerate, a single
    /// default-instance candidate (port 5555) is emitted as a fallback so a
    /// closed-but-installed LDPlayer is still surfaced.
    ///
    /// This replaces the old blind 7-port emission (5554–5560) which produced seven
    /// duplicate "LDPlayer" devices for a single install.
    /// </summary>
    public sealed class LdPlayerScanner : IDeviceScanner
    {
        // LDPlayer install root candidates. Covers per-major-version subfolders
        // (LDPlayer9 / LDPlayer4 / LDPlayer) across drives C–F including Download subfolders.
        private static readonly string[] InstallRoots =
        {
            // Direct drive roots
            @"C:\LDPlayer\LDPlayer9",
            @"D:\LDPlayer\LDPlayer9",
            @"E:\LDPlayer\LDPlayer9",
            @"F:\LDPlayer\LDPlayer9",
            @"C:\LDPlayer\LDPlayer4",
            @"D:\LDPlayer\LDPlayer4",
            @"E:\LDPlayer\LDPlayer4",
            @"F:\LDPlayer\LDPlayer4",
            @"C:\LDPlayer\LDPlayer",
            @"D:\LDPlayer\LDPlayer",
            @"E:\LDPlayer\LDPlayer",
            @"F:\LDPlayer\LDPlayer",
            // Download subfolders (e.g. user machine: E:\Download\LDPlayer\LDPlayer9)
            @"C:\Download\LDPlayer\LDPlayer9",
            @"D:\Download\LDPlayer\LDPlayer9",
            @"E:\Download\LDPlayer\LDPlayer9",
            @"F:\Download\LDPlayer\LDPlayer9",
            @"C:\Download\LDPlayer\LDPlayer4",
            @"D:\Download\LDPlayer\LDPlayer4",
            @"E:\Download\LDPlayer\LDPlayer4",
            @"F:\Download\LDPlayer\LDPlayer4",
            @"C:\Download\LDPlayer\LDPlayer",
            @"D:\Download\LDPlayer\LDPlayer",
            @"E:\Download\LDPlayer\LDPlayer",
            @"F:\Download\LDPlayer\LDPlayer",
            // Program Files variants
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "LDPlayer", "LDPlayer9"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "LDPlayer", "LDPlayer4"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "LDPlayer", "LDPlayer"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "LDPlayer", "LDPlayer9"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "LDPlayer", "LDPlayer4"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "LDPlayer", "LDPlayer"),
        };

        // LDPlayer default ADB port (instance 0). Used by the ldconsole fallback.
        private const int DefaultInstancePort = 5555;

        public IEnumerable<DeviceCandidate> Scan(CancellationToken cancellationToken = default)
        {
            // Locate dnplayer.exe. ldconsole.exe sits in the same install directory.
            string? dnplayerPath = null;
            foreach (string root in InstallRoots)
            {
                cancellationToken.ThrowIfCancellationRequested();
                string exe = Path.Combine(root, "dnplayer.exe");
                if (File.Exists(exe))
                {
                    dnplayerPath = exe;
                    break;
                }
            }

            // If no dnplayer.exe is found, LDPlayer is not installed (or not where we look):
            // emit nothing — the generic CommonPortScanner still covers the case where an
            // LDPlayer instance is already running with ADB online.
            if (dnplayerPath is null)
            {
                yield break;
            }

            string installDir = Path.GetDirectoryName(dnplayerPath) ?? string.Empty;
            string ldconsolePath = Path.Combine(installDir, "ldconsole.exe");

            // Enumerate real LDPlayer instances via `ldconsole.exe list2`. Each output
            // line is "index,name,...". ADB port follows the Android emulator convention:
            // instance index i -> 5555 + 2*i (instance 0 -> 5555, instance 1 -> 5557, ...).
            bool emittedAny = false;
            if (File.Exists(ldconsolePath))
            {
                foreach ((int index, string name) instance in ListLdPlayerInstances(ldconsolePath))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    int port = DefaultInstancePort + (2 * instance.index);
                    yield return new DeviceCandidate(
                        "127.0.0.1",
                        port,
                        BuildInstanceName(instance.name, port),
                        "LDPlayer",
                        null,
                        DeviceStatus.Installed,
                        "LDPlayer",
                        dnplayerPath);
                    emittedAny = true;
                }
            }

            // Fallback: ldconsole missing/failed -> emit one candidate for the default
            // instance (port 5555) so a closed-but-installed LDPlayer is still surfaced
            // and Start can auto-launch dnplayer.exe (which launches the default instance).
            if (!emittedAny)
            {
                yield return new DeviceCandidate(
                    "127.0.0.1",
                    DefaultInstancePort,
                    "LDPlayer",
                    "LDPlayer",
                    null,
                    DeviceStatus.Installed,
                    "LDPlayer",
                    dnplayerPath);
            }
        }

        /// <summary>
        /// Builds a display name for an LDPlayer instance. Uses the instance name from
        /// <c>ldconsole list2</c> verbatim when it already starts with "LDPlayer"
        /// (avoids the redundant "LDPlayer LDPlayer" label), otherwise prefixes it.
        /// Falls back to "LDPlayer {port}" when the instance name is empty.
        /// </summary>
        private static string BuildInstanceName(string instanceName, int port)
        {
            if (string.IsNullOrWhiteSpace(instanceName))
            {
                return $"LDPlayer {port}";
            }

            if (instanceName.StartsWith("LDPlayer", StringComparison.OrdinalIgnoreCase))
            {
                return instanceName;
            }

            return $"LDPlayer {instanceName}";
        }

        /// <summary>
        /// Runs <c>ldconsole.exe list2</c> and parses the instance list. Each line is
        /// <c>index,name,...</c>. Never throws; returns an empty list on any failure
        /// (missing exe, timeout, malformed output) so the caller can fall back to the
        /// default-instance candidate.
        /// </summary>
        private static List<(int index, string name)> ListLdPlayerInstances(string ldconsolePath)
        {
            string? output = null;
            try
            {
                using var process = Process.Start(new ProcessStartInfo
                {
                    FileName = ldconsolePath,
                    Arguments = "list2",
                    CreateNoWindow = true,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                });

                if (process is null)
                {
                    return new List<(int, string)>();
                }

                if (!process.WaitForExit(3000))
                {
                    try { process.Kill(entireProcessTree: true); } catch { /* best effort */ }
                    return new List<(int, string)>();
                }

                output = process.StandardOutput.ReadToEnd();
            }
            catch
            {
                return new List<(int, string)>();
            }

            var instances = new List<(int, string)>();
            if (string.IsNullOrWhiteSpace(output))
            {
                return instances;
            }

            foreach (string rawLine in output.Split('\n', StringSplitOptions.RemoveEmptyEntries))
            {
                string[] parts = rawLine.Trim().Split(',');
                if (parts.Length < 2)
                {
                    continue;
                }

                if (!int.TryParse(parts[0].AsSpan(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int index))
                {
                    continue;
                }

                string name = parts[1].Trim();
                instances.Add((index, name));
            }

            return instances;
        }
    }

    /// <summary>MEmu install-path scanner.</summary>
    public sealed class MemuScanner : EmulatorInstallScanner
    {
        public MemuScanner()
            : base("MEmu",
                   new[]
                   {
                       Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Microvirt", "MEmu"),
                       Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Microvirt", "MEmu"),
                   },
                   CommonPorts.All,
                   new[] { "MEmu.exe", "MEmuConsole.exe" })
        {
        }
    }

    /// <summary>
    /// BlueStacks install-path scanner. Acts as a fallback for the config-driven
    /// <see cref="BlueStacksScanner"/>: when BlueStacks is installed but its
    /// <c>bluestacks.conf</c> is missing/unreadable, BlueStacks-likely ADB ports are probed.
    /// When <c>bluestacks.conf</c> exists the <see cref="BlueStacksScanner"/> already emits
    /// the exact per-instance <c>adb_port</c>, so this scanner yields nothing to avoid
    /// flooding the device list with every common port (the old behaviour emitted all
    /// <see cref="CommonPorts.All"/> just because <c>HD-Player.exe</c> was found).
    /// BlueStacks_nxt is the current install layout; legacy installs use BlueStacks.
    /// </summary>
    public sealed class BlueStacksInstallScanner : EmulatorInstallScanner
    {
            // BlueStacks-likely ADB ports only. The full CommonPorts set (which includes MuMu /
            // LDPlayer ranges) must NOT be emitted here — it produced ~14 duplicate
        // "BlueStacks" candidates when only one real instance (e.g. 5556) exists.
        private static readonly int[] BlueStacksLikelyPorts = { 5556, 5554, 5557 };

        public BlueStacksInstallScanner()
            : base("BlueStacks",
                   new[]
                   {
                       Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "BlueStacks_nxt"),
                       Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "BlueStacks"),
                       Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "BlueStacks_nxt"),
                       Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "BlueStacks"),
                   },
                   BlueStacksLikelyPorts,
                   new[] { "HD-Player.exe" })
        {
        }

        /// <summary>
        /// Override the base port-emission: when <c>bluestacks.conf</c> exists the
        /// config-driven <see cref="BlueStacksScanner"/> already emits the exact instance
        /// ports, so yielding more candidates here would only create duplicate endpoints.
        /// Only fall back to install-path probing when the conf is missing/unreadable.
        /// </summary>
        public override IEnumerable<DeviceCandidate> Scan(CancellationToken cancellationToken = default)
        {
            string confPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                @"BlueStacks_nxt\bluestacks.conf");

            if (File.Exists(confPath))
            {
                yield break;
            }

            foreach (DeviceCandidate candidate in base.Scan(cancellationToken))
            {
                yield return candidate;
            }
        }
    }

    /// <summary>Android SDK emulator install-path scanner.</summary>
    public sealed class AndroidSdkEmulatorScanner : EmulatorInstallScanner
    {
        public AndroidSdkEmulatorScanner()
            : base("Android Emulator",
                   new[]
                   {
                       Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Android", "Sdk", "emulator"),
                   },
                   CommonPorts.All,
                   new[] { "emulator.exe" })
        {
        }
    }

    /// <summary>
    /// Generic common-port scanner — emits candidates for every common local ADB port
    /// regardless of install path. Acts as a last-resort fallback so an emulator the
    /// other scanners do not recognize can still be discovered if its port is common.
    /// </summary>
    public sealed class CommonPortScanner : IDeviceScanner
    {
        public IEnumerable<DeviceCandidate> Scan(CancellationToken cancellationToken = default)
        {
            foreach (int port in CommonPorts.All)
            {
                cancellationToken.ThrowIfCancellationRequested();
                yield return new DeviceCandidate("127.0.0.1", port, $"Emulator {port}", "CommonPort");
            }
        }
    }
}
