using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CvAut.Models;
using CvAut.Services.Sessions;

namespace CvAut.ViewModels
{
    /// <summary>
    /// The device-scoped runtime state holder — the single source of truth for one device's
    /// status, stats and logs. This is the UI core that <c>DevicePanelView</c> binds to and that
    /// is reused unchanged for single and grid mode (roadmap: "<c>DeviceViewModel</c> +
    /// <c>DevicePanelView</c> là lõi UI, tái dùng cho single/grid").
    ///
    /// Talks to the backend only through <see cref="IDeviceSession"/> — never calls ADB/engine
    /// directly. All session events are marshalled to the UI thread before mutating state.
    /// </summary>
    public partial class DeviceViewModel : ViewModelBase
    {
        private const int MaxLogEntries = 500;

        private IDeviceSession? _session;
        private readonly Func<Device, string, IDeviceSession>? _startHandler;
        private readonly SessionStatsViewModel _stats;

        public Device Device { get; }

        public string DeviceId => Device.Id;

        public string DisplayName => Device.DisplayName;

        /// <summary>
        /// Human-readable explanation of the device's <see cref="DeviceStatus"/> for the UI,
        /// so a closed-but-installed emulator (or an unauthorized/offline one) shows a clear
        /// hint instead of a bare enum name. Bound in <c>DeviceListItemView</c> /
        /// <c>DevicePanelView</c>.
        /// </summary>
        public string DeviceStatusText => Device.Status switch
        {
            DeviceStatus.Ready => "Sẵn sàng",
            DeviceStatus.Installed => "Đã cài đặt \u2014 Chạy để khởi động giả lập",
            DeviceStatus.Unauthorized => "ADB chưa được ủy quyền \u2014 hãy chấp nhận yêu cầu gỡ lỗi USB trên giả lập",
            DeviceStatus.Offline => "ADB ngoại tuyến \u2014 khởi động lại giả lập hoặc ADB",
            _ => "Không xác định",
        };

        /// <summary>Session lifecycle status, mirrored from <see cref="IDeviceSession.StatusChanged"/>.</summary>
        [ObservableProperty]
        [NotifyCanExecuteChangedFor(nameof(StartCommand))]
        [NotifyCanExecuteChangedFor(nameof(PauseCommand))]
        [NotifyCanExecuteChangedFor(nameof(ResumeCommand))]
        [NotifyCanExecuteChangedFor(nameof(StopCommand))]
        [NotifyPropertyChangedFor(nameof(DisplayStatus))]
        [NotifyPropertyChangedFor(nameof(DisplayStatusColor))]
        [NotifyPropertyChangedFor(nameof(ShowStartButton))]
        [NotifyPropertyChangedFor(nameof(ShowStopButton))]
        private BotStatus _status = BotStatus.Idle;

        public string DisplayStatus => Status switch
        {
            BotStatus.Idle => "Rảnh",
            BotStatus.Starting => "Đang khởi động",
            BotStatus.Running => "Đang chạy",
            BotStatus.Paused => "Đang tạm dừng",
            BotStatus.Stopping => "Đang dừng",
            BotStatus.Stopped => "Đã dừng",
            BotStatus.Error => "Lỗi",
            _ => Status.ToString()
        };

        public string DisplayStatusColor => Status switch
        {
            BotStatus.Running => "LimeGreen",
            BotStatus.Paused => "Orange",
            BotStatus.Error => "Red",
            BotStatus.Starting => "Cyan",
            BotStatus.Stopping => "LightGray",
            _ => "Gray"
        };

        public bool ShowStartButton => Status is BotStatus.Idle or BotStatus.Stopped or BotStatus.Error;
        public bool ShowStopButton => !ShowStartButton;

        /// <summary>Per-device running totals (binds <c>DevicePanelView</c> stats block).</summary>
        public SessionStatsViewModel Stats => _stats;

        /// <summary>Per-device log buffer (never shared across devices).</summary>
        public ObservableCollection<LogEntry> Logs { get; } = new();

        public bool HasLogs => Logs.Count > 0;

        /// <summary>Design-time / fallback ctor. Not used at runtime (DI injects the start handler).</summary>
        public DeviceViewModel()
            : this(new Device("127.0.0.1", 5556, "Design device", "Design", DeviceStatus.Ready, "127.0.0.1:5556"), null)
        {
            AttachSession(new MockDeviceSession("127.0.0.1:5556"));
        }

        private readonly IConfigStore _configStore;
        private readonly CvAut.Services.Emulators.IEmulatorDiscovery? _discovery;

        [ObservableProperty]
        private string _selectedPlayMode = "Làng chính";

        /// <summary>Non-blocking display precheck note (e.g. resolution/DPI mismatch) shown before/after Start.</summary>
        [ObservableProperty] private string _displayWarning = string.Empty;

        public ObservableCollection<string> PlayModes { get; } = new()
        {
            "Làng chính", "Làng đêm", "Trò chơi hội (sắp ra mắt)", "Kinh đô hội (sắp ra mắt)"
        };

        /// <summary>
        /// Runtime ctor. Detect creates the VM with a <paramref name="startHandler"/> that
        /// writes the selected device into the active config and builds a fresh session — the
        /// session is only realised on Start so Detect stays side-effect-light (item 8).
        /// </summary>
        public DeviceViewModel(Device device, Func<Device, string, IDeviceSession>? startHandler = null, IConfigStore? configStore = null, CvAut.Services.Emulators.IEmulatorDiscovery? discovery = null)
        {
            Device = device;
            _startHandler = startHandler;
            _stats = new SessionStatsViewModel();
            _configStore = configStore ?? new ConfigStore();
            _discovery = discovery;
            LoadSelectedPlayMode();
        }

        /// <summary>
        /// Subscribes session events and seeds initial status/stats. Called by Start once the
        /// session has been built with the up-to-date config, or by the design-time ctor.
        /// </summary>
        public void AttachSession(IDeviceSession session)
        {
            if (_session is not null)
            {
                Detach();
            }

            _session = session;
            _stats.Apply(_session.Stats);
            _session.StatusChanged += OnSessionStatusChanged;
            _session.LogReceived += OnLogReceived;
            _session.StatsUpdated += OnStatsUpdated;
            Status = _session.Status;
        }

        private void OnSessionStatusChanged(BotStatus status)
        {
            Dispatcher.UIThread.Post(() => Status = status);
        }

        private void OnLogReceived(LogEntry entry)
        {
            Dispatcher.UIThread.Post(() =>
            {
                Logs.Add(entry);
                while (Logs.Count > MaxLogEntries)
                {
                    Logs.RemoveAt(0);
                }

                OnPropertyChanged(nameof(HasLogs));
            });
        }

        private void OnStatsUpdated(SessionStats stats)
        {
            Dispatcher.UIThread.Post(() => _stats.Apply(stats));
        }

        // Start is allowed when the bot is idle/stopped AND the device is either Ready
        // (ADB online now) or Installed (emulator executable known — Start will trigger
        // EmulatorBootstrapper to launch it and wait for ADB). Unauthorized/Offline/Unknown
        // stay blocked: the bot cannot reach a usable ADB shell from those states.
        public bool CanStart => Status is BotStatus.Idle or BotStatus.Stopped
            && Device.Status is DeviceStatus.Ready or DeviceStatus.Installed;

        /// <summary>Device-side start eligibility, independent of bot status (for UI hints).</summary>
        public bool DeviceCanStart => Device.Status is DeviceStatus.Ready or DeviceStatus.Installed;
        public bool CanPause => Status is BotStatus.Running;
        public bool CanResume => Status is BotStatus.Paused;
        public bool CanStop => Status is BotStatus.Running or BotStatus.Paused or BotStatus.Starting;

        [RelayCommand(CanExecute = nameof(CanStart))]
        private async Task StartAsync()
        {
            try
            {
                // Item 8: session is built lazily on first Start so the active config is written
                // (and read by CVAutomationFramework) with the user's final selection — never a
                // stale host/port captured during Detect.
                if (_session is null && _startHandler is not null)
                {
                    // Phase 3: each device gets its own config file whose device_connection points at
                    // this device, so concurrent sessions never share a host/port or clobber each other.
                    string configPath = _configStore.PrepareDeviceConfig(Device.ProfileKey, Device.Host, Device.Port);
                    ApplySelectedPlayModeTo(configPath);
                    IDeviceSession session = _startHandler(Device, configPath);
                    AttachSession(session);
                }

                if (_session is null)
                {
                    return;
                }

                await WarnIfDisplayMismatchAsync();

                await _session.StartAsync();
            }
            catch (Exception ex)
            {
                Logs.Add(new LogEntry("Khởi động thất bại: " + ex.Message, LogLevel.Error, DeviceId));
                OnPropertyChanged(nameof(HasLogs));
            }
        }

        [RelayCommand(CanExecute = nameof(CanPause))]
        private async Task PauseAsync()
        {
            if (_session is null)
            {
                return;
            }

            try
            {
                await _session.PauseAsync();
            }
            catch (Exception ex)
            {
                Logs.Add(new LogEntry("Tạm dừng thất bại: " + ex.Message, LogLevel.Error, DeviceId));
                OnPropertyChanged(nameof(HasLogs));
            }
        }

        [RelayCommand(CanExecute = nameof(CanResume))]
        private async Task ResumeAsync()
        {
            if (_session is null)
            {
                return;
            }

            try
            {
                await _session.ResumeAsync();
            }
            catch (Exception ex)
            {
                Logs.Add(new LogEntry("Tiếp tục thất bại: " + ex.Message, LogLevel.Error, DeviceId));
                OnPropertyChanged(nameof(HasLogs));
            }
        }

        [RelayCommand(CanExecute = nameof(CanStop))]
        private async Task StopAsync()
        {
            if (_session is null)
            {
                return;
            }

            try
            {
                await _session.StopAsync();
            }
            catch (Exception ex)
            {
                Logs.Add(new LogEntry("Dừng thất bại: " + ex.Message, LogLevel.Error, DeviceId));
                OnPropertyChanged(nameof(HasLogs));
            }
        }

        [RelayCommand]
        private void ClearLog()
        {
            Logs.Clear();
            OnPropertyChanged(nameof(HasLogs));
        }

        /// <summary>Detach session event handlers. Called by the manager when removing this device.</summary>
        public void Detach()
        {
            if (_session is null)
            {
                return;
            }

            _session.StatusChanged -= OnSessionStatusChanged;
            _session.LogReceived -= OnLogReceived;
            _session.StatsUpdated -= OnStatsUpdated;
        }

        /// <summary>
        /// Best-effort display precheck: if a discovery service is available, verify the emulator
        /// is at 1600x900 / 240dpi and surface a non-blocking warning (log + <see cref="DisplayWarning"/>)
        /// when it is not. Never blocks Start — the user may have marked the display OK manually.
        /// </summary>
        private async Task WarnIfDisplayMismatchAsync()
        {
            if (_discovery is null)
            {
                return;
            }

            try
            {
                var info = await _discovery.GetDisplayInfoAsync(Device);
                if (info.ResolutionOk && info.DpiOk)
                {
                    DisplayWarning = string.Empty;
                    return;
                }

                int expectedDpi = !string.IsNullOrWhiteSpace(Device.EmulatorType) && Device.EmulatorType.Equals("BlueStacks", StringComparison.OrdinalIgnoreCase) ? 300 : 240;
                DisplayWarning = $"Màn hình {info.Width}x{info.Height} / {info.DensityDpi}dpi khác chuẩn 1600x900 / {expectedDpi}dpi — bot có thể chạy sai.";
                Logs.Add(new LogEntry(DisplayWarning, LogLevel.Warning, DeviceId));
            OnPropertyChanged(nameof(HasLogs));
            }
            catch
            {
                // Precheck is best-effort; never block Start on a probe failure.
            }
        }

        /// <summary>Writes the current play-mode token into a specific config file (the per-device config
        /// prepared for Start), without touching the active profile.</summary>
        private void ApplySelectedPlayModeTo(string configPath)
        {
            try
            {
                if (System.IO.File.Exists(configPath) &&
                    System.Text.Json.Nodes.JsonNode.Parse(System.IO.File.ReadAllText(configPath)) is System.Text.Json.Nodes.JsonObject cfg)
                {
                    cfg["play_mode"] = Models.PlayMode.ToToken(SelectedPlayMode);
                    System.IO.File.WriteAllText(configPath, cfg.ToJsonString(new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
                }
            }
            catch
            {
                // Best effort — Start proceeds with whatever the prepared config already holds.
            }
        }

        private string GetProfileName()
        {
            return Device.ProfileKey;
        }

        private void LoadSelectedPlayMode()
        {
            string profileName = GetProfileName();
            try
            {
                if (_configStore.Profiles.Any(p => string.Equals(p.Name, profileName, StringComparison.OrdinalIgnoreCase)))
                {
                    _configStore.LoadProfile(profileName);
                    var config = _configStore.LoadActiveConfig();
                    if (config.TryGetPropertyValue("play_mode", out var val) && val is not null)
                    {
                        SelectedPlayMode = Models.PlayMode.ToDisplay(val.ToString());
                    }
                }
            }
            catch
            {
                SelectedPlayMode = "Làng chính";
            }
        }

        partial void OnSelectedPlayModeChanged(string value)
        {
            string profileName = GetProfileName();
            try
            {
                if (!_configStore.Profiles.Any(p => string.Equals(p.Name, profileName, StringComparison.OrdinalIgnoreCase)))
                {
                    _configStore.LoadProfile("Default");
                    var template = _configStore.LoadActiveConfig();
                    _configStore.SaveProfileAs(profileName, template);
                }

                _configStore.LoadProfile(profileName);
                var config = _configStore.LoadActiveConfig();
                config["play_mode"] = Models.PlayMode.ToToken(value);
                _configStore.SaveActiveConfig(config);
            }
            catch
            {
                // Best effort
            }
        }
    }
}
