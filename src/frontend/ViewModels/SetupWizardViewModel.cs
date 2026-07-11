using System;
using System.Collections.ObjectModel;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CvAut.Models;
using CvAut.Services.Emulators;

namespace CvAut.ViewModels
{
    public partial class SetupWizardViewModel : ViewModelBase
    {
        private readonly IEmulatorDiscovery _discovery;
        private readonly IConfigStore _configStore;

        [ObservableProperty] private string _title = "Thiết lập";
        [ObservableProperty]
        [NotifyCanExecuteChangedFor(nameof(VerifyDisplayCommand))]
        [NotifyCanExecuteChangedFor(nameof(RunTrialCommand))]
        private Device? _selectedDevice;
        [ObservableProperty] private string _status = "Chọn giả lập, xác minh 1600x900 (BlueStacks 300dpi / MEmu 240dpi), sau đó chạy thử nghiệm.";
        [ObservableProperty]
        [NotifyCanExecuteChangedFor(nameof(RunTrialCommand))]
        private bool _resolutionOk;
        [ObservableProperty]
        [NotifyCanExecuteChangedFor(nameof(RunTrialCommand))]
        private bool _dpiOk;
        [ObservableProperty] private string _displaySummary = "Chưa kiểm tra";
        [ObservableProperty]
        [NotifyCanExecuteChangedFor(nameof(DetectEmulatorsCommand))]
        [NotifyCanExecuteChangedFor(nameof(VerifyDisplayCommand))]
        [NotifyCanExecuteChangedFor(nameof(RunTrialCommand))]
        private bool _isTrialRunning;

        public ObservableCollection<Device> Devices { get; } = new();

        private bool CanDetectEmulators() => !IsTrialRunning;
        private bool CanVerifyDisplay() => !IsTrialRunning && SelectedDevice is not null;
        private bool CanRunTrial() => !IsTrialRunning && SelectedDevice is not null && ResolutionOk && DpiOk;

        public SetupWizardViewModel(IEmulatorDiscovery discovery, IConfigStore configStore)
        {
            _discovery = discovery;
            _configStore = configStore;
        }

        public SetupWizardViewModel() : this(new AdbEmulatorDiscovery(), new ConfigStore())
        {
        }

        [RelayCommand(CanExecute = nameof(CanDetectEmulators))]
        private async Task DetectEmulatorsAsync()
        {
            Devices.Clear();
            foreach (Device device in await _discovery.DiscoverAsync())
            {
                Devices.Add(device);
            }

            SelectedDevice = Devices.Count > 0 ? Devices[0] : null;
            Status = Devices.Count == 0 ? "Không tìm thấy thiết bị ADB. Hãy bật ADB trên giả lập và chạy lệnh adb connect." : "Tìm thấy thiết bị ADB. Bấm 'Xác minh màn hình'.";
            if (SelectedDevice is not null)
            {
                await VerifyDisplayAsync();
            }
        }

        [RelayCommand(CanExecute = nameof(CanVerifyDisplay))]
        private async Task VerifyDisplayAsync()
        {
            if (SelectedDevice is null)
            {
                Status = "Chọn giả lập trước khi kiểm tra màn hình.";
                return;
            }

            EmulatorDisplayInfo info = await _discovery.GetDisplayInfoAsync(SelectedDevice);
            ResolutionOk = info.ResolutionOk;
            DpiOk = info.DpiOk;
            int expectedDpi = !string.IsNullOrWhiteSpace(SelectedDevice.EmulatorType)
                && SelectedDevice.EmulatorType.Equals("BlueStacks", StringComparison.OrdinalIgnoreCase) ? 300 : 240;
            DisplaySummary = $"{info.Width}x{info.Height}, {info.DensityDpi}dpi ({info.Raw})";
            Status = ResolutionOk && DpiOk
                ? $"Màn hình ĐẠT: 1600x900 / {expectedDpi}dpi."
                : $"Kích thước không khớp. Hãy đặt giả lập ở độ phân giải 1600x900 / {expectedDpi}dpi trước khi Bắt đầu.";
        }

        [RelayCommand]
        private void MarkDisplayOk()
        {
            ResolutionOk = true;
            DpiOk = true;
            Status = "Màn hình đã được đánh dấu Đạt.";
        }

        /// <summary>
        /// When the user picks a different device, invalidate the cached display check so a
        /// trial never runs against display flags captured for a different emulator (item 9:
        /// trial target must always be the currently selected device, including its display).
        /// </summary>
        partial void OnSelectedDeviceChanged(Device? value)
        {
            if (value is null)
            {
                return;
            }

            ResolutionOk = false;
            DpiOk = false;
            DisplaySummary = "Chưa kiểm tra";
        }

        [RelayCommand(CanExecute = nameof(CanRunTrial))]
        private async Task RunTrialAsync()
        {
            if (SelectedDevice is null)
            {
                Status = "Chọn giả lập trước khi chạy thử nghiệm.";
                return;
            }

            if (!ResolutionOk || !DpiOk)
            {
                Status = "Xác minh màn hình 1600x900 đúng DPI trước khi chạy thử nghiệm.";
                return;
            }

            string configPath = _configStore.ResolveActiveConfigPath();
            ApplySelectedDeviceToActiveConfig();
            IsTrialRunning = true;
            Status = "Đang chạy thử nghiệm: một chu kỳ tự động.";
            try
            {
                using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(30));
                await Task.Run(() => BackendDiagnostics.RunWorkflowTemplate(configPath, 1, cts.Token), cts.Token);
                Status = "Thử nghiệm hoàn tất. Thiết bị đã sẵn sàng để Bắt đầu từ Bảng điều khiển.";
            }
            catch (OperationCanceledException)
            {
                Status = "Thử nghiệm quá thời gian 30 phút.";
            }
            catch (Exception ex)
            {
                Status = "Thử nghiệm thất bại: " + ex.Message;
            }
            finally
            {
                IsTrialRunning = false;
            }
        }

        private void ApplySelectedDeviceToActiveConfig()
        {
            if (SelectedDevice is null)
            {
                return;
            }

            JsonObject config = _configStore.LoadActiveConfig();
            JsonObject device = ConfigStore.GetOrCreateObject(config, "device_connection");
            device["host"] = SelectedDevice.Host;
            device["port"] = SelectedDevice.Port;
            // Installed-emulator discovery: persist type/path so a trial run can auto-launch
            // the selected emulator via EmulatorBootstrapper even when it is not running.
            if (!string.IsNullOrWhiteSpace(SelectedDevice.EmulatorType))
            {
                device["emulator_type"] = SelectedDevice.EmulatorType;
            }
            if (!string.IsNullOrWhiteSpace(SelectedDevice.EmulatorPath))
            {
                device["emulator_path"] = SelectedDevice.EmulatorPath;
            }
            if (!string.IsNullOrWhiteSpace(SelectedDevice.EmulatorInstance))
            {
                device["emulator_instance"] = SelectedDevice.EmulatorInstance;
            }
            _configStore.SaveActiveConfig(config);
        }
    }
}
