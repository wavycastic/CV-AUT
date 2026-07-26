using System.Collections.ObjectModel;
using System.Text.Json.Nodes;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CvAut.Services.Configuration;

namespace CvAut.ViewModels.Settings
{
    public partial class ClanGamesViewModel : ViewModelBase
    {
        private readonly IConfigStore _configStore;
        private JsonObject _config;

        [ObservableProperty] private string _title = "Trò chơi Clan";
        [ObservableProperty] private string _saveStatus = "Đã tải";
        [ObservableProperty] private string _selectedVillage = "Làng chính";
        [ObservableProperty] private string _missionFilter = "resources,walls,stars";
        [ObservableProperty] private string _filterSetName = "Default";

        public ObservableCollection<string> Villages { get; } = new() { "Làng chính", "Làng đêm" };
        public ObservableCollection<string> SavedFilterSets { get; } = new() { "Default" };

        public ClanGamesViewModel(IConfigStore configStore)
        {
            _configStore = configStore;
            _config = _configStore.LoadActiveConfig();
            LoadFromConfig();
        }

        public ClanGamesViewModel() : this(new ConfigStore())
        {
        }

        public void Reload()
        {
            _config = _configStore.LoadActiveConfig();
            LoadFromConfig();
        }

        public void ApplyTo(JsonObject config)
        {
            JsonObject clanGames = ConfigStore.GetOrCreateObject(config, "clan_games");
            clanGames["village"] = SelectedVillage switch
            {
                "Làng đêm" => "night_village",
                _ => "main_village"
            };
            clanGames["mission_filter"] = MissionFilter;
            clanGames["filter_set_name"] = FilterSetName;
        }

        [RelayCommand]
        private void SaveFilterSet()
        {
            if (!SavedFilterSets.Contains(FilterSetName))
            {
                SavedFilterSets.Add(FilterSetName);
            }

            Save();
        }

        [RelayCommand]
        private void Save()
        {
            ApplyTo(_config);
            _configStore.SaveActiveConfig(_config);
            SaveStatus = "Đã lưu cài đặt Trò chơi Clan";
        }

        private void LoadFromConfig()
        {
            JsonNode? clanGames = _config["clan_games"];
            string village = ConfigStore.TryGetString(clanGames?["village"], "main_village");
            SelectedVillage = village switch
            {
                "night_village" => "Làng đêm",
                _ => "Làng chính"
            };
            MissionFilter = ConfigStore.TryGetString(clanGames?["mission_filter"], "resources,walls,stars");
            FilterSetName = ConfigStore.TryGetString(clanGames?["filter_set_name"], "Default");
            if (!SavedFilterSets.Contains(FilterSetName))
            {
                SavedFilterSets.Add(FilterSetName);
            }
        }
    }
}
