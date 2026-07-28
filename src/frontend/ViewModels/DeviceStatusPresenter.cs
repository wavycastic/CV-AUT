using System;
using CvAut.Models;

namespace CvAut.ViewModels
{
    /// <summary>
    /// Display mapping for a device: status wording, badge colours and the list subtitle.
    /// Kept apart from <see cref="DeviceViewModel"/> so the session lifecycle logic is not
    /// interleaved with presentation tables. Pure functions, no state.
    /// </summary>
    internal static class DeviceStatusPresenter
    {
        /// <summary>
        /// Human-readable explanation of the device's <see cref="DeviceStatus"/> for the UI,
        /// so a closed-but-installed emulator (or an unauthorized/offline one) shows a clear
        /// hint instead of a bare enum name.
        /// </summary>
        internal static string DeviceStatusText(DeviceStatus status, bool canAutoStart) => status switch
        {
            DeviceStatus.Ready => "Sẵn sàng — sẽ dùng instance đang mở",
            DeviceStatus.Installed => "Giả lập chưa chạy — Khởi chạy sẽ tự bật instance này",
            DeviceStatus.Unauthorized => "ADB chưa được ủy quyền — hãy chấp nhận yêu cầu gỡ lỗi USB trên giả lập",
            DeviceStatus.Offline when canAutoStart => "Giả lập hoặc ADB đang ngoại tuyến — Khởi chạy sẽ tự bật lại instance này",
            DeviceStatus.Offline => "ADB ngoại tuyến — không tìm thấy trình giả lập để tự khởi động",
            _ => "Không xác định",
        };

        internal static string DisplayStatus(BotStatus status) => status switch
        {
            BotStatus.Idle => "Rảnh",
            BotStatus.Starting => "Đang khởi động",
            BotStatus.Running => "Đang chạy",
            BotStatus.Paused => "Đang tạm dừng",
            BotStatus.Stopping => "Đang dừng",
            BotStatus.Stopped => "Đã dừng",
            BotStatus.Error => "Lỗi",
            _ => status.ToString()
        };

        internal static string DisplayStatusColor(BotStatus status) => status switch
        {
            BotStatus.Running => "LimeGreen",
            BotStatus.Paused => "Orange",
            BotStatus.Error => "Red",
            BotStatus.Starting => "Cyan",
            BotStatus.Stopping => "LightGray",
            _ => "Gray"
        };

        internal static string StatusBadgeText(BotStatus status, DeviceStatus deviceStatus, bool canAutoStart) => status switch
        {
            BotStatus.Running => "Đang chạy",
            BotStatus.Starting => "Đang bật",
            BotStatus.Paused => "Tạm dừng",
            BotStatus.Stopping => "Đang tắt",
            BotStatus.Error => "Lỗi",
            BotStatus.Stopped => "Đã dừng",
            _ => deviceStatus switch
            {
                DeviceStatus.Ready => "Sẵn sàng",
                DeviceStatus.Installed => "Có thể bật",
                DeviceStatus.Offline when canAutoStart => "Có thể bật",
                DeviceStatus.Unauthorized => "Chưa ủy quyền",
                _ => "Ngoại tuyến",
            }
        };

        internal static string StatusBadgeColor(BotStatus status, DeviceStatus deviceStatus, bool canAutoStart) => status switch
        {
            BotStatus.Running => "#4caf50",
            BotStatus.Starting => "#2196f3",
            BotStatus.Paused => "#ff9800",
            BotStatus.Error => "#f44336",
            BotStatus.Stopping => "#9e9e9e",
            _ when deviceStatus == DeviceStatus.Ready => "#4caf50",
            _ when deviceStatus == DeviceStatus.Installed || (deviceStatus == DeviceStatus.Offline && canAutoStart) => "#eab308",
            _ when deviceStatus == DeviceStatus.Unauthorized => "#f97316",
            _ => "#757575"
        };

        /// <summary>
        /// Clean subtitle string for device list items. Prevents trailing dots and duplicate vendor names.
        /// </summary>
        internal static string SubTitle(string? source, string? serial, int port, string displayName)
        {
            source ??= string.Empty;
            serial ??= string.Empty;

            string endpoint = !string.IsNullOrWhiteSpace(serial)
                ? serial
                : (port > 0 ? $"Port {port}" : string.Empty);

            if (string.IsNullOrWhiteSpace(endpoint))
            {
                return !string.IsNullOrWhiteSpace(source) ? source : "ADB Cục bộ";
            }

            if (!string.IsNullOrWhiteSpace(source) &&
                !source.Equals(displayName, StringComparison.OrdinalIgnoreCase) &&
                !source.Equals(endpoint, StringComparison.OrdinalIgnoreCase))
            {
                return $"{source} • {endpoint}";
            }

            return endpoint;
        }
    }
}
