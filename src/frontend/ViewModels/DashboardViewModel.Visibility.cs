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
        public string DeviceSummaryText => $"{ReadyCount}/{DeviceCount} thiết bị sẵn sàng";

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
