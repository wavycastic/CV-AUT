using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CvAut.Services;
using CvAut.Services.Sessions;

namespace CvAut.ViewModels
{
    /// <summary>
    /// TopBar state: global lifecycle (Start/Pause/Stop All), running summary, theme/lang,
    /// license badge. Always visible across every page (roadmap: "TopBar luôn hiển thị").
    /// Reads the live device list + sessions from <see cref="AppStateService"/> /
    /// <see cref="IDeviceSessionManager"/>; it does not own device runtime state itself.
    /// </summary>
    public partial class TopBarViewModel : ViewModelBase
    {
        private readonly AppStateService _appState;
        private readonly IDeviceSessionManager _sessions;

        [ObservableProperty] private int _runningCount;
        [ObservableProperty] private int _pausedCount;
        [ObservableProperty] private bool _isAnyRunning;

        public TopBarViewModel(AppStateService appState, IDeviceSessionManager sessions)
        {
            _appState = appState;
            _sessions = sessions;
            RefreshSummary();
        }

        /// <summary>Design-time ctor.</summary>
        public TopBarViewModel() : this(new AppStateService(), new DeviceSessionManager())
        {
        }

        /// <summary>Start every live session.</summary>
        [RelayCommand]
        private void StartAll()
        {
            foreach (IDeviceSession s in _sessions.Sessions)
            {
                _ = s.StartAsync();
            }

            RefreshSummary();
        }

        /// <summary>Pause every running session.</summary>
        [RelayCommand]
        private void PauseAll()
        {
            foreach (IDeviceSession s in _sessions.Sessions)
            {
                if (s.Status is CvAut.Models.BotStatus.Running)
                {
                    _ = s.PauseAsync();
                }
            }

            RefreshSummary();
        }

        /// <summary>Stop every live session.</summary>
        [RelayCommand]
        private void StopAll()
        {
            foreach (IDeviceSession s in _sessions.Sessions)
            {
                _ = s.StopAsync();
            }

            RefreshSummary();
        }

        /// <summary>Recompute the running/paused counts. Call after device list changes.</summary>
        public void RefreshSummary()
        {
            int running = 0, paused = 0;
            foreach (IDeviceSession s in _sessions.Sessions)
            {
                if (s.Status is CvAut.Models.BotStatus.Running) running++;
                else if (s.Status is CvAut.Models.BotStatus.Paused) paused++;
            }

            RunningCount = running;
            PausedCount = paused;
            IsAnyRunning = running > 0 || paused > 0;
        }
    }
}
