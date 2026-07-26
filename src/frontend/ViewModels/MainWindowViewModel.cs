using System;
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
    ///
    /// This file covers shell composition and app-scoped state. The device fleet — detection,
    /// per-device subscriptions and the All-commands — lives in <c>MainWindowViewModel.Devices.cs</c>.
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
        private readonly IConfigStore _configStore;
        private readonly IEmulatorDiscovery _discovery;
        private readonly CvAut.Services.Notifications.INotificationService? _notifications;

        /// <summary>Design-time / fallback ctor (no DI).</summary>
        public MainWindowViewModel()
            : this(new AppStateService(), new DeviceSessionManager(), new ConfigStore(), new AdbEmulatorDiscovery(),
                   new DashboardViewModel(), new LogsViewModel(), new LicenseViewModel(),
                   new SettingsViewModel(), new AdvancedViewModel(), null)
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
            ConfigPath = _configStore.ResolveActiveConfigPath();

            TopBar = new TopBarViewModel(appState, sessions, configStore);
            Sidebar = new SidebarViewModel();
            Sidebar.Seed(new[]
            {
                new NavItem("Thiết bị", "MonitorDashboard", dashboard),
                new NavItem("Nâng cao", "Cogs", advanced),
                new NavItem("Nhật ký", "Terminal", logs),
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

        [ObservableProperty] private bool _isLicenseOpen;

        [RelayCommand]
        private void OpenLicense() => IsLicenseOpen = true;

        [RelayCommand]
        private void CloseLicense() => IsLicenseOpen = false;

        [ObservableProperty]
        private string _configPath = DefaultConfigPath;

        [ObservableProperty]
        private bool _isGridMode;

        /// <summary>Config file path used when starting a session on a newly detected device.</summary>
        partial void OnConfigPathChanged(string value)
        {
            // Phase 1: config is still file-driven (roadmap appendix). Phase 2 will use IConfigStore.
        }

        partial void OnIsGridModeChanged(bool value)
        {
            _appState.IsGridMode = value;
            _dashboard.IsGridMode = value;
        }

        [RelayCommand]
        private void ToggleGridMode()
        {
            IsGridMode = !IsGridMode;
        }

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
    }
}
