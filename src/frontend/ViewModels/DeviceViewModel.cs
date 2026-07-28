using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CvAut.Configuration;
using CvAut.Models;
using CvAut.Services;
using CvAut.Services.Configuration;
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
    /// <remarks>
    /// Split across partials: <c>DeviceViewModel.Session.cs</c> holds the session subscription and
    /// the log buffer, <c>.Commands.cs</c> the start/pause/resume/stop lifecycle,
    /// <c>.PlayMode.cs</c> the per-profile play mode, and <c>.DisplayCheck.cs</c> the resolution
    /// precheck.
    /// </remarks>
    public partial class DeviceViewModel : ViewModelBase
    {
        private IDeviceSession? _session;
        private readonly Func<Device, string, IDeviceSession>? _startHandler;
        private readonly SessionStatsViewModel _stats;
        private readonly IConfigStore _configStore;
        private readonly IProfileConfigSnapshotProvider _configSnapshots;
        private readonly CvAut.Services.Emulators.IEmulatorDiscovery? _discovery;

        /// <summary>Design-time / fallback ctor. Not used at runtime (DI injects the start handler).</summary>
        public DeviceViewModel()
            : this(new Device("127.0.0.1", 5556, "Design device", "Design", DeviceStatus.Ready, "127.0.0.1:5556"), null)
        {
            AttachSession(new MockDeviceSession("127.0.0.1:5556"));
        }

        /// <summary>
        /// Runtime ctor. Detect creates the VM with a <paramref name="startHandler"/> that
        /// writes the selected device into the active config and builds a fresh session — the
        /// session is only realised on Start so Detect stays side-effect-light (item 8).
        /// </summary>
        public DeviceViewModel(Device device, Func<Device, string, IDeviceSession>? startHandler = null, IConfigStore? configStore = null, CvAut.Services.Emulators.IEmulatorDiscovery? discovery = null, IProfileConfigSnapshotProvider? configSnapshots = null)
        {
            Device = device;
            _startHandler = startHandler;
            _stats = new SessionStatsViewModel();
            _configStore = configStore ?? new ConfigStore();
            _configSnapshots = configSnapshots ?? new ProfileConfigSnapshotProvider(_configStore);
            _discovery = discovery;
            LoadSelectedPlayMode();
        }

        public Device Device { get; }

        public string DeviceId => Device.Id;

        public string DisplayName => Device.DisplayName;

        public Bitmap? EmulatorIcon => EmulatorIconLoader.Load(Device.EmulatorType);

        /// <summary>
        /// Human-readable explanation of the device's <see cref="DeviceStatus"/> for the UI,
        /// so a closed-but-installed emulator (or an unauthorized/offline one) shows a clear
        /// hint instead of a bare enum name. Bound in <c>DeviceListItemView</c> /
        /// <c>DevicePanelView</c>.
        /// </summary>
        public string DeviceStatusText => DeviceStatusPresenter.DeviceStatusText(Device.Status, Device.CanAutoStart);

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

        [ObservableProperty]
        [NotifyCanExecuteChangedFor(nameof(StartCommand))]
        [NotifyCanExecuteChangedFor(nameof(StopCommand))]
        [NotifyPropertyChangedFor(nameof(SelectionHint))]
        private bool _isSelected = true;

        public string DisplayStatus => DeviceStatusPresenter.DisplayStatus(Status);

        public string DisplayStatusColor => DeviceStatusPresenter.DisplayStatusColor(Status);

        /// <summary>
        /// Clean subtitle string for device list items. Prevents trailing dots and duplicate vendor names.
        /// </summary>
        public string SubTitleText => DeviceStatusPresenter.SubTitle(Device.Source, Device.Serial, Device.Port, DisplayName);

        public string StatusBadgeText => DeviceStatusPresenter.StatusBadgeText(Status, Device.Status, Device.CanAutoStart);

        public string StatusBadgeColor => DeviceStatusPresenter.StatusBadgeColor(Status, Device.Status, Device.CanAutoStart);

        public string SelectionHint => IsSelected
            ? "Bỏ chọn instance này"
            : "Chọn instance này để chạy";

        public bool ShowStartButton => Status is BotStatus.Idle or BotStatus.Stopped or BotStatus.Error;
        public bool ShowStopButton => !ShowStartButton;

        /// <summary>Per-device running totals (binds <c>DevicePanelView</c> stats block).</summary>
        public SessionStatsViewModel Stats => _stats;
    }
}
