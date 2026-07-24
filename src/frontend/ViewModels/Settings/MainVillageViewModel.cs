using System.Collections.ObjectModel;
using System.Text.Json.Nodes;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace CvAut.ViewModels.Settings
{
    public partial class MainVillageViewModel : ViewModelBase
    {
        private readonly IConfigStore _configStore;
        private JsonObject _config;
        private bool _isUpdating;

        [ObservableProperty] private string _title = "Làng chính";
        [ObservableProperty] private string _saveStatus = "Đã tải";
        [ObservableProperty] private string _attackName = "Rồng lửa + Balloon";
        [ObservableProperty] private bool _attackEnabled = true;
        [ObservableProperty] private bool _autoDonateEnabled;
        [ObservableProperty] private bool _useDragon = true;
        [ObservableProperty] private bool _useElectroDragon;
        [ObservableProperty] private int _goldThreshold;
        [ObservableProperty] private int _elixirThreshold;
        [ObservableProperty] private int _darkElixirThreshold;
        [ObservableProperty] private int _totalResourceThreshold;
        [ObservableProperty] private string _targetLogic = "total";
        [ObservableProperty] private bool _requestTroops;
        
        [ObservableProperty] private bool _upgradeWall;
        [ObservableProperty] private int _wallBatchLimit = 1;
        [ObservableProperty] private int _wallGoldThreshold;
        [ObservableProperty] private int _wallElixirThreshold;
        [ObservableProperty] private int _wallGoldReserve;
        [ObservableProperty] private int _wallElixirReserve;
        [ObservableProperty] private bool _smartSurrenderEnabled;
        [ObservableProperty] private int _smartSurrenderSeconds;
        [ObservableProperty] private int _smartSurrenderLowResourceThreshold;

        public ObservableCollection<string> TargetLogics { get; } = new() { "Tổng tài nguyên", "Tất cả điều kiện", "Một trong các điều kiện" };
        public ObservableCollection<string> AttackCatalog { get; } = new() { "Rồng lửa + Balloon", "Rồng điện + Balloon" };

        public MainVillageViewModel(IConfigStore configStore)
        {
            _configStore = configStore;
            _config = _configStore.LoadActiveConfig();
            
            LoadFromConfig();
        }

        public MainVillageViewModel() : this(new ConfigStore())
        {
        }

        public void Reload()
        {
            _config = _configStore.LoadActiveConfig();
            LoadFromConfig();
        }

        public void ApplyTo(JsonObject config)
        {
            JsonObject thresholds = ConfigStore.GetOrCreateObject(config, "farming_thresholds");
            thresholds["gold_threshold"] = GoldThreshold;
            thresholds["elixir_threshold"] = ElixirThreshold;
            thresholds["dark_elixir_threshold"] = DarkElixirThreshold;
            thresholds["total_resource_threshold"] = TotalResourceThreshold;
            thresholds["target_logic"] = TargetLogic switch
            {
                "Tất cả điều kiện" => "and",
                "Một trong các điều kiện" => "or",
                _ => "total"
            };

            config["attack"] = AttackName switch
            {
                "Rồng điện + Balloon" => "ElectroDragon_Attack",
                _ => "Dragon_Attack"
            };
            config["attack_mode"] = AttackEnabled ? "attack" : "donate_only";
            config["auto_donate"] = AutoDonateEnabled;
            config["use_dragon"] = UseDragon;
            config["use_electro_dragon"] = UseElectroDragon;
            config["request_troops"] = RequestTroops;
            
            config["upgrade_wall"] = UpgradeWall;
            config["wall_batch_limit"] = WallBatchLimit;
            config["wall_gold_threshold"] = WallGoldThreshold;
            config["wall_elixir_threshold"] = WallElixirThreshold;
            config["wall_gold_reserve"] = WallGoldReserve;
            config["wall_elixir_reserve"] = WallElixirReserve;

            JsonObject smart = ConfigStore.GetOrCreateObject(config, "smart_surrender");
            smart["enabled"] = SmartSurrenderEnabled;
            smart["after_seconds_enabled"] = SmartSurrenderEnabled;
            smart["after_seconds"] = SmartSurrenderSeconds;
            smart["low_resources_enabled"] = SmartSurrenderLowResourceThreshold > 0;
            smart["low_resources_threshold"] = SmartSurrenderLowResourceThreshold;
        }

        [RelayCommand]
        private void Save()
        {
            ApplyTo(_config);
            _configStore.SaveActiveConfig(_config);
            SaveStatus = "Đã lưu cài đặt Làng chính";
        }

        private void LoadFromConfig()
        {
            _isUpdating = true;
            try
            {
                JsonNode? thresholds = _config["farming_thresholds"];
                JsonNode? smart = _config["smart_surrender"];
                string rawAttack = ConfigStore.TryGetString(_config["attack"], "Dragon_Attack");
                AttackName = rawAttack switch
                {
                    "ElectroDragon_Attack" => "Rồng điện + Balloon",
                    _ => "Rồng lửa + Balloon"
                };
                AttackEnabled = ConfigStore.TryGetString(_config["attack_mode"], "attack") != "donate_only";
                AutoDonateEnabled = ConfigStore.TryGetBool(_config["auto_donate"], false);
                UseDragon = ConfigStore.TryGetBool(_config["use_dragon"], true);
                UseElectroDragon = ConfigStore.TryGetBool(_config["use_electro_dragon"], false);
                GoldThreshold = ConfigStore.TryGetInt(thresholds?["gold_threshold"], 650000);
                ElixirThreshold = ConfigStore.TryGetInt(thresholds?["elixir_threshold"], 650000);
                DarkElixirThreshold = ConfigStore.TryGetInt(thresholds?["dark_elixir_threshold"], 1000);
                TotalResourceThreshold = ConfigStore.TryGetInt(thresholds?["total_resource_threshold"], 1300000);
                string logic = ConfigStore.TryGetString(thresholds?["target_logic"], "total");
                TargetLogic = logic switch
                {
                    "and" => "Tất cả điều kiện",
                    "or" => "Một trong các điều kiện",
                    _ => "Tổng tài nguyên"
                };
                RequestTroops = ConfigStore.TryGetBool(_config["request_troops"], false);
                
                UpgradeWall = ConfigStore.TryGetBool(_config["upgrade_wall"], false);
                WallBatchLimit = ConfigStore.TryGetInt(_config["wall_batch_limit"], 1);
                WallGoldThreshold = ConfigStore.TryGetInt(_config["wall_gold_threshold"], 5000000);
                WallElixirThreshold = ConfigStore.TryGetInt(_config["wall_elixir_threshold"], 5000000);
                WallGoldReserve = ConfigStore.TryGetInt(_config["wall_gold_reserve"], 100000);
                WallElixirReserve = ConfigStore.TryGetInt(_config["wall_elixir_reserve"], 0);
                SmartSurrenderEnabled = ConfigStore.TryGetBool(smart?["enabled"], false);
                SmartSurrenderSeconds = ConfigStore.TryGetInt(smart?["after_seconds"], 60);
                SmartSurrenderLowResourceThreshold = ConfigStore.TryGetInt(smart?["low_resources_threshold"], 100000);
            }
            finally
            {
                _isUpdating = false;
            }
        }

        partial void OnUseDragonChanged(bool value)
        {
            if (_isUpdating) return;
            if (value)
            {
                _isUpdating = true;
                UseElectroDragon = false;
                AttackName = "Rồng lửa + Balloon";
                _isUpdating = false;
            }
        }

        partial void OnUseElectroDragonChanged(bool value)
        {
            if (_isUpdating) return;
            if (value)
            {
                _isUpdating = true;
                UseDragon = false;
                AttackName = "Rồng điện + Balloon";
                _isUpdating = false;
            }
        }

        partial void OnAttackNameChanged(string value)
        {
            if (_isUpdating) return;
            if (value == "Rồng lửa + Balloon")
            {
                _isUpdating = true;
                UseDragon = true;
                UseElectroDragon = false;
                _isUpdating = false;
            }
            else if (value == "Rồng điện + Balloon")
            {
                _isUpdating = true;
                UseDragon = false;
                UseElectroDragon = true;
                _isUpdating = false;
            }
        }
    }
}
