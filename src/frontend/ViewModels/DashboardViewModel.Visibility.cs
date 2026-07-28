using System.Linq;
using CvAut.Models;

namespace CvAut.ViewModels
{
    /// <summary>
    /// Computed pane visibility and status text for the dashboard page. These are all derived
    /// from <see cref="DashboardViewModel.State"/>, the active device and the attached device
    /// collection; the observable fields that raise change notifications for them carry the
    /// matching <c>[NotifyPropertyChangedFor]</c> attributes in <c>DashboardViewModel.cs</c>.
    /// </summary>
    public partial class DashboardViewModel
    {
        public int RunnableDeviceCount => Devices?.Count(device => device.DeviceCanStart) ?? ReadyCount;
        public int SelectedDeviceCount => Devices?.Count(device => device.IsSelected) ?? 0;
        public int RunnableSelectedCount => Devices?.Count(device => device.IsSelected && device.CanStart) ?? 0;
        public int StoppableSelectedCount => Devices?.Count(device => device.IsSelected && device.CanStop) ?? 0;

        /// <summary>
        /// Header caption. Deliberately reports detection only: the runnable/selected counts are
        /// owned by <see cref="FleetSelectionText"/> right below it, and repeating them here made
        /// two adjacent lines say the same thing.
        /// </summary>
        public string DeviceSummaryText => DeviceCount switch
        {
            0 => "Chưa phát hiện instance nào",
            1 => "Đã phát hiện 1 instance",
            _ => $"Đã phát hiện {DeviceCount} instance",
        };

        public string FleetSelectionText => SelectedDeviceCount switch
        {
            0 => "Chưa chọn thiết bị",
            _ => $"{SelectedDeviceCount} đã chọn • {RunnableSelectedCount} có thể chạy",
        };

        public string FleetStartHint => SelectedDeviceCount switch
        {
            0 => "Chọn ít nhất một thiết bị để khởi chạy.",
            _ when RunnableSelectedCount == 0 => "Các thiết bị đã chọn chưa thể khởi chạy.",
            _ => $"Khởi chạy {RunnableSelectedCount} thiết bị đã chọn.",
        };

        public string FleetStopHint => StoppableSelectedCount == 0
            ? "Không có thiết bị đã chọn nào đang chạy."
            : $"Dừng {StoppableSelectedCount} thiết bị đã chọn.";

        public bool IsIdle => State == DashboardDeviceState.Idle;
        public bool IsDetecting => State == DashboardDeviceState.Detecting;
        public bool HasNoDevices => State == DashboardDeviceState.NoDevices;

        public bool ShowDeviceList =>
            State == DashboardDeviceState.DeviceSelected && ActiveDevice is null && DeviceCount > 0;

        public bool ShowActivePanel =>
            !IsGridMode &&
            ActiveDevice is not null &&
            State is DashboardDeviceState.DeviceSelected or DashboardDeviceState.Running
                or DashboardDeviceState.Paused or DashboardDeviceState.Error;

        public bool ShowSelectionPane => !ShowActivePanel && !ShowGridPane;

        public bool ShowGridPane => IsGridMode && State != DashboardDeviceState.ConfiguringDevice && (Devices?.Count ?? 0) > 0;
        public bool ShowConfiguringPanel => State == DashboardDeviceState.ConfiguringDevice;
        public bool ShowEmptyState => ShowEmptyNotDetected || ShowEmptyNoDevices;
        public bool ShowEmptyNotDetected => State == DashboardDeviceState.Idle;
        public bool ShowEmptyNoDevices => State == DashboardDeviceState.NoDevices;

        public bool IsRunning => State == DashboardDeviceState.Running ||
            (Devices != null && Devices.Any(d => d.IsSelected && (d.Status == BotStatus.Running || d.Status == BotStatus.Starting)));

        public bool IsStopped => _hasBeenStopped && !IsRunning;
        public bool IsPaused => State == DashboardDeviceState.Paused;
        public bool HasError => State == DashboardDeviceState.Error;

        public string StatusText => State switch
        {
            DashboardDeviceState.Idle => "Đang quét thiết bị...",
            DashboardDeviceState.Detecting => "Đang tìm kiếm thiết bị...",
            DashboardDeviceState.NoDevices => string.IsNullOrEmpty(DetectDetail) ? "Không tìm thấy thiết bị." : "Không tìm thấy thiết bị — " + DetectDetail,
            DashboardDeviceState.DeviceSelected => ActiveDevice is null
                ? string.Empty
                : "Đã chọn thiết bị — sẵn sàng chạy.",
            DashboardDeviceState.Running => "Đang chạy.",
            DashboardDeviceState.Paused => "Đang tạm dừng.",
            DashboardDeviceState.Error => "Lỗi — kiểm tra nhật ký.",
            DashboardDeviceState.ConfiguringDevice => "Đang cấu hình thiết bị...",
            _ => string.Empty,
        };
    }
}
