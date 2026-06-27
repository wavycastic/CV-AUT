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

        /// <summary>Design-time / fallback ctor (no DI).</summary>
        public MainWindowViewModel()
            : this(new AppStateService(), new DeviceSessionManager(),
                   new DashboardViewModel(), new SettingsViewModel(),
                   new AccountsViewModel(), new AdvancedViewModel(),
                   new LogsViewModel(), new LicenseViewModel())
        {
        }

        public MainWindowViewModel(
            AppStateService appState,
            IDeviceSessionManager sessions,
            DashboardViewModel dashboard,
            SettingsViewModel settings,
            AccountsViewModel accounts,
            AdvancedViewModel advanced,
            LogsViewModel logs,
            LicenseViewModel license)
        {
            _appState = appState;
            _sessions = sessions;
            _dashboard = dashboard;

            TopBar = new TopBarViewModel(appState, sessions);
            Sidebar = new SidebarViewModel();
            Sidebar.Seed(new[]
            {
                new NavItem("Dashboard", "ViewDashboard", dashboard),
                new NavItem("Settings", "Cog", settings),
                new NavItem("Accounts", "AccountMultiple", accounts),
                new NavItem("Advanced", "Tune", advanced),
                new NavItem("Logs", "ScriptText", logs),
                new NavItem("License", "KeyVariant", license),
            });

            // Keep the TopBar summary fresh when the active device's status changes.
            ActiveDeviceChanged += OnActiveDeviceStatusChanged;
        }

        public TopBarViewModel TopBar { get; }

        public SidebarViewModel Sidebar { get; }

        /// <summary>Current page rendered in the shell ContentControl (driven by Sidebar).</summary>
        public ViewModelBase? CurrentPage => Sidebar.CurrentPage;

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
        }

        partial void OnIsGridModeChanged(bool value)
        {
            _appState.IsGridMode = value;
        }

        /// <summary>Raised when <see cref="ActiveDevice"/> changes so listeners can re-subscribe.</summary>
        public event Action<DeviceViewModel?>? ActiveDeviceChanged;

        private void OnActiveDeviceStatusChanged(DeviceViewModel? device)
        {
            if (device is null)
            {
                return;
            }

            device.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(DeviceViewModel.Status))
                {
                    Dispatcher.UIThread.Post(() => TopBar.RefreshSummary());
                }
            };
        }

        /// <summary>Detect connected ADB devices and build a <see cref="DeviceViewModel"/> for each.</summary>
        [RelayCommand]
        private async Task DetectDevicesAsync()
        {
            var found = await Task.Run(() => BackendDiagnostics.ListAdbDevices());
            Devices.Clear();
            _appState.Devices.Clear();

            foreach (string serial in found)
            {
                if (!AdbEndpoint.TryParse(serial, out string host, out int port))
                {
                    continue;
                }

                var device = new Device(Device.MakeId(host, port), host, port, serial, serial);
                _appState.Devices.Add(device);

                IDeviceSession session = _sessions.GetOrCreate(device, ConfigPath);
                var vm = new DeviceViewModel(device, session);
                Devices.Add(vm);
            }

            ActiveDevice = Devices.FirstOrDefault();
            TopBar.RefreshSummary();
        }

        [RelayCommand]
        private void ToggleGridMode()
        {
            IsGridMode = !IsGridMode;
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
