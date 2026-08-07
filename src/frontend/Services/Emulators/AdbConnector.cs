using System;
using System.Diagnostics;
using System.IO;
using System.Net.Sockets;

namespace CvAut.Services.Emulators
{
    /// <summary>
    /// Best-effort <c>adb connect host:port</c>. Returns true only when ADB reports a
    /// successful/already-connected result. Used by the discovery orchestrator; never
    /// throws. Extracted from the old <c>AdbEmulatorDiscovery.TryAdbConnect</c> so
    /// scanners and the orchestrator share one connect path.
    /// </summary>
    public static class AdbConnector
    {
        /// <summary>
        /// Starts the bundled ADB server once so a burst of parallel connect attempts
        /// does not race N adb.exe processes into starting the server on first use
        /// (each startup is slow and the race can stall all of them).
        /// </summary>
        public static void EnsureServerStarted()
        {
            string adbPath = Path.Combine(AppContext.BaseDirectory, "adb", "adb.exe");
            if (!File.Exists(adbPath))
            {
                return;
            }

            try
            {
                using var process = Process.Start(new ProcessStartInfo
                {
                    FileName = adbPath,
                    Arguments = "start-server",
                    CreateNoWindow = true,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                });

                if (process != null && !process.WaitForExit(3000))
                {
                    try { process.Kill(entireProcessTree: true); } catch { /* best effort */ }
                }
            }
            catch
            {
                // Best effort.
            }
        }

        public static bool TryConnect(string host, int port)
        {
            string adbPath = Path.Combine(AppContext.BaseDirectory, "adb", "adb.exe");
            if (!File.Exists(adbPath) || !IsEndpointListening(host, port))
            {
                return false;
            }

            try
            {
                using var process = Process.Start(new ProcessStartInfo
                {
                    FileName = adbPath,
                    Arguments = "connect " + host + ":" + port.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    CreateNoWindow = true,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                });

                if (process is null)
                {
                    return false;
                }

                if (!process.WaitForExit(1500))
                {
                    try
                    {
                        process.Kill(entireProcessTree: true);
                    }
                    catch
                    {
                        // Best effort.
                    }

                    return false;
                }

                string output = process.StandardOutput.ReadToEnd() + process.StandardError.ReadToEnd();
                return output.Contains("connected to", StringComparison.OrdinalIgnoreCase) ||
                       output.Contains("already connected", StringComparison.OrdinalIgnoreCase) ||
                       output.Contains("is already connected", StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Rejects closed local ports before launching adb.exe. A full "Tất cả" scan
        /// includes many fallback ports; letting adb wait 1.5 seconds for every closed
        /// endpoint dominated the total discovery time.
        /// </summary>
        private static bool IsEndpointListening(string host, int port)
        {
            try
            {
                using var client = new TcpClient();
                Task connectTask = client.ConnectAsync(host, port);
                return connectTask.Wait(TimeSpan.FromMilliseconds(150)) && client.Connected;
            }
            catch
            {
                return false;
            }
        }
    }
}
