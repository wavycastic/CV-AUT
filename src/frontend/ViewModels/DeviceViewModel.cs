using System;
using System.Collections.ObjectModel;
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

        private readonly IDeviceSession _session;
        private readonly SessionStatsViewModel _stats;

        public Device Device { get; }

        public string DeviceId => Device.Id;

        public string DisplayName => Device.DisplayName;

        /// <summary>Session lifecycle status, mirrored from <see cref="IDeviceSession.StatusChanged"/>.</summary>
        [ObservableProperty]
        [NotifyCanExecuteChangedFor(nameof(StartCommand))]
        [NotifyCanExecuteChangedFor(nameof(PauseCommand))]
        [NotifyCanExecuteChangedFor(nameof(ResumeCommand))]
        [NotifyCanExecuteChangedFor(nameof(StopCommand))]
        private BotStatus _status = BotStatus.Idle;

        /// <summary>Per-device running totals (binds <c>DevicePanelView</c> stats block).</summary>
        public SessionStatsViewModel Stats => _stats;

        /// <summary>Per-device log buffer (never shared across devices).</summary>
        public ObservableCollection<LogEntry> Logs { get; } = new();

        /// <summary>Design-time / fallback ctor. Not used at runtime (DI injects the session).</summary>
        public DeviceViewModel()
            : this(new Device("127.0.0.1:5556", "127.0.0.1", 5556), new MockDeviceSession("127.0.0.1:5556"))
        {
        }

        public DeviceViewModel(Device device, IDeviceSession session)
        {
            Device = device;
            _session = session;
            _stats = new SessionStatsViewModel();

            _session.StatusChanged += OnSessionStatusChanged;
            _session.LogReceived += OnLogReceived;
            _session.StatsUpdated += OnStatsUpdated;
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
            });
        }

        private void OnStatsUpdated(SessionStats stats)
        {
            Dispatcher.UIThread.Post(() => _stats.Apply(stats));
        }

        public bool CanStart => Status is BotStatus.Idle or BotStatus.Stopped;
        public bool CanPause => Status is BotStatus.Running;
        public bool CanResume => Status is BotStatus.Paused;
        public bool CanStop => Status is BotStatus.Running or BotStatus.Paused or BotStatus.Starting;

        [RelayCommand(CanExecute = nameof(CanStart))]
        private async Task StartAsync()
        {
            try
            {
                await _session.StartAsync();
            }
            catch (Exception ex)
            {
                Logs.Add(new LogEntry("Start failed: " + ex.Message, LogLevel.Error, DeviceId));
            }
        }

        [RelayCommand(CanExecute = nameof(CanPause))]
        private async Task PauseAsync()
        {
            try
            {
                await _session.PauseAsync();
            }
            catch (Exception ex)
            {
                Logs.Add(new LogEntry("Pause failed: " + ex.Message, LogLevel.Error, DeviceId));
            }
        }

        [RelayCommand(CanExecute = nameof(CanResume))]
        private async Task ResumeAsync()
        {
            try
            {
                await _session.ResumeAsync();
            }
            catch (Exception ex)
            {
                Logs.Add(new LogEntry("Resume failed: " + ex.Message, LogLevel.Error, DeviceId));
            }
        }

        [RelayCommand(CanExecute = nameof(CanStop))]
        private async Task StopAsync()
        {
            try
            {
                await _session.StopAsync();
            }
            catch (Exception ex)
            {
                Logs.Add(new LogEntry("Stop failed: " + ex.Message, LogLevel.Error, DeviceId));
            }
        }

        [RelayCommand]
        private void ClearLog()
        {
            Logs.Clear();
        }

        /// <summary>Detach session event handlers. Called by the manager when removing this device.</summary>
        public void Detach()
        {
            _session.StatusChanged -= OnSessionStatusChanged;
            _session.LogReceived -= OnLogReceived;
            _session.StatsUpdated -= OnStatsUpdated;
        }
    }
}
