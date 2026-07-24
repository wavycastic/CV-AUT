using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json.Nodes;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CvAut.Models;

namespace CvAut.ViewModels
{
    /// <summary>
    /// Dashboard page view model. In single mode it renders the active device's
    /// <see cref="DeviceViewModel"/> via <c>DevicePanelView</c>; grid mode (Phase 3) renders the
    /// whole <see cref="Devices"/> collection. The active device is pushed here by the shell host
    /// (<c>MainWindowViewModel</c>) so the page stays self-contained for binding.
    ///
    /// Item 10: the page's coarse UI state lives in a single <see cref="State"/> value
    /// (<see cref="DashboardDeviceState"/>) instead of scattered flags. The view binds the
    /// computed visibility booleans below, each derived from <see cref="State"/> (+ the active
    /// device pointer where the panel/list split is needed), so the page never goes blank
    /// mid-detect or after navigation. <see cref="DeviceCount"/>/<see cref="ReadyCount"/> are
    /// kept only for the "X ready of Y detected" summary text — not for visibility branching.
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

        /// <summary>Grid mode renders every detected device as a compact <c>DevicePanelView</c> tile;
        /// single mode renders only the active device. Same view, different container (roadmap Phase 3).</summary>
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
            SaveSettings();
            if (_detectDevicesCommand is not null && _detectDevicesCommand.CanExecute(null))
            {
                _detectDevicesCommand.Execute(null);
            }
        }

        /// <summary>
        /// Single source of truth for the page's coarse UI state (item 10). Pushed by the shell
        /// host at Detect start/end, on selection, and when the active device's bot status
        /// transitions. Drives every computed visibility boolean below via
        /// <see cref="NotifyStateChanged"/>.
        /// </summary>
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

        /// <summary>Total devices returned by the last Detect pass (any status) — summary text only.</summary>
        [NotifyPropertyChangedFor(nameof(DeviceSummaryText))]
        [ObservableProperty]
        private int _deviceCount;

        /// <summary>Ready devices from the last Detect pass — summary text only.</summary>
        [NotifyPropertyChangedFor(nameof(DeviceSummaryText))]
        [ObservableProperty]
        private int _readyCount;

        /// <summary>
        /// Selection-pane summary line. Built in the VM so the view binds a single string and
        /// both counters stay in sync — the old XAML StringFormat only fed one argument, so the
        /// "{1} phát hiện" slot rendered wrong.
        /// </summary>
        public string DeviceSummaryText => $"{ReadyCount} sẵn sàng trên {DeviceCount} phát hiện";

        /// <summary>
        /// Device list rendered for manual selection when no single ready device is
        /// auto-selected. Backed by the same collection owned by the shell host.
        /// </summary>
        public ObservableCollection<DeviceViewModel>? Devices { get; private set; }

        /// <summary>Command injected by the shell host; selects a device as active.</summary>
        [ObservableProperty]
        private IRelayCommand? _selectDeviceCommand;

        [ObservableProperty]
        private IRelayCommand<DeviceViewModel>? _showDeviceLogsCommand;

        public event Action<string>? CopyDeviceLogsRequested;

        // --- Computed visibility booleans (single source: State + ActiveDevice) ---

        /// <summary>True before the first Detect pass — drives the "No device yet" hint.</summary>
        public bool IsIdle => State == DashboardDeviceState.Idle;

        /// <summary>True while DiscoverAsync is in flight — drives a detecting indicator.</summary>
        public bool IsDetecting => State == DashboardDeviceState.Detecting;

        /// <summary>True when the last Detect pass found zero devices.</summary>
        public bool HasNoDevices => State == DashboardDeviceState.NoDevices;

        /// <summary>True when devices exist but none is selected yet — drives the selection list.</summary>
        public bool ShowDeviceList =>
            State == DashboardDeviceState.DeviceSelected && ActiveDevice is null && DeviceCount > 0;

        /// <summary>True when an active device is selected and its panel should render.</summary>
        public bool ShowActivePanel =>
            !IsGridMode &&
            ActiveDevice is not null &&
            State is DashboardDeviceState.DeviceSelected or DashboardDeviceState.Running
                or DashboardDeviceState.Paused or DashboardDeviceState.Error;

        /// <summary>
        /// True when the selection/list/empty pane (everything that is not the active device
        /// panel) should render — the complement of <see cref="ShowActivePanel"/>. Avalonia 12
        /// ships no built-in bool negation converter, so this is exposed as a first-class prop.
        /// </summary>
        public bool ShowSelectionPane => !ShowActivePanel && !ShowGridPane;

        /// <summary>True when the multi-device grid should render (grid mode + at least one device).</summary>
        public bool ShowGridPane => IsGridMode && State != DashboardDeviceState.ConfiguringDevice && (Devices?.Count ?? 0) > 0;
        public bool ShowConfiguringPanel => State == DashboardDeviceState.ConfiguringDevice;

        /// <summary>True when any empty-state panel (Idle or NoDevices) should render.</summary>
        public bool ShowEmptyState => ShowEmptyNotDetected || ShowEmptyNoDevices;

        /// <summary>Empty-state panel shown before any Detect ("No device yet").</summary>
        public bool ShowEmptyNotDetected => State == DashboardDeviceState.Idle;

        /// <summary>Empty-state panel shown after a Detect that returned nothing.</summary>
        public bool ShowEmptyNoDevices => State == DashboardDeviceState.NoDevices;

        private bool _hasBeenStopped;

        /// <summary>True while the active device's bot or any selected device is running.</summary>
        public bool IsRunning => State == DashboardDeviceState.Running ||
            (Devices != null && Devices.Any(d => d.IsSelected && (d.Status == BotStatus.Running || d.Status == BotStatus.Starting)));

        /// <summary>True only after the bot was started and is now stopped (not on initial launch).</summary>
        public bool IsStopped => _hasBeenStopped && !IsRunning;

        /// <summary>True while the active device's bot is paused.</summary>
        public bool IsPaused => State == DashboardDeviceState.Paused;

        /// <summary>True when the active device's bot errored.</summary>
        public bool HasError => State == DashboardDeviceState.Error;

        /// <summary>Human-readable status line for the current state.</summary>
        /// <summary>Optional extra detail (e.g. Detect failure reason) appended to the status line.</summary>
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
        private DeviceViewModel? _trackedDevice;

        public SettingsViewModel SettingsViewModel => _settingsViewModel;

        public DashboardViewModel(SettingsViewModel settingsViewModel, IConfigStore configStore)
        {
            _settingsViewModel = settingsViewModel;
            _settingsViewModel.IsInstanceMode = true;
            // The config dialog renders SettingsViewModel inside MainWindow's overlay (outside the
            // DashboardView visual tree), so its Save/Cancel buttons cannot reach DashboardView via
            // a parent binding. Forward the VM-level events into the existing save/cancel commands.
                _settingsViewModel.InstanceSaveRequested += () => SaveDeviceConfigCommand.Execute(null);
                _settingsViewModel.InstanceCancelRequested += () => CancelDeviceConfigCommand.Execute(null);
                _configStore = configStore;
                LoadSettings();
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

        public DashboardViewModel() : this(new SettingsViewModel(), new ConfigStore())
        {
        }

        [RelayCommand]
        private void ConfigureDevice(DeviceViewModel device)
        {
            SelectedDeviceForConfig = device;
            string profileName = device.Device.ProfileKey;

            if (!_configStore.Profiles.Any(p => string.Equals(p.Name, profileName, StringComparison.OrdinalIgnoreCase)))
            {
                _configStore.LoadProfile("Default");
                JsonObject defaultTemplate = _configStore.LoadActiveConfig();
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

        private static string GetSettingsPath()
        {
            string appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            return Path.Combine(appData, "AutoClashOfClan20206", "app_settings.json");
        }

        private void LoadSettings()
        {
            string path = GetSettingsPath();
            try
            {
                if (File.Exists(path))
                {
                    string content = File.ReadAllText(path);
                    var json = JsonNode.Parse(content);
                    if (json is JsonObject obj && obj.TryGetPropertyValue("SelectedEmulatorFilter", out var val) && val is not null)
                    {
                        string? filter = val.ToString();
                        if (EmulatorFilters.Contains(filter))
                        {
                            SelectedEmulatorFilter = filter;
                        }
                    }
                }
            }
            catch
            {
                // Fallback to default
            }
        }

        private void SaveSettings()
        {
            string path = GetSettingsPath();
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                var obj = new JsonObject
                {
                    ["SelectedEmulatorFilter"] = SelectedEmulatorFilter
                };
                File.WriteAllText(path, obj.ToString());
            }
            catch
            {
                // Best effort
            }
        }

        /// <summary>Attaches the shell-owned device collection so the UI can bind it.</summary>
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

        /// <summary>Re-evaluate device-count-dependent visibility (call after a Detect pass).</summary>
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
                    device.StartCommand.Execute(null);
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
                    device.StopCommand.Execute(null);
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
