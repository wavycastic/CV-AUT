using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;

namespace CvAut.Adb
{
    /// <summary>
    /// Executes the bundled adb.exe and owns process timeout/error handling.
    /// </summary>
    internal sealed class AdbProcessRunner : IAdbCommandRunner
    {
        private const int DefaultTimeoutMs = 5000;

        private readonly string _adbExePath;
        private readonly int _timeoutMs;

        public AdbProcessRunner()
            : this(Path.Combine(AppContext.BaseDirectory, "adb", "adb.exe"), DefaultTimeoutMs)
        {
        }

        internal AdbProcessRunner(string adbExePath, int timeoutMs = DefaultTimeoutMs)
        {
            if (string.IsNullOrWhiteSpace(adbExePath))
            {
                throw new ArgumentException("ADB executable path is required.", nameof(adbExePath));
            }

            if (timeoutMs <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(timeoutMs), "Timeout must be greater than zero.");
            }

            _adbExePath = adbExePath;
            _timeoutMs = timeoutMs;
        }

        public string AdbExePath => _adbExePath;

        public string RunAdbCommand(string deviceAddress, string arguments)
        {
            string fullArguments = string.IsNullOrWhiteSpace(deviceAddress)
                ? arguments
                : $"-s {deviceAddress} {arguments}";

            return RunRawAdbCommand(fullArguments);
        }

        public string RunRawAdbCommand(string arguments)
        {
            try
            {
                var startInfo = new ProcessStartInfo
                {
                    FileName = _adbExePath,
                    Arguments = arguments,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                using Process? process = Process.Start(startInfo);
                if (process is null)
                {
                    return "Error: Failed to start process";
                }

                Task<string> outputTask = process.StandardOutput.ReadToEndAsync();
                Task<string> errorTask = process.StandardError.ReadToEndAsync();

                if (!process.WaitForExit(_timeoutMs))
                {
                    TryKill(process);
                    return $"Error: ADB command timed out after {_timeoutMs} ms";
                }

                string output = outputTask.GetAwaiter().GetResult().Trim();
                string error = errorTask.GetAwaiter().GetResult().Trim();

                if (process.ExitCode != 0)
                {
                    string details = !string.IsNullOrWhiteSpace(error)
                        ? error
                        : !string.IsNullOrWhiteSpace(output)
                            ? output
                            : $"Process exited with code {process.ExitCode}";

                    return $"Error: {details}";
                }

                return output;
            }
            catch (Exception ex)
            {
                return $"Error: {ex.Message}";
            }
        }

        private static void TryKill(Process process)
        {
            try
            {
                process.Kill(entireProcessTree: true);
                process.WaitForExit(1000);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ADB WARNING] phase=command status=pending action=kill_timeout_process reason=\"{ex.Message}\"");
            }
        }
    }
}
