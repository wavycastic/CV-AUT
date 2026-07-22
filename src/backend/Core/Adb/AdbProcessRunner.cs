using System;
using System.Diagnostics;
using System.IO;

namespace CvAut.Adb
{
    /// <summary>
    /// Chuyên thực thi lệnh CLI ADB (`adb.exe`), quản lý tiến trình hệ thống và khởi chạy ADB server.
    /// </summary>
    internal class AdbProcessRunner
    {
        private readonly string _adbExePath = Path.Combine(AppContext.BaseDirectory, "adb", "adb.exe");

        public string AdbExePath => _adbExePath;

        public string RunAdbCommand(string deviceAddress, string arguments)
        {
            string targetAddress = deviceAddress;
            string fullArgs = string.IsNullOrWhiteSpace(targetAddress) ? arguments : $"-s {targetAddress} {arguments}";
            return RunRawAdbCommand(fullArgs);
        }

        public string RunRawAdbCommand(string arguments)
        {
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = _adbExePath,
                    Arguments = arguments,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                using var proc = Process.Start(psi);
                if (proc == null) return "Error: Failed to start process";

                string output = proc.StandardOutput.ReadToEnd();
                string error = proc.StandardError.ReadToEnd();
                proc.WaitForExit(5000);

                if (!string.IsNullOrWhiteSpace(error) && string.IsNullOrWhiteSpace(output))
                {
                    return $"Error: {error.Trim()}";
                }

                return output.Trim();
            }
            catch (Exception ex)
            {
                return $"Error: {ex.Message}";
            }
        }
    }
}
