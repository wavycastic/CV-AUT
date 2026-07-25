using System;
using System.Collections.Generic;
using System.IO;
using SharpAdbClient;

namespace CvAut
{
    /// <summary>
    /// Chịu trách nhiệm duy nhất: liệt kê các thiết bị mà ADB server cục bộ nhìn thấy.
    /// Mọi hàm ở đây đều không bao giờ ném ngoại lệ vì được UI gọi trực tiếp.
    /// </summary>
    internal static class AdbDeviceProbe
    {
        /// <summary>
        /// Khởi động ADB server đi kèm ứng dụng nếu nó chưa chạy.
        /// </summary>
        private static void EnsureServerStarted()
        {
            var server = new AdbServer();
            try
            {
                server.StartServer(Path.Combine(AppContext.BaseDirectory, "adb", "adb.exe"), restartServerIfNewer: false);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[UI] phase=list_devices status=pending action=start_adb_server reason=\"{ex.Message}\"");
            }
        }

        /// <summary>
        /// Returns the serials of all ADB devices the local ADB server can see
        /// (e.g. "127.0.0.1:5556", "emulator-5554"). Starts the bundled ADB server
        /// first. Used by the UI device picker; never throws.
        /// </summary>
        public static IReadOnlyList<string> ListDevices()
        {
            var serials = new List<string>();
            try
            {
                EnsureServerStarted();

                IEnumerable<DeviceData>? devices = AdbClient.Instance.GetDevices();
                if (devices != null)
                {
                    foreach (DeviceData device in devices)
                    {
                        if (!string.IsNullOrWhiteSpace(device.Serial))
                        {
                            serials.Add(device.Serial);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[UI] phase=list_devices status=fail reason=\"{ex.Message}\"");
            }

            return serials;
        }

        /// <summary>
        /// Returns ADB devices with their ADB state string (e.g. "Device", "Offline",
        /// "Unauthorized"). Maps <see cref="DeviceData.State"/>. Used by scanners so the
        /// orchestrator can show unauthorized/offline devices distinctly from ready ones.
        /// Starts the bundled ADB server first. Never throws.
        /// </summary>
        public static IReadOnlyList<(string Serial, string State)> ListDevicesWithStatus()
        {
            var result = new List<(string, string)>();
            try
            {
                EnsureServerStarted();

                IEnumerable<DeviceData>? devices = AdbClient.Instance.GetDevices();
                if (devices != null)
                {
                    foreach (DeviceData device in devices)
                    {
                        if (!string.IsNullOrWhiteSpace(device.Serial))
                        {
                            result.Add((device.Serial, device.State.ToString()));
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[UI] phase=list_devices_status status=fail reason=\"{ex.Message}\"");
            }

            return result;
        }
    }
}
