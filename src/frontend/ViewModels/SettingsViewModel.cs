using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CvAut.Models;
using CvAut.ViewModels.Settings;

namespace CvAut.ViewModels
{
    /// <summary>
    /// Settings page view model. This file covers construction and the tab strip, whose contents
    /// depend on the selected play mode. The rest is split by feature:
    /// <c>SettingsViewModel.Profiles.cs</c> (profile CRUD and config sync),
    /// <c>SettingsViewModel.Notifications.cs</c> (Discord webhook form) and
    /// <c>SettingsViewModel.InstanceMode.cs</c> (per-device dialog contract).
    /// </summary>
    public partial class SettingsViewModel : ViewModelBase
    {
        private readonly IConfigStore _configStore;
        private readonly MainVillageViewModel _mainVillage;
        private readonly NightVillageViewModel _nightVillage;
        private readonly ClanGamesViewModel _clanGames;
        private readonly ClanCapitalViewModel _clanCapital;

        [ObservableProperty] private string _title = "Cài đặt";
        [ObservableProperty] private string _profileName = "Default";
        [ObservableProperty] private string _status = "Đã tải cấu hình";
        [ObservableProperty] private BotProfile? _selectedProfile;
        [ObservableProperty] private SettingsTab? _selectedTab;

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
            SyncPlayModeFromConfig();
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

            RebuildTabsByPlayMode(value);
        }

        private void RebuildTabsByPlayMode(string playMode)
        {
            Tabs.Clear();
            SettingsTab selectedModeTab = CreateTabForPlayMode(playMode);
            Tabs.Add(selectedModeTab);
            SelectedTab = selectedModeTab;
            OnPropertyChanged(nameof(HasTabs));
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
    }
}
