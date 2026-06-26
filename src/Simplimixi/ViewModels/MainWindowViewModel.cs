using System;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace CvAut.ViewModels
{
    /// <summary>
    /// Skeleton view model that drives <see cref="AutomationRunner"/> from the UI.
    /// Mirrors the IAutomationRunner lifecycle (Start/Stop/Pause/Resume + Completion)
    /// and exposes observable state for binding.
    /// </summary>
    public partial class MainWindowViewModel : ViewModelBase
    {
        private const string DefaultConfigPath = "Config/test_config.json";

        private AutomationRunner? _runner;

        [ObservableProperty]
        private string _configPath = DefaultConfigPath;

        [ObservableProperty]
        [NotifyCanExecuteChangedFor(nameof(StartCommand))]
        [NotifyCanExecuteChangedFor(nameof(StopCommand))]
        [NotifyCanExecuteChangedFor(nameof(PauseCommand))]
        [NotifyCanExecuteChangedFor(nameof(ResumeCommand))]
        private bool _isRunning;

        [ObservableProperty]
        [NotifyCanExecuteChangedFor(nameof(ResumeCommand))]
        private bool _isPaused;

        [ObservableProperty]
        private string _status = "Idle";

        public string Greeting { get; } = "SimpliMixi — CV Automation";

        /// <summary>Starts the automation runner with the current config path.</summary>
        [RelayCommand(CanExecute = nameof(CanStart))]
        private void Start()
        {
            if (_runner is not null)
                return;

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
                return;

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
                return;

            _runner.Pause();
            IsPaused = true;
            Status = "Paused";
        }

        private bool CanPause() => IsRunning && !IsPaused;

        [RelayCommand(CanExecute = nameof(CanResume))]
        private void Resume()
        {
            if (_runner is null)
                return;

            _runner.Resume();
            IsPaused = false;
            Status = "Running";
        }

        private bool CanResume() => IsPaused;

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
