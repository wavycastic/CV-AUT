using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using System.Threading;
using CvAut.Models;

namespace CvAut.Services.Emulators.Scanners
{
    /// <summary>
    /// Reads <c>C:\ProgramData\BlueStacks_nxt\bluestacks.conf</c> and extracts the
    /// <c>adb_port</c> for each instance, paired with the BlueStacks player executable
    /// (<c>HD-Player.exe</c>) so the orchestrator can emit <see cref="DeviceStatus.Installed"/>
    /// candidates that survive even when the emulator is not running / ADB not listening.
    ///
    /// Config lines look like:
    ///   bst.instance.Pie64.adb_port="5556"
    ///   bst.instance.Pie64.status.adb_port="5556"
    /// The instance name (e.g. <c>Pie64</c>) is captured for a friendlier display name.
    /// </summary>
    public sealed class BlueStacksScanner : IDeviceScanner
    {
        private static readonly Regex InstancePortPattern =
            new(@"bst\.instance\.([A-Za-z0-9_]+)\.adb_port\s*=\s*[""']?(\d+)", RegexOptions.Compiled);

        private static readonly Regex InstanceNamePattern =
            new(@"bst\.instance\.([A-Za-z0-9_]+)\.display_name\s*=\s*[""']?([^""'\r\n]+)", RegexOptions.Compiled);

        // HD-Player.exe path candidates, ordered by likelihood. BlueStacks_nxt is the
        // current install layout; legacy installs use the BlueStacks folder.
        private static readonly string[] HdPlayerCandidates =
        {
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "BlueStacks_nxt", "HD-Player.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "BlueStacks", "HD-Player.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "BlueStacks_nxt", "HD-Player.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "BlueStacks", "HD-Player.exe"),
        };

        public IEnumerable<DeviceCandidate> Scan(CancellationToken cancellationToken = default)
        {
            string confPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                @"BlueStacks_nxt\bluestacks.conf");

            if (!File.Exists(confPath))
            {
                yield break;
            }

            // Locate the player executable once. BlueStacks ships one HD-Player.exe that
            // launches the configured instance via command line args (later improvement).
            string? hdPlayerPath = null;
            foreach (string candidate in HdPlayerCandidates)
            {
                if (File.Exists(candidate))
                {
                    hdPlayerPath = candidate;
                    break;
                }
            }

            var ports = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            var displayNames = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            foreach (string line in File.ReadLines(confPath))
            {
                cancellationToken.ThrowIfCancellationRequested();

                Match portMatch = InstancePortPattern.Match(line);
                if (portMatch.Success)
                {
                    string instanceKey = portMatch.Groups[1].Value;
                    if (int.TryParse(portMatch.Groups[2].Value, out int port) && port > 0 && port <= 65535)
                    {
                        ports[instanceKey] = port;
                    }
                    continue;
                }

                Match nameMatch = InstanceNamePattern.Match(line);
                if (nameMatch.Success)
                {
                    string instanceKey = nameMatch.Groups[1].Value;
                    string displayName = nameMatch.Groups[2].Value;
                    displayNames[instanceKey] = displayName;
                }
            }

            var seenPorts = new HashSet<int>();
            foreach (var kvp in ports)
            {
                string instanceKey = kvp.Key;
                int port = kvp.Value;

                if (!seenPorts.Add(port))
                {
                    continue;
                }

                displayNames.TryGetValue(instanceKey, out string? customName);
                string name = string.IsNullOrWhiteSpace(customName)
                    ? $"BlueStacks {instanceKey}"
                    : customName;

                yield return new DeviceCandidate(
                    "127.0.0.1",
                    port,
                    name,
                    "BlueStacks",
                    null,
                    DeviceStatus.Installed,
                    "BlueStacks",
                    hdPlayerPath,
                    instanceKey);
            }
        }
    }
}
