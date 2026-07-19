using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CvAut.Models;
using CvAut.ViewModels.Settings;

namespace CvAut.ViewModels
{
    public partial class SettingsViewModel : ViewModelBase
    {
        private readonly IConfigStore _configStore;
        private readonly MainVillageViewModel _mainVillage;
        private readonly NightVillageViewModel _nightVillage;
        private readonly ClanGamesViewModel _clanGames;
        private readonly ClanCapitalViewModel _clanCapital;
        private bool _syncingProfiles;

        [ObservableProperty] private string _title = "Cài đặt";
        [ObservableProperty] private string _profileName = "Default";
        [ObservableProperty] private string _status = "Đã tải cấu hình";
        [ObservableProperty] private BotProfile? _selectedProfile;
        [ObservableProperty] private SettingsTab? _selectedTab;

        // Opt-in notifications (Discord webhook). Disabled by default; URL is user-supplied.
        [ObservableProperty] private bool _notifyEnabled;
        [ObservableProperty] private string _webhookUrl = string.Empty;
        [ObservableProperty] private bool _notifyOnError = true;
        [ObservableProperty] private bool _notifyOnStopped;
        [ObservableProperty] private bool _notifyOnStarted;
        [ObservableProperty] private string _notifyStatus = string.Empty;

        public ObservableCollection<SettingsTab> Tabs { get; } = new();
        public ObservableCollection<BotProfile> Profiles { get; } = new();
        public ObservableCollection<string> PlayModes { get; } = new()
        {
            PlayMode.MainVillageLabel,
            PlayMode.NightVillageLabel,
            PlayMode.ClanGamesLabel,
            PlayMode.ClanCapitalLabel
        };

        [ObservableProperty]
        private string _selectedPlayMode = PlayMode.MainVillageLabel;

        public bool HasTabs => Tabs.Count > 0;

        public SettingsViewModel(
            MainVillageViewModel mainVillage,
            NightVillageViewModel nightVillage,
            ClanGamesViewModel clanGames,
            ClanCapitalViewModel clanCapital,
            IConfigStore configStore)
        {
            _mainVillage = mainVillage;
            _nightVillage = nightVillage;
            _clanGames = clanGames;
            _clanCapital = clanCapital;
            _configStore = configStore;

            Tabs.Add(new SettingsTab("Làng chính", "Home", mainVillage));
            Tabs.Add(new SettingsTab("Làng đêm", "MoonWaningCrescent", nightVillage));
            Tabs.Add(new SettingsTab("Trò chơi hội", "SwordCross", clanGames));
            Tabs.Add(new SettingsTab("Kinh đô hội", "HomeModern", clanCapital));
            SelectedTab = Tabs[0];
            OnPropertyChanged(nameof(HasTabs));
            RefreshProfiles();
            LoadNotificationSettings();
        }

        public SettingsViewModel() : this(new MainVillageViewModel(), new NightVillageViewModel(), new ClanGamesViewModel(), new ClanCapitalViewModel(), new ConfigStore())
        {
        }

        partial void OnSelectedPlayModeChanged(string value)
        {
            if (!IsInstanceMode)
            {
                return;
            }

            RebuildInstanceModeTabs(value);
        }

        private void RebuildInstanceModeTabs(string playMode)
        {
            Tabs.Clear();
            SettingsTab selectedModeTab = CreateTabForPlayMode(playMode);
            Tabs.Add(selectedModeTab);
            SelectedTab = selectedModeTab;
            OnPropertyChanged(nameof(HasTabs));
        }

        [RelayCommand]
        private void LoadProfile()
        {
            LoadSelectedProfile();
        }

        partial void OnSelectedProfileChanged(BotProfile? value)
        {
            if (!_syncingProfiles && value is not null && value.Name != _configStore.ActiveProfileName)
            {
                LoadSelectedProfile();
            }
        }

        private void LoadSelectedProfile()
        {
            if (SelectedProfile is null)
            {
                return;
            }

            _configStore.LoadProfile(SelectedProfile.Name);
            ProfileName = _configStore.ActiveProfileName;
            _mainVillage.Reload();
            _nightVillage.Reload();
            _clanGames.Reload();
            _clanCapital.Reload();
            RefreshProfiles();
            Status = "Đã tải cấu hình " + ProfileName;
        }

        [RelayCommand]
        private void SaveNewProfile()
        {
            var config = _configStore.LoadActiveConfig();
            _mainVillage.ApplyTo(config);
            _nightVillage.ApplyTo(config);
            _clanGames.ApplyTo(config);
            _clanCapital.ApplyTo(config);
            _configStore.SaveProfileAs(ProfileName, config);
            RefreshProfiles();
            Status = "Đã lưu cấu hình " + _configStore.ActiveProfileName;
        }

        [RelayCommand]
        private void UpdateProfile()
        {
            var config = _configStore.LoadActiveConfig();
            _mainVillage.ApplyTo(config);
            _nightVillage.ApplyTo(config);
            _clanGames.ApplyTo(config);
            _clanCapital.ApplyTo(config);
            _configStore.SaveActiveConfig(config);
            RefreshProfiles();
            Status = "Đã cập nhật cấu hình " + _configStore.ActiveProfileName;
        }

        [RelayCommand]
        private void DeleteProfile()
        {
            if (SelectedProfile is null)
            {
                return;
            }

            string deletedName = SelectedProfile.Name;
            _configStore.DeleteProfile(deletedName);
            RefreshProfiles();
            _mainVillage.Reload();
            _nightVillage.Reload();
            _clanGames.Reload();
            _clanCapital.Reload();
            Status = "Đã xóa cấu hình " + deletedName;
        }

        private void LoadNotificationSettings()
        {
            var s = _configStore.LoadNotificationSettings();
            NotifyEnabled = s.Enabled;
            WebhookUrl = s.WebhookUrl;
            NotifyOnError = s.NotifyOnError;
            NotifyOnStopped = s.NotifyOnStopped;
            NotifyOnStarted = s.NotifyOnStarted;
        }

        [RelayCommand]
        private void SaveNotifications()
        {
            var s = new Models.NotificationSettings
            {
                Enabled = NotifyEnabled,
                WebhookUrl = (WebhookUrl ?? string.Empty).Trim(),
                NotifyOnError = NotifyOnError,
                NotifyOnStopped = NotifyOnStopped,
                NotifyOnStarted = NotifyOnStarted,
            };
            _configStore.SaveNotificationSettings(s);
            NotifyStatus = s.IsActionable ? "Đã lưu — thông báo bật." : (s.Enabled ? "Đã lưu — cần URL webhook https hợp lệ." : "Đã lưu — thông báo tắt.");
        }

        private void RefreshProfiles()
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
                        ProfileName = profile.Name;
                    }
                }
            }
            finally
            {
                _syncingProfiles = false;
            }
        }

        [ObservableProperty]
        private bool _isInstanceMode;

        /// <summary>
        /// Raised when the instance-mode Save/Cancel buttons are pressed. The dialog renders this
        /// SettingsViewModel directly (inside MainWindow's overlay, NOT under DashboardView), so
        /// the buttons must bind to commands on THIS view model. DashboardViewModel wires these
        /// events to its own save/cancel flow.
        /// </summary>
        public event System.Action? InstanceSaveRequested;
        public event System.Action? InstanceCancelRequested;

        [RelayCommand]
        private void InstanceSave() => InstanceSaveRequested?.Invoke();

        [RelayCommand]
        private void InstanceCancel() => InstanceCancelRequested?.Invoke();

        public void LoadSelectedProfileDirectly(string name, string playMode = "")
        {
            ProfileName = name;
            _mainVillage.Reload();
            _nightVillage.Reload();
            _clanGames.Reload();
            _clanCapital.Reload();
            RefreshProfiles();

            SelectedPlayMode = PlayMode.ToDisplay(playMode);
            RebuildInstanceModeTabs(SelectedPlayMode);

            Status = "Đã tải cấu hình " + name;
        }

        private SettingsTab CreateTabForPlayMode(string playMode)
        {
            string token = Models.PlayMode.ToToken(playMode);
            return token switch
            {
                "night_village" => new SettingsTab("Làng đêm", "MoonWaningCrescent", _nightVillage),
                "clan_games" => new SettingsTab("Trò chơi hội", "SwordCross", _clanGames),
                "clan_capital" => new SettingsTab("Kinh đô hội", "HomeModern", _clanCapital),
                _ => new SettingsTab("Làng chính", "Home", _mainVillage)
            };
        }

        public void UpdateProfileDirectly()
        {
            var config = _configStore.LoadActiveConfig();
            config["play_mode"] = PlayMode.ToToken(SelectedPlayMode);
            _mainVillage.ApplyTo(config);
            _nightVillage.ApplyTo(config);
            _clanGames.ApplyTo(config);
            _clanCapital.ApplyTo(config);
            _configStore.SaveActiveConfig(config);
            RefreshProfiles();
            Status = "Đã cập nhật cấu hình " + _configStore.ActiveProfileName;
        }
    }

    public sealed partial class SettingsTab : ObservableObject
    {
        public string Title { get; }
        public string IconKind { get; }
        public ViewModelBase Page { get; }

        public SettingsTab(string title, string iconKind, ViewModelBase page)
        {
            Title = title;
            IconKind = iconKind;
            Page = page;
        }
    }
}
