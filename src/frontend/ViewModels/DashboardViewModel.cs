using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CvAut.Models;
using CvAut.Services;

namespace CvAut.ViewModels
{
    /// <summary>
    /// Dashboard page view model. The active device is pushed here by the shell host
    /// so the page stays self-contained for binding.
    /// </summary>
    public partial class DashboardViewModel : ViewModelBase
    {
        [ObservableProperty]
        private string _title = "Thiết bị";

        [NotifyPropertyChangedFor(nameof(ShowActivePanel))]
        [NotifyPropertyChangedFor(nameof(ShowSelectionPane))]
        [NotifyPropertyChangedFor(nameof(ShowDeviceList))]
        [NotifyPropertyChangedFor(nameof(ShowEmptyState))]
        [ObservableProperty]
        private DeviceViewModel? _activeDevice;

        [NotifyPropertyChangedFor(nameof(ShowConfiguringPanel))]
        [NotifyPropertyChangedFor(nameof(ShowSelectionPane))]
        [ObservableProperty]
        private DeviceViewModel? _selectedDeviceForConfig;

        [ObservableProperty]
        private string _selectedEmulatorFilter = "BlueStacks";

        [NotifyPropertyChangedFor(nameof(ShowGridPane))]
        [NotifyPropertyChangedFor(nameof(ShowActivePanel))]
        [NotifyPropertyChangedFor(nameof(ShowSelectionPane))]
        [ObservableProperty]
        private bool _isGridMode;

        public ObservableCollection<string> EmulatorFilters { get; } = new()
        {
            "Tất cả", "BlueStacks", "LDPlayer", "MEmu"
        };

        private IAsyncRelayCommand? _detectDevicesCommand;

        public IAsyncRelayCommand? DetectDevicesCommand
        {
            get => _detectDevicesCommand;
            set
            {
                if (SetProperty(ref _detectDevicesCommand, value))
                {
                    if (value is not null && value.CanExecute(null))
                    {
                        value.Execute(null);
                    }
                }
            }
        }

        partial void OnSelectedEmulatorFilterChanged(string value)
        {
            _appPreferences.SaveSelectedEmulatorFilter(value);
            if (_detectDevicesCommand is not null && _detectDevicesCommand.CanExecute(null))
            {
                _detectDevicesCommand.Execute(null);
            }
        }

        [NotifyPropertyChangedFor(nameof(IsIdle))]
        [NotifyPropertyChangedFor(nameof(IsDetecting))]
        [NotifyPropertyChangedFor(nameof(HasNoDevices))]
        [NotifyPropertyChangedFor(nameof(ShowDeviceList))]
        [NotifyPropertyChangedFor(nameof(ShowActivePanel))]
        [NotifyPropertyChangedFor(nameof(ShowGridPane))]
        [NotifyPropertyChangedFor(nameof(ShowSelectionPane))]
        [NotifyPropertyChangedFor(nameof(ShowConfiguringPanel))]
        [NotifyPropertyChangedFor(nameof(ShowEmptyState))]
        [NotifyPropertyChangedFor(nameof(ShowEmptyNotDetected))]
        [NotifyPropertyChangedFor(nameof(ShowEmptyNoDevices))]
        [NotifyPropertyChangedFor(nameof(IsRunning))]
        [NotifyPropertyChangedFor(nameof(IsStopped))]
        [NotifyPropertyChangedFor(nameof(IsPaused))]
        [NotifyPropertyChangedFor(nameof(HasError))]
        [NotifyPropertyChangedFor(nameof(StatusText))]
        [ObservableProperty]
        private DashboardDeviceState _state = DashboardDeviceState.Idle;

        [NotifyPropertyChangedFor(nameof(DeviceSummaryText))]
        [ObservableProperty]
        private int _deviceCount;

        [NotifyPropertyChangedFor(nameof(DeviceSummaryText))]
        [ObservableProperty]
        private int _readyCount;

        public string DeviceSummaryText => $"{ReadyCount} sẵn sàng trên {DeviceCount} phát hiện";

        public ObservableCollection<DeviceViewModel>? Devices { get; private set; }

        [ObservableProperty]
        private IRelayCommand? _selectDeviceCommand;

        [ObservableProperty]
        private IRelayCommand<DeviceViewModel>? _showDeviceLogsCommand;

        public event Action<string>? CopyDeviceLogsRequested;

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

        private bool _hasBeenStopped;

        public bool IsRunning => State == DashboardDeviceState.Running ||
            (Devices != null && Devices.Any(d => d.IsSelected && (d.Status == BotStatus.Running || d.Status == BotStatus.Starting)));

        public bool IsStopped => _hasBeenStopped && !IsRunning;
        public bool IsPaused => State == DashboardDeviceState.Paused;
        public bool HasError => State == DashboardDeviceState.Error;

        [NotifyPropertyChangedFor(nameof(StatusText))]
        [ObservableProperty]
        private string _detectDetail = string.Empty;

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

        private readonly SettingsViewModel _settingsViewModel;
        private readonly IConfigStore _configStore;
        private readonly IAppPreferences _appPreferences;
        private DeviceViewModel? _trackedDevice;

        public SettingsViewModel SettingsViewModel => _settingsViewModel;

        public DashboardViewModel(
            SettingsViewModel settingsViewModel,
            IConfigStore configStore,
            IAppPreferences appPreferences)
        {
            _settingsViewModel = settingsViewModel;
            _settingsViewModel.IsInstanceMode = true;
            _settingsViewModel.InstanceSaveRequested += () => SaveDeviceConfigCommand.Execute(null);
            _settingsViewModel.InstanceCancelRequested += () => CancelDeviceConfigCommand.Execute(null);
            _configStore = configStore;
            _appPreferences = appPreferences;
            LoadPreferences();
        }

        public DashboardViewModel(SettingsViewModel settingsViewModel, IConfigStore configStore)
            : this(settingsViewModel, configStore, new JsonAppPreferences())
        {
        }

        public DashboardViewModel()
            : this(new SettingsViewModel(), new ConfigStore(), new JsonAppPreferences())
        {
        }

        partial void OnSelectedDeviceForConfigChanged(DeviceViewModel? value)
        {
            if (_trackedDevice is not null)
            {
                _trackedDevice.PropertyChanged -= OnTrackedDevicePropertyChanged;
            }

            _trackedDevice = value;

            if (_trackedDevice is not null)
            {
                _trackedDevice.PropertyChanged += OnTrackedDevicePropertyChanged;
            }
        }

        private void OnTrackedDevicePropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(DeviceViewModel.SelectedPlayMode) && _trackedDevice is not null)
            {
                _settingsViewModel.SelectedPlayMode = _trackedDevice.SelectedPlayMode;
            }
        }

        [RelayCommand]
        private void ConfigureDevice(DeviceViewModel device)
        {
            SelectedDeviceForConfig = device;
            string profileName = device.Device.ProfileKey;

            if (!_configStore.Profiles.Any(p => string.Equals(p.Name, profileName, StringComparison.OrdinalIgnoreCase)))
            {
                _configStore.LoadProfile("Default");
                var defaultTemplate = _configStore.LoadActiveConfig();
                _configStore.SaveProfileAs(profileName, defaultTemplate);
            }

            _configStore.LoadProfile(profileName);
            _settingsViewModel.LoadSelectedProfileDirectly(profileName, device.SelectedPlayMode);
            State = DashboardDeviceState.ConfiguringDevice;
        }

        [RelayCommand]
        private void SaveDeviceConfig()
        {
            if (SelectedDeviceForConfig is not null)
            {
                _settingsViewModel.UpdateProfileDirectly();
                SelectedDeviceForConfig.SelectedPlayMode = _settingsViewModel.SelectedPlayMode;
            }
            State = DashboardDeviceState.DeviceSelected;
            SelectedDeviceForConfig = null;
        }

        [RelayCommand]
        private void CancelDeviceConfig()
        {
            State = DashboardDeviceState.DeviceSelected;
            SelectedDeviceForConfig = null;
        }

        [RelayCommand]
        private void CopyDeviceLogs(DeviceViewModel device)
        {
            CopyDeviceLogsRequested?.Invoke(BuildDeviceLogText(device));
        }

        [RelayCommand]
        private void DeselectDevice()
        {
            ActiveDevice = null;
            State = DashboardDeviceState.DeviceSelected;
        }

        private void LoadPreferences()
        {
            string filter = _appPreferences.LoadSelectedEmulatorFilter();
            if (EmulatorFilters.Contains(filter))
            {
                SelectedEmulatorFilter = filter;
            }
        }

        public void AttachDevices(ObservableCollection<DeviceViewModel> devices)
        {
            if (Devices != null)
            {
                Devices.CollectionChanged -= OnDevicesCollectionChanged;
                foreach (var dev in Devices)
                {
                    dev.PropertyChanged -= OnDevicePropertyChanged;
                }
            }

            Devices = devices;

            if (Devices != null)
            {
                Devices.CollectionChanged += OnDevicesCollectionChanged;
                foreach (var dev in Devices)
                {
                    dev.PropertyChanged += OnDevicePropertyChanged;
                }
            }

            OnPropertyChanged(nameof(IsRunning));
            OnPropertyChanged(nameof(IsStopped));
        }

        private void OnDevicesCollectionChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
        {
            if (e.OldItems != null)
            {
                foreach (DeviceViewModel item in e.OldItems)
                {
                    item.PropertyChanged -= OnDevicePropertyChanged;
                }
            }
            if (e.NewItems != null)
            {
                foreach (DeviceViewModel item in e.NewItems)
                {
                    item.PropertyChanged += OnDevicePropertyChanged;
                }
            }

            NotifyDevicesChanged();
        }

        private void OnDevicePropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(DeviceViewModel.Status) || e.PropertyName == nameof(DeviceViewModel.IsSelected))
            {
                if (sender is DeviceViewModel dev)
                {
                    if (dev.Status is BotStatus.Running or BotStatus.Starting)
                    {
                        _hasBeenStopped = false;
                    }
                    else if (dev.Status is BotStatus.Stopped or BotStatus.Stopping)
                    {
                        _hasBeenStopped = true;
                    }
                }

                OnPropertyChanged(nameof(IsRunning));
                OnPropertyChanged(nameof(IsStopped));
            }
        }

        public void NotifyDevicesChanged()
        {
            OnPropertyChanged(nameof(ShowGridPane));
            OnPropertyChanged(nameof(IsRunning));
            OnPropertyChanged(nameof(IsStopped));
        }

        [RelayCommand]
        private void StartAll()
        {
            if (Devices is null)
            {
                return;
            }

            _hasBeenStopped = false;

            foreach (var device in Devices)
            {
                if (device.IsSelected && device.CanStart)
                {
                    device.StartCommand.Execute(null);
                }
            }

            OnPropertyChanged(nameof(IsRunning));
            OnPropertyChanged(nameof(IsStopped));
        }

        [RelayCommand]
        private void StopAll()
        {
            if (Devices is null)
            {
                return;
            }

            _hasBeenStopped = true;

            foreach (var device in Devices)
            {
                if (device.IsSelected && device.CanStop)
                {
                    device.StopCommand.Execute(null);
                }
            }

            OnPropertyChanged(nameof(IsRunning));
            OnPropertyChanged(nameof(IsStopped));
        }

        private static string BuildDeviceLogText(DeviceViewModel device)
        {
            var builder = new StringBuilder();
            foreach (LogEntry entry in device.Logs)
            {
                builder.AppendLine($"[{entry.TimeText}] {entry.LevelText} {entry.Message}");
            }

            return builder.ToString().TrimEnd();
        }
    }
}
