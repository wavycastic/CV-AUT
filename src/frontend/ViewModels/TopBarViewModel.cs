using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CvAut.Models;
using CvAut.Services;
using CvAut.Services.Sessions;

namespace CvAut.ViewModels
{
    public partial class TopBarViewModel : ViewModelBase
    {
        private readonly AppStateService _appState;
        private readonly IDeviceSessionManager _sessions;
        private readonly IConfigStore _configStore;

        [ObservableProperty] private int _runningCount;
        [ObservableProperty] private int _pausedCount;
        [ObservableProperty] private bool _isAnyRunning;
        [ObservableProperty] private bool _hasStatus;
        [ObservableProperty] private string _statusText = string.Empty;
        [ObservableProperty] private string _statusColor = "#ef4444";
        [ObservableProperty] private string _statusBorderColor = "#80ef4444";
        [ObservableProperty] private string _windowTitle = "SimpliMixi Console";
        [ObservableProperty] private string _activeProfileName = "Default";
        [ObservableProperty] private BotProfile? _selectedProfile;
        private bool _hasEverStarted;

        // Fleet aggregate (grid mode / multi-device summary).
        [NotifyPropertyChangedFor(nameof(HasErrors))]
        [ObservableProperty] private int _errorCount;
        [ObservableProperty] private int _totalBattles;
        [ObservableProperty] private int _totalStars;
        [ObservableProperty] private long _totalGold;
        [ObservableProperty] private long _totalElixir;
        [ObservableProperty] private long _totalDarkElixir;

        public ObservableCollection<BotProfile> Profiles { get; } = new();

        public TopBarViewModel(AppStateService appState, IDeviceSessionManager sessions, IConfigStore configStore)
        {
            _appState = appState;
            _sessions = sessions;
            _configStore = configStore;
            RefreshProfiles();
            RefreshSummary();
        }

        public TopBarViewModel() : this(new AppStateService(), new DeviceSessionManager(), new ConfigStore())
        {
        }

        [RelayCommand]
        private void LoadProfile()
        {
            if (SelectedProfile is null)
            {
                return;
            }

            _configStore.LoadProfile(SelectedProfile.Name);
            ActiveProfileName = _configStore.ActiveProfileName;
            RefreshProfiles();
        }

        private bool _syncingProfiles;

        partial void OnSelectedProfileChanged(BotProfile? value)
        {
            if (_syncingProfiles || value is null || value.Name == _configStore.ActiveProfileName)
            {
                return;
            }

            _configStore.LoadProfile(value.Name);
            ActiveProfileName = _configStore.ActiveProfileName;
        }

        public void RefreshProfiles()
        {
            _syncingProfiles = true;
            try
            {
            Profiles.Clear();
            foreach (BotProfile profile in _configStore.Profiles)
            {
                Profiles.Add(profile);
                if (profile.Name == _configStore.ActiveProfileName)
                {
                    SelectedProfile = profile;
                    ActiveProfileName = profile.Name;
                }
            }
            }
            finally
            {
                _syncingProfiles = false;
            }
        }

        /// <summary>True when any device is in the Error state (drives the fleet error badge).</summary>
        public bool HasErrors => ErrorCount > 0;

        public void RefreshSummary()
        {
            int running = 0, paused = 0, starting = 0;
            foreach (IDeviceSession s in _sessions.Sessions)
            {
                if (s.Status is BotStatus.Running) running++;
                else if (s.Status is BotStatus.Starting) starting++;
                else if (s.Status is BotStatus.Paused) paused++;
            }

            int activeCount = running + starting;
            RunningCount = activeCount;
            PausedCount = paused;
            IsAnyRunning = activeCount > 0 || paused > 0;

            if (activeCount > 0)
            {
                _hasEverStarted = true;
                HasStatus = true;
                StatusText = activeCount == 1 ? "Bot đang chạy" : $"Bot đang chạy ({activeCount})";
                StatusColor = "#22c55e";
                StatusBorderColor = "#8022c55e";
                WindowTitle = $"SimpliMixi Console - {StatusText}";
            }
            else if (paused > 0)
            {
                _hasEverStarted = true;
                HasStatus = true;
                StatusText = "Đang tạm dừng";
                StatusColor = "#f59e0b";
                StatusBorderColor = "#80f59e0b";
                WindowTitle = $"SimpliMixi Console - {StatusText}";
            }
            else if (_hasEverStarted)
            {
                HasStatus = true;
                StatusText = "Bot đã dừng";
                StatusColor = "#ef4444";
                StatusBorderColor = "#80ef4444";
                WindowTitle = $"SimpliMixi Console - {StatusText}";
            }
            else
            {
                HasStatus = false;
                StatusText = string.Empty;
                WindowTitle = "SimpliMixi Console";
            }
        }

        public void UpdateStatusFromDevices(System.Collections.Generic.IEnumerable<DeviceViewModel>? devices)
        {
            int running = 0, starting = 0, paused = 0, error = 0, stopped = 0;

            if (devices != null)
            {
                foreach (DeviceViewModel d in devices)
                {
                    if (d.Status is BotStatus.Running) running++;
                    else if (d.Status is BotStatus.Starting) starting++;
                    else if (d.Status is BotStatus.Paused) paused++;
                    else if (d.Status is BotStatus.Error) error++;
                    else if (d.Status is BotStatus.Stopped or BotStatus.Stopping) stopped++;
                }
            }

            int activeCount = running + starting;
            if (activeCount > 0)
            {
                _hasEverStarted = true;
                HasStatus = true;
                StatusText = activeCount == 1 ? "Bot đang chạy" : $"Bot đang chạy ({activeCount})";
                StatusColor = "#22c55e";
                StatusBorderColor = "#8022c55e";
                IsAnyRunning = true;
            }
            else if (paused > 0)
            {
                _hasEverStarted = true;
                HasStatus = true;
                StatusText = "Đang tạm dừng";
                StatusColor = "#f59e0b";
                StatusBorderColor = "#80f59e0b";
                IsAnyRunning = true;
            }
            else if (error > 0)
            {
                HasStatus = true;
                StatusText = "Có lỗi xảy ra";
                StatusColor = "#ef4444";
                StatusBorderColor = "#80ef4444";
                IsAnyRunning = false;
            }
            else if (_hasEverStarted || stopped > 0)
            {
                HasStatus = true;
                StatusText = "Bot đã dừng";
                StatusColor = "#ef4444";
                StatusBorderColor = "#80ef4444";
                IsAnyRunning = false;
            }
            else
            {
                HasStatus = false;
                StatusText = string.Empty;
                StatusColor = "#ef4444";
                StatusBorderColor = "#80ef4444";
                IsAnyRunning = false;
            }

            RunningCount = activeCount;
            PausedCount = paused;
            ErrorCount = error;
            WindowTitle = HasStatus ? $"SimpliMixi Console - {StatusText}" : "SimpliMixi Console";
        }

        /// <summary>Rolls up loot + error counts across all device panels for the fleet summary.</summary>
        public void RefreshAggregate(System.Collections.Generic.IEnumerable<DeviceViewModel> devices)
        {
            int errors = 0, battles = 0, stars = 0;
            long gold = 0, elixir = 0, dark = 0;
            foreach (DeviceViewModel d in devices)
            {
                if (d.Status is BotStatus.Error) errors++;
                battles += d.Stats.Battles;
                stars += d.Stats.Stars;
                gold += d.Stats.Gold;
                elixir += d.Stats.Elixir;
                dark += d.Stats.DarkElixir;
            }

            ErrorCount = errors;
            TotalBattles = battles;
            TotalStars = stars;
            TotalGold = gold;
            TotalElixir = elixir;
            TotalDarkElixir = dark;
        }
    }
}
