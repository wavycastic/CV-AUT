using System;
using System.Diagnostics;
using System.IO;

namespace CvAut
{
    /// <summary>
    /// Chịu trách nhiệm duy nhất: ép giả lập về độ phân giải 1600x900 và DPI 300,
    /// điều kiện bắt buộc để các vùng ROI cố định trong AutomationRoiConstants còn đúng.
    /// Mỗi vendor có một cơ chế riêng: BlueStacks sửa bluestacks.conf, LDPlayer dùng
    /// ldconsole, MEmu dùng memuc.
    /// </summary>
    internal static class EmulatorDisplayConfigurator
    {
        private const string DefaultInstancePrefix = "bst.instance.Pie64.";

        /// <summary>
        /// Áp dụng cấu hình hiển thị cho giả lập.
        /// </summary>
        /// <returns>True nếu cấu hình thực sự bị thay đổi, khi đó giả lập cần khởi động lại.</returns>
        public static bool EnsureDisplaySettings(string emulatorType, string? playerPath, string instanceName)
        {
            if (string.Equals(emulatorType, "BlueStacks", StringComparison.OrdinalIgnoreCase))
            {
                return EnsureBlueStacks(instanceName);
            }

            if (string.Equals(emulatorType, "LDPlayer", StringComparison.OrdinalIgnoreCase))
            {
                return EnsureLdPlayer(playerPath, instanceName);
            }

            if (string.Equals(emulatorType, "MEmu", StringComparison.OrdinalIgnoreCase))
            {
                return EnsureMemu(playerPath, instanceName);
            }

            return false;
        }

        private static bool EnsureBlueStacks(string instanceName)
        {
            string confPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                @"BlueStacks_nxt\bluestacks.conf");

            // Configure the exact selected instance; fall back to the historical
            // Pie64 default only when no instance key was resolved from discovery.
            string instancePrefix = string.IsNullOrWhiteSpace(instanceName)
                ? DefaultInstancePrefix
                : $"bst.instance.{instanceName}.";

            if (!File.Exists(confPath))
            {
                Console.WriteLine($"[ADB WARNING] phase=boot status=pending action=locate_conf reason=not_found details=\"{confPath}\"");
                return false;
            }

            Console.WriteLine($"[ADB] phase=boot status=pending action=locate_conf details=\"{confPath}\"");
            Console.WriteLine($"[ADB] phase=boot status=pending action=set_instance prefix=\"{instancePrefix}\"");
            return EnsureBlueStacksDisplaySettings(confPath, instancePrefix);
        }

        /// <summary>
        /// Đảm bảo BlueStacks dùng đúng độ phân giải 1600x900 và DPI 300 cho các vùng ROI cố định.
        /// </summary>
        private static bool EnsureBlueStacksDisplaySettings(string confPath, string instancePrefix)
        {
            if (!File.Exists(confPath))
            {
                Console.WriteLine("[ADB WARNING] phase=configure status=skip reason=missing_conf");
                return false;
            }

            try
            {
                string[] lines = File.ReadAllLines(confPath);
                bool changed = false;
                changed |= SetConfigValue(lines, $"{instancePrefix}fb_width", "1600");
                changed |= SetConfigValue(lines, $"{instancePrefix}fb_height", "900");
                changed |= SetConfigValue(lines, $"{instancePrefix}dpi", "300");
                changed |= SetConfigValue(lines, $"{instancePrefix}custom_resolution_selected", "1");

                if (!changed)
                {
                    Console.WriteLine("[ADB] phase=configure status=skip reason=already_set");
                    return false;
                }

                File.Copy(confPath, confPath + ".bak", overwrite: true);
                File.WriteAllLines(confPath, lines);
                Console.WriteLine("[ADB] phase=configure status=success");
                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ADB ERROR] phase=configure status=fail reason=\"{ex.Message}\"");
                return false;
            }
        }

        /// <summary>
        /// Cập nhật một dòng key="value" trong bluestacks.conf nếu key tồn tại.
        /// </summary>
        private static bool SetConfigValue(string[] lines, string key, string value)
        {
            string prefix = key + "=";
            for (int i = 0; i < lines.Length; i++)
            {
                if (!lines[i].StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                string desiredLine = $"{key}=\"{value}\"";
                if (string.Equals(lines[i], desiredLine, StringComparison.Ordinal))
                {
                    return false;
                }

                lines[i] = desiredLine;
                return true;
            }

            return false;
        }

        private static bool EnsureLdPlayer(string? playerPath, string instanceName)
        {
            string? installDir = string.IsNullOrWhiteSpace(playerPath) ? null : Path.GetDirectoryName(playerPath);
            string? consolePath = installDir is null ? null : Path.Combine(installDir, "ldconsole.exe");
            if (consolePath is null || !File.Exists(consolePath))
            {
                consolePath = installDir is null ? null : Path.Combine(installDir, "dnconsole.exe");
            }

            if (consolePath is null || !File.Exists(consolePath))
            {
                Console.WriteLine("[ADB WARNING] phase=configure status=skip type=LDPlayer reason=missing_console");
                return false;
            }

            string selector = int.TryParse(instanceName, out int index)
                ? $"--index {index}"
                : "--index 0";
            return RunConfigCommand(consolePath, $"modify {selector} --resolution 1600,900,300", "LDPlayer");
        }

        private static bool EnsureMemu(string? playerPath, string instanceName)
        {
            string? installDir = string.IsNullOrWhiteSpace(playerPath) ? null : Path.GetDirectoryName(playerPath);
            string? memucPath = installDir is null ? null : Path.Combine(installDir, "memuc.exe");
            if (memucPath is null || !File.Exists(memucPath))
            {
                Console.WriteLine("[ADB WARNING] phase=configure status=skip type=MEmu reason=missing_memuc");
                return false;
            }

            string selector = int.TryParse(instanceName, out int index)
                ? $"-i {index}"
                : "-i 0";
            return RunConfigCommand(memucPath, $"setconfigex {selector} custom_resolution \"1600 900 300\"", "MEmu");
        }

        /// <summary>
        /// Chạy công cụ dòng lệnh của vendor để đặt cấu hình hiển thị, có giới hạn thời gian chờ.
        /// </summary>
        private static bool RunConfigCommand(string fileName, string arguments, string emulatorType)
        {
            try
            {
                using var process = Process.Start(new ProcessStartInfo
                {
                    FileName = fileName,
                    Arguments = arguments,
                    CreateNoWindow = true,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                });

                if (process is null)
                {
                    Console.WriteLine($"[ADB WARNING] phase=configure status=skip type={emulatorType} reason=start_failed");
                    return false;
                }

                if (!process.WaitForExit(5000))
                {
                    try { process.Kill(entireProcessTree: true); } catch { /* best effort */ }
                    Console.WriteLine($"[ADB WARNING] phase=configure status=skip type={emulatorType} reason=timeout");
                    return false;
                }

                string stderr = process.StandardError.ReadToEnd().Trim();
                if (process.ExitCode != 0)
                {
                    Console.WriteLine($"[ADB WARNING] phase=configure status=skip type={emulatorType} exit={process.ExitCode} reason=command_failed details=\"{stderr}\"");
                    return false;
                }

                Console.WriteLine($"[ADB] phase=configure status=success type={emulatorType} resolution=1600x900 dpi=300");
                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ADB WARNING] phase=configure status=skip type={emulatorType} reason=\"{ex.Message}\"");
                return false;
            }
        }
    }
}
