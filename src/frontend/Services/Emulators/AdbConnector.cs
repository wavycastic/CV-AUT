using System;
using System.Diagnostics;
using System.IO;

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
        public static bool TryConnect(string host, int port)
        {
            string adbPath = Path.Combine(AppContext.BaseDirectory, "adb", "adb.exe");
            if (!File.Exists(adbPath))
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
    }
}
