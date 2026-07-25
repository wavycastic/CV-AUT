using System;
using System.Threading;

namespace CvAut
{
    /// <summary>
    /// Điều phối trình tự khởi động giả lập và game.
    /// Lớp này chỉ còn giữ thứ tự các bước; kiến thức chi tiết được uỷ quyền cho
    /// <see cref="EmulatorCatalog"/> (nhận diện vendor),
    /// <see cref="EmulatorDisplayConfigurator"/> (cấu hình hiển thị),
    /// <see cref="EmulatorProcessLauncher"/> (vòng đời tiến trình),
    /// <see cref="AdbReadinessWaiter"/> (chờ ADB online) và
    /// <see cref="ClashAppLauncher"/> (khởi chạy game).
    /// </summary>
    internal static class EmulatorBootstrapper
    {
        /// <summary>
        /// Đảm bảo giả lập đang chạy, ADB đã online và Clash of Clans đang ở foreground.
        /// </summary>
        public static bool EnsureReady(IADBHelper adb, string host, int port, string emulatorType, string emulatorPath, CancellationToken token, string emulatorInstance = "")
        {
            Console.WriteLine($"[ADB] phase=boot status=start host={host} port={port} type={emulatorType}");

            // BlueStacks multi-instance: launch flag "--instance <key>" targets the exact
            // instance; empty falls back to whatever instance the player opens by default.
            string instanceName = emulatorInstance?.Trim() ?? string.Empty;

            string? playerPath = EmulatorCatalog.ResolvePlayerPath(emulatorType, emulatorPath);

            if (playerPath != null)
            {
                Console.WriteLine($"[ADB] phase=boot status=pending action=locate_player details=\"{playerPath}\"");
            }
            else
            {
                Console.WriteLine("[ADB WARNING] phase=boot status=pending action=locate_player reason=not_found");
            }

            bool displaySettingsChanged = EmulatorDisplayConfigurator.EnsureDisplaySettings(emulatorType, playerPath, instanceName);

            if (displaySettingsChanged && EmulatorCatalog.IsRunning(emulatorType))
            {
                Console.WriteLine("[ADB] phase=boot status=pending action=restart_emulator reason=settings_changed");
                EmulatorCatalog.KillEmulatorProcesses(emulatorType);
                if (token.WaitHandle.WaitOne(1500)) return false;
            }

            if (!EmulatorCatalog.IsRunning(emulatorType))
            {
                if (!EmulatorProcessLauncher.ResetAdbAndStartEmulator(playerPath, emulatorType, $"{emulatorType} is not running", instanceName))
                {
                    return false;
                }
            }
            else
            {
                Console.WriteLine("[ADB] phase=boot status=pending action=check_emulator details=\"running\"");
            }

            if (!AdbReadinessWaiter.WaitForOnline(adb, token))
            {
                Console.WriteLine("[ADB WARNING] phase=boot status=pending action=wait_online reason=timeout_retrying");
                if (!EmulatorProcessLauncher.ResetAdbAndStartEmulator(playerPath, emulatorType, "ADB connection is not online", instanceName))
                {
                    return false;
                }

                if (!AdbReadinessWaiter.WaitForOnline(adb, token))
                {
                    Console.WriteLine("[ADB ERROR] phase=boot status=fail action=wait_online reason=timeout");
                    return false;
                }
            }

            Console.WriteLine("[ADB] phase=boot status=success details=\"connected\"");
            Console.WriteLine("[ADB] phase=check_app status=start package=\"com.supercell.clashofclans\"");

            if (!ClashAppLauncher.IsInstalled(adb))
            {
                Console.WriteLine("[ADB ERROR] phase=check_app status=fail reason=not_installed");
                return false;
            }

            if (ClashAppLauncher.IsForeground(adb))
            {
                Console.WriteLine("[ADB] phase=launch_app status=success details=\"already_foreground\"");
                return true;
            }

            if (ClashAppLauncher.LaunchAndWaitForeground(adb, token, timeoutSeconds: 12))
            {
                Console.WriteLine("[ADB] phase=launch_app status=success");
                return true;
            }

            Console.WriteLine("[ADB WARNING] phase=launch_app status=retry reason=timeout");
            if (!EmulatorProcessLauncher.ResetAdbAndStartEmulator(playerPath, emulatorType, "Clash of Clans failed to launch", instanceName))
            {
                return false;
            }

            if (!AdbReadinessWaiter.WaitForOnline(adb, token))
            {
                Console.WriteLine("[ADB ERROR] phase=boot status=fail action=reconnect reason=timeout");
                return false;
            }

            if (!ClashAppLauncher.LaunchAndWaitForeground(adb, token, timeoutSeconds: 15))
            {
                Console.WriteLine("[ADB ERROR] phase=launch_app status=fail reason=timeout");
                return false;
            }

            Console.WriteLine("[ADB] phase=launch_app status=success");
            return true;
        }
    }
}
