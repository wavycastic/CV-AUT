using System;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Threading.Tasks;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CvAut.Services;

namespace CvAut.ViewModels
{
    /// <summary>
    /// Drives <see cref="AutomationRunner"/> from the UI: device picker (ADB host/port),
    /// attack template selection, lifecycle control (start/stop/pause/resume), and a live
    /// log fed by <see cref="AppLog"/>. Config edits are persisted to the config file before
    /// a run so the backend (which reads the file) picks them up.
    /// </summary>
    public partial class MainWindowViewModel : ViewModelBase
    {
        private const string DefaultConfigPath = "Config/test_config.json";
        private const int MaxLogLines = 500;

        private readonly AppStateService _appState;

        private AutomationRunner? _runner;

        [ObservableProperty]
        private string _configPath = DefaultConfigPath;

        [ObservableProperty]
        private string _host = "127.0.0.1";

        [ObservableProperty]
        private string _port = "5556";

        [ObservableProperty]
        private string? _selectedAttack;

        [ObservableProperty]
        private string? _selectedDevice;

        [ObservableProperty]
        [NotifyCanExecuteChangedFor(nameof(StartCommand))]
        [NotifyCanExecuteChangedFor(nameof(StopCommand))]
        [NotifyCanExecuteChangedFor(nameof(PauseCommand))]
        [NotifyCanExecuteChangedFor(nameof(ResumeCommand))]
        [NotifyCanExecuteChangedFor(nameof(DetectDevicesCommand))]
        private bool _isRunning;

        [ObservableProperty]
        [NotifyCanExecuteChangedFor(nameof(ResumeCommand))]
        private bool _isPaused;

        [ObservableProperty]
        private bool _isBusy;

        [ObservableProperty]
        private string _status = "Idle";

        public string Greeting { get; } = "SimpliMixi — CV Automation";

        public ObservableCollection<string> Attacks { get; } = new();

        public ObservableCollection<string> Devices { get; } = new();

        public ObservableCollection<string> LogLines { get; } = new();

        /// <summary>Design-time / fallback ctor. Avalonia's Design.DataContext needs a parameterless ctor.</summary>
        public MainWindowViewModel()
            : this(new AppStateService())
        {
        }

        public MainWindowViewModel(AppStateService appState)
        {
            _appState = appState;

            // Live log: AppLog raises on the backend's thread, so marshal to the UI thread.
            AppLog.LineWritten += OnLogLine;

            LoadAttacks();
            LoadConfigIntoFields();
        }

        private void OnLogLine(string line)
        {
            Dispatcher.UIThread.Post(() =>
            {
                LogLines.Add(line);
                while (LogLines.Count > MaxLogLines)
                {
                    LogLines.RemoveAt(0);
                }
            });
        }

        private void LoadAttacks()
        {
            Attacks.Clear();
            foreach (string name in AttackCatalog.Discover())
            {
                Attacks.Add(name);
            }
        }

        private void LoadConfigIntoFields()
        {
            ConfigStore.DeviceConnection cfg = ConfigStore.Read(ConfigPath);
            Host = cfg.Host;
            Port = cfg.Port.ToString(CultureInfo.InvariantCulture);
            if (!string.IsNullOrWhiteSpace(cfg.Attack))
            {
                SelectedAttack = cfg.Attack;
            }
        }

        /// <summary>Reload the attack list and config fields when the config path changes.</summary>
        partial void OnConfigPathChanged(string value)
        {
            if (!IsRunning)
            {
                LoadConfigIntoFields();
            }
        }

        /// <summary>When the user picks a detected device serial like "127.0.0.1:5556", split it into host/port.</summary>
        partial void OnSelectedDeviceChanged(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return;
            }

            int sep = value.LastIndexOf(':');
            if (sep > 0 && int.TryParse(value.AsSpan(sep + 1), out int parsedPort))
            {
                Host = value[..sep];
                Port = parsedPort.ToString(CultureInfo.InvariantCulture);
            }
        }

        [RelayCommand(CanExecute = nameof(CanDetect))]
        private async Task DetectDevicesAsync()
        {
            IsBusy = true;
            Status = "Detecting devices…";
            try
            {
                var found = await Task.Run(() => BackendDiagnostics.ListAdbDevices());
                Devices.Clear();
                foreach (string serial in found)
                {
                    Devices.Add(serial);
                }

                if (Devices.Count > 0)
                {
                    SelectedDevice = Devices[0];
                    Status = $"Found {Devices.Count} device(s)";
                }
                else
                {
                    Status = "No devices detected";
                }
            }
            catch (Exception ex)
            {
                Status = "Detect failed: " + ex.Message;
            }
            finally
            {
                IsBusy = false;
            }
        }

        private bool CanDetect() => !IsRunning && !IsBusy;

        [RelayCommand(CanExecute = nameof(CanStart))]
        private void Start()
        {
            if (_runner is not null)
            {
                return;
            }

            if (!int.TryParse(Port, NumberStyles.Integer, CultureInfo.InvariantCulture, out int port))
            {
                Status = "Invalid port";
                return;
            }

            try
            {
                ConfigStore.Save(ConfigPath, Host.Trim(), port, SelectedAttack ?? string.Empty);
            }
            catch (Exception ex)
            {
                Status = "Config save failed: " + ex.Message;
                return;
            }

            _runner = new AutomationRunner(ConfigPath);
            _runner.Completion.ContinueWith(OnCompleted, TaskScheduler.FromCurrentSynchronizationContext());

            _runner.Start();
            IsRunning = true;
            IsPaused = false;
            Status = "Running";
        }

        private bool CanStart() => !IsRunning;

        [RelayCommand(CanExecute = nameof(CanStop))]
        private void Stop()
        {
            if (_runner is null)
            {
                return;
            }

            _runner.Stop();
            DisposeRunner();
            IsRunning = false;
            IsPaused = false;
            Status = "Stopped";
        }

        private bool CanStop() => IsRunning;

        [RelayCommand(CanExecute = nameof(CanPause))]
        private void Pause()
        {
            if (_runner is null)
            {
                return;
            }

            _runner.Pause();
            IsPaused = true;
            Status = "Paused";
        }

        private bool CanPause() => IsRunning && !IsPaused;

        [RelayCommand(CanExecute = nameof(CanResume))]
        private void Resume()
        {
            if (_runner is null)
            {
                return;
            }

            _runner.Resume();
            IsPaused = false;
            Status = "Running";
        }

        private bool CanResume() => IsPaused;

        [RelayCommand]
        private void ClearLog()
        {
            LogLines.Clear();
        }

        private void OnCompleted(Task completion)
        {
            IsRunning = false;
            IsPaused = false;
            Status = completion.IsFaulted
                ? "Faulted: " + (completion.Exception?.GetBaseException().Message ?? "unknown")
                : "Completed";
            DisposeRunner();
        }

        private void DisposeRunner()
        {
            _runner?.Dispose();
            _runner = null;
        }
    }
}
