using System.Text.Json.Nodes;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace CvAut.ViewModels.Settings
{
    public partial class NightVillageViewModel : ViewModelBase
    {
        private readonly IConfigStore _configStore;
        private JsonObject _config;

        [ObservableProperty] private string _title = "Làng đêm";
        [ObservableProperty] private string _saveStatus = "Đã tải";
        [ObservableProperty] private string _farmMode = "auto";
        [ObservableProperty] private int _minCups;
        [ObservableProperty] private int _maxCups = 5000;
        [ObservableProperty] private bool _upgradeWall;

        public string[] FarmModes { get; } = { "Chỉ dầu", "Vàng và dầu", "Tự động" };

        public NightVillageViewModel(IConfigStore configStore)
        {
            _configStore = configStore;
            _config = _configStore.LoadActiveConfig();
            LoadFromConfig();
        }

        public NightVillageViewModel() : this(new ConfigStore())
        {
        }

        public void Reload()
        {
            _config = _configStore.LoadActiveConfig();
            LoadFromConfig();
        }

        public void ApplyTo(JsonObject config)
        {
            JsonObject night = ConfigStore.GetOrCreateObject(config, "night_village");
            night["farm_mode"] = FarmMode switch
            {
                "Chỉ dầu" => "dark_only",
                "Vàng và dầu" => "gold_dark",
                _ => "auto"
            };
            night["min_cups"] = MinCups;
            night["max_cups"] = MaxCups;
            night["upgrade_wall"] = UpgradeWall;
        }

        [RelayCommand]
        private void Save()
        {
            ApplyTo(_config);
            _configStore.SaveActiveConfig(_config);
            SaveStatus = "Đã lưu cài đặt Làng đêm";
        }

        private void LoadFromConfig()
        {
            JsonNode? night = _config["night_village"];
            string mode = ConfigStore.TryGetString(night?["farm_mode"], "auto");
            FarmMode = mode switch
            {
                "dark_only" => "Chỉ dầu",
                "gold_dark" => "Vàng và dầu",
                _ => "Tự động"
            };
            MinCups = ConfigStore.TryGetInt(night?["min_cups"], 0);
            MaxCups = ConfigStore.TryGetInt(night?["max_cups"], 5000);
            UpgradeWall = ConfigStore.TryGetBool(night?["upgrade_wall"], false);
        }
    }
}
