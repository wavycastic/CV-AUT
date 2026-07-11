using System;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CvAut.Models;
using CvAut.Services;
using CvAut.Services.Sessions;
using CvAut.Services.Emulators;

namespace CvAut.ViewModels
{
    /// <summary>
    /// Shell host view model: owns the device collection, the active device (single mode), the
    /// grid-mode flag, and the TopBar / Sidebar sub-view-models. The shell window binds
    /// TopBar + Sidebar + a <c>ContentControl</c> driven by <see cref="SidebarViewModel.CurrentPage"/>.
    ///
    /// Device runtime state lives in <see cref="DeviceViewModel"/> (one per device); this class
    /// only holds the collection + the active pointer + app-scoped flags — never per-device
    /// status/stats/logs directly (roadmap: "Không hardcode state runtime ngoài DeviceViewModel").
    /// </summary>
    public partial class MainWindowViewModel : ViewModelBase
    {
        private const string DefaultConfigPath = "Config/test_config.json";

        private readonly AppStateService _appState;
        private readonly IDeviceSessionManager _sessions;
        private readonly DashboardViewModel _dashboard;
        private readonly LogsViewModel _logs;
        private readonly LicenseViewModel _license;
        private readonly SettingsViewModel _settings;
        private readonly AdvancedViewModel _advanced;
        private readonly SetupWizardViewModel _wizard;
        private readonly IConfigStore _configStore;
        private readonly IEmulatorDiscovery _discovery;
        private readonly CvAut.Services.Notifications.INotificationService? _notifications;

        /// <summary>Design-time / fallback ctor (no DI).</summary>
        public MainWindowViewModel()
            : this(new AppStateService(), new DeviceSessionManager(), new ConfigStore(), new AdbEmulatorDiscovery(),
                   new DashboardViewModel(), new LogsViewModel(), new LicenseViewModel(),
                   new SettingsViewModel(), new AdvancedViewModel(), new SetupWizardViewModel(), null)
        {
        }

        public MainWindowViewModel(
            AppStateService appState,
            IDeviceSessionManager sessions,
            IConfigStore configStore,
            IEmulatorDiscovery discovery,
            DashboardViewModel dashboard,
            LogsViewModel logs,
            LicenseViewModel license,
            SettingsViewModel settings,
            AdvancedViewModel advanced,
            SetupWizardViewModel wizard,
            CvAut.Services.Notifications.INotificationService? notifications = null)
        {
            _appState = appState;
            _sessions = sessions;
            _configStore = configStore;
            _discovery = discovery;
            _notifications = notifications;
            _dashboard = dashboard;
            _logs = logs;
            _license = license;
            _settings = settings;
            _advanced = advanced;
            _wizard = wizard;
            ConfigPath = _configStore.ResolveActiveConfigPath();

            TopBar = new TopBarViewModel(appState, sessions, configStore);
            Sidebar = new SidebarViewModel();
            Sidebar.Seed(new[]
            {
                new NavItem("Bảng điều khiển", "ViewDashboard", dashboard),
                new NavItem("Thiết lập", "AutoFix", wizard),
                new NavItem("Nâng cao", "Tune", advanced),
                new NavItem("Nhật ký", "ScriptText", logs),
            });

            // Inject commands into Dashboard
            dashboard.DetectDevicesCommand = DetectDevicesCommand;
            dashboard.SelectDeviceCommand = SelectDeviceCommand;
            dashboard.ShowDeviceLogsCommand = ShowDeviceLogsCommand;
            dashboard.AttachDevices(Devices);

            // Keep the TopBar summary fresh when the active device's status changes.
            ActiveDeviceChanged += OnActiveDeviceStatusChanged;

            // Reset dialog mode and restore device fleet when navigating to the full logs page.
            Sidebar.PropertyChanged += (s, e) =>
            {
                if (e.PropertyName == nameof(Sidebar.CurrentPage) && Sidebar.CurrentPage == _logs)
                {
                    _logs.IsDialogMode = false;
                    _logs.SetDevices(Devices);
                    _logs.Refresh();
                }
            };
        }

        public TopBarViewModel TopBar { get; }

        public SidebarViewModel Sidebar { get; }

        public DashboardViewModel Dashboard => _dashboard;

        public LogsViewModel Logs => _logs;

        /// <summary>Settings page (also used as the per-device config panel host).</summary>
        public SettingsViewModel Settings => _settings;

        /// <summary>Advanced tuning page (delays + coordinate editor).</summary>
        public AdvancedViewModel Advanced => _advanced;

        /// <summary>Setup wizard page (emulator detect + display verify + trial run).</summary>
        public SetupWizardViewModel Wizard => _wizard;

        [ObservableProperty] private bool _isLicenseOpen;

        [RelayCommand]
        private void OpenLicense() => IsLicenseOpen = true;

        [RelayCommand]
        private void CloseLicense() => IsLicenseOpen = false;

        /// <summary>Device-scoped view models — one per connected/configured device.</summary>
        public ObservableCollection<DeviceViewModel> Devices { get; } = new();

        [ObservableProperty]
        private DeviceViewModel? _activeDevice;

        [ObservableProperty]
        private string _configPath = DefaultConfigPath;

        [ObservableProperty]
        private bool _isGridMode;

        /// <summary>Config file path used when starting a session on a newly detected device.</summary>
        partial void OnConfigPathChanged(string value)
        {
            // Phase 1: config is still file-driven (roadmap appendix). Phase 2 will use IConfigStore.
        }

        partial void OnActiveDeviceChanged(DeviceViewModel? value)
        {
            _appState.ActiveDeviceId = value?.DeviceId;
            _dashboard.ActiveDevice = value;
            ActiveDeviceChanged?.Invoke(value);

            if (value is not null)
            {
                SyncDashboardState(value.Status);
            }
            else
            {
                _dashboard.State = Devices.Count > 0 ? DashboardDeviceState.DeviceSelected : DashboardDeviceState.NoDevices;
            }
        }

        partial void OnIsGridModeChanged(bool value)
        {
            _appState.IsGridMode = value;
            _dashboard.IsGridMode = value;
        }

        /// <summary>Raised when <see cref="ActiveDevice"/> changes so listeners can re-subscribe.</summary>
        public event Action<DeviceViewModel?>? ActiveDeviceChanged;

        private void SyncDashboardState(BotStatus status)
        {
            _dashboard.State = status switch
            {
                BotStatus.Running => DashboardDeviceState.Running,
                BotStatus.Paused => DashboardDeviceState.Paused,
                BotStatus.Error => DashboardDeviceState.Error,
                _ => DashboardDeviceState.DeviceSelected
            };
        }

        /// <summary>Any device's bot-status change refreshes the TopBar running summary so grid mode
        /// (multiple concurrent sessions) reports accurate running/paused counts, not just the active one.</summary>
        private void OnDeviceStatusChangedForSummary(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(DeviceViewModel.Status))
            {
                Dispatcher.UIThread.Post(() =>
                {
                    TopBar.RefreshSummary();
                    TopBar.RefreshAggregate(Devices);
                    NotifyFleetCommands();
                });

                if (_notifications is not null && sender is DeviceViewModel vm)
                {
                    _ = _notifications.NotifyStatusAsync(vm.DisplayName, vm.Status);
                }
            }
        }

        private void OnDeviceStatsChangedForAggregate(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            Dispatcher.UIThread.Post(() => TopBar.RefreshAggregate(Devices));
        }

        private void OnActiveDeviceStatusChanged(DeviceViewModel? device)
        {
            if (device is null)
            {
                return;
            }

            SyncDashboardState(device.Status);

            device.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(DeviceViewModel.Status))
                {
                    Dispatcher.UIThread.Post(() =>
                    {
                        TopBar.RefreshSummary();
                        if (ActiveDevice == device)
                        {
                            SyncDashboardState(device.Status);
                        }
                    });
                }
            };
        }

        /// <summary>Detect connected ADB devices and build a <see cref="DeviceViewModel"/> for each.</summary>
        [RelayCommand]
        private async Task DetectDevicesAsync()
        {
            _dashboard.State = DashboardDeviceState.Detecting;
            _dashboard.DetectDetail = string.Empty;
            try
            {
                var found = await _discovery.DiscoverAsync(_dashboard.SelectedEmulatorFilter);
                foreach (DeviceViewModel existing in Devices)
                {
                    existing.PropertyChanged -= OnDeviceStatusChangedForSummary;
                    existing.Stats.PropertyChanged -= OnDeviceStatsChangedForAggregate;
                }

                Devices.Clear();
                _appState.Devices.Clear();

                foreach (Device device in found)
                {
                    _appState.Devices.Add(device);
                    var vm = new DeviceViewModel(device, (dev, cfgPath) => _sessions.GetOrCreate(dev, cfgPath), _configStore, _discovery);
                    vm.PropertyChanged += OnDeviceStatusChangedForSummary;
                    vm.Stats.PropertyChanged += OnDeviceStatsChangedForAggregate;
                    Devices.Add(vm);
                }

                _logs.SetDevices(Devices);
                _dashboard.NotifyDevicesChanged();
                TopBar.RefreshAggregate(Devices);
                NotifyFleetCommands();
                _dashboard.DeviceCount = Devices.Count;
                _dashboard.ReadyCount = Devices.Count(d => d.Device.Status == DeviceStatus.Ready);

                ActiveDevice = null;
                TopBar.RefreshSummary();

                if (Devices.Count > 0)
                {
                    _dashboard.State = DashboardDeviceState.DeviceSelected;
                }
                else
                {
                    _dashboard.State = DashboardDeviceState.NoDevices;
                }
            }
            catch (Exception ex)
            {
                _dashboard.DetectDetail = ex.Message;
                _dashboard.State = DashboardDeviceState.NoDevices;
            }
        }

        private bool CanStartAll() => Devices.Any(vm => vm.StartCommand.CanExecute(null));
        private bool CanPauseAll() => Devices.Any(vm => vm.PauseCommand.CanExecute(null));
        private bool CanStopAll() => Devices.Any(vm => vm.StopCommand.CanExecute(null));

        private void NotifyFleetCommands()
        {
            StartAllCommand.NotifyCanExecuteChanged();
            PauseAllCommand.NotifyCanExecuteChanged();
            StopAllCommand.NotifyCanExecuteChanged();
        }

        /// <summary>Start every device that can start. Sessions are created lazily inside each
        /// DeviceViewModel.Start, so All-commands must iterate Devices — not the session manager,
        /// which is empty until a device has started at least once.</summary>
        [RelayCommand(CanExecute = nameof(CanStartAll))]
        private async Task StartAll()
        {
            foreach (DeviceViewModel vm in Devices)
            {
                if (vm.StartCommand.CanExecute(null))
                {
                    await vm.StartCommand.ExecuteAsync(null);
                }
            }

            TopBar.RefreshSummary();
            NotifyFleetCommands();
        }

        [RelayCommand(CanExecute = nameof(CanPauseAll))]
        private async Task PauseAll()
        {
            foreach (DeviceViewModel vm in Devices)
            {
                if (vm.PauseCommand.CanExecute(null))
                {
                    await vm.PauseCommand.ExecuteAsync(null);
                }
            }

            TopBar.RefreshSummary();
            NotifyFleetCommands();
        }

        [RelayCommand(CanExecute = nameof(CanStopAll))]
        private async Task StopAll()
        {
            foreach (DeviceViewModel vm in Devices)
            {
                if (vm.StopCommand.CanExecute(null))
                {
                    await vm.StopCommand.ExecuteAsync(null);
                }
            }

            TopBar.RefreshSummary();
            NotifyFleetCommands();
        }

        [RelayCommand]
        private void ToggleGridMode()
        {
            IsGridMode = !IsGridMode;
        }

        [RelayCommand]
        private void SelectDevice(DeviceViewModel vm)
        {
            ActiveDevice = vm;
        }

        [RelayCommand]
        private void ShowDeviceLogs(DeviceViewModel vm)
        {
            Logs.ShowDevice(vm);
        }
    }

    /// <summary>Parses an ADB serial like "127.0.0.1:5556" or "emulator-5554".</summary>
    internal static class AdbEndpoint
    {
        public static bool TryParse(string serial, out string host, out int port)
        {
            host = "127.0.0.1";
            port = 5556;
            if (string.IsNullOrWhiteSpace(serial))
            {
                return false;
            }

            int sep = serial.LastIndexOf(':');
            if (sep > 0 && int.TryParse(serial.AsSpan(sep + 1), NumberStyles.Integer, CultureInfo.InvariantCulture, out port))
            {
                host = serial[..sep];
                return true;
            }

            // "emulator-XXXX" etc. — not a host:port endpoint; treat as localhost default.
            host = "127.0.0.1";
            return true;
        }
    }
}
