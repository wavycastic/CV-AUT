using System.Text.Json.Nodes;
using CommunityToolkit.Mvvm.ComponentModel;
using CvAut.Services.Configuration;

namespace CvAut.ViewModels.Settings
{
    public partial class NightVillageViewModel : ViewModelBase
    {
        private readonly IConfigStore _configStore;
        private JsonObject _config;

        [ObservableProperty] private string _title = "Làng đêm";
        [ObservableProperty] private string _farmMode = "trophy";
        [ObservableProperty] private bool _boostClockTower;
        [ObservableProperty] private bool _upgradeWall;
        [ObservableProperty] private string _armyFormation = "auto";
        
        [ObservableProperty] private bool _customDropOrderEnabled;
        [ObservableProperty] private string _dropOrder = "BattleMachine|BattleCopter|BoxerGiant|DropShip|HogGlider|Bomber|SuperPekka|PowerPekka|BabyDragon|CannonCart|ElectrofireWizard|NightWitch|RagedBarbarian|BetaMinion|SneakyArcher";
        [ObservableProperty] private int _nextTroopDelayMs = 600;
        [ObservableProperty] private int _sameTroopDelayMs = 180;
        [ObservableProperty] private bool _handleBomber = true;
        
        
        [ObservableProperty] private bool _suggestedUpgrades;
        [ObservableProperty] private bool _starLaboratory;
        [ObservableProperty] private bool _upgradeBattleMachine;
        [ObservableProperty] private bool _upgradeBattleCopter;
        
        // Suggested upgrades sub-options
        [ObservableProperty] private bool _placeNewBuildings;
        [ObservableProperty] private bool _ignoreGoldUpgrades;
        [ObservableProperty] private bool _ignoreElixirUpgrades;
        [ObservableProperty] private bool _ignoreHallUpgrades = true;
        [ObservableProperty] private bool _ignoreWallUpgrades = true;
        // Star Laboratory troop
        [ObservableProperty] private string _starLaboratoryTroop = "auto";
        // Trophy range filter
        [ObservableProperty] private int _minCups;
        [ObservableProperty] private int _maxCups = 5000;
        // Halt conditions
        [ObservableProperty] private bool _haltOnGoldFull;
        [ObservableProperty] private bool _haltOnElixirFull;
        // Clan Games
        [ObservableProperty] private bool _forceAttackForClanGames;

        public FarmModeOption[] FarmModeOptions { get; } =
        {
            new("trophy", "Cày cúp / Giữ cúp (Trophy Push)"),
            new("drop_trophy", "Hạ cúp (Drop Trophy)")
        };

        [ObservableProperty] private FarmModeOption _selectedFarmMode = default!;

        partial void OnFarmModeChanged(string value)
        {
            OnPropertyChanged(nameof(IsTrophyMode));
            OnPropertyChanged(nameof(IsDropTrophyMode));
        }

        partial void OnSelectedFarmModeChanged(FarmModeOption value)
        {
            if (value != null) FarmMode = value.Value;
            OnPropertyChanged(nameof(IsTrophyMode));
            OnPropertyChanged(nameof(IsDropTrophyMode));
        }

        public bool IsTrophyMode => string.Equals(FarmMode, "trophy", StringComparison.OrdinalIgnoreCase);
        public bool IsDropTrophyMode => string.Equals(FarmMode, "drop_trophy", StringComparison.OrdinalIgnoreCase);
        public string[] ArmyFormationOptions { get; } =
        {
            "auto",
            "power_pekka",
            "baby_dragon",
            "cannon_cart",
            "night_witch",
            "raged_barbarian",
            "sneaky_archer",
            "boxer_giant",
            "beta_minion",
            "bomber",
            "drop_ship",
            "hog_glider",
            "electrofire_wizard"
        };

        public string[] StarLaboratoryTroopOptions { get; } =
        {
            "auto",
            "raged_barbarian",
            "sneaky_archer",
            "boxer_giant",
            "beta_minion",
            "bomber",
            "baby_dragon",
            "cannon_cart",
            "night_witch",
            "drop_ship",
            "super_pekka",
            "hog_glider",
            "electrofire_wizard"
        };

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
            night["farm_mode"] = string.IsNullOrWhiteSpace(FarmMode) ? "trophy" : FarmMode;
            night["enable_attack"] = true;
            night["boost_clock_tower"] = BoostClockTower;
            night["upgrade_wall"] = UpgradeWall;
            night["army_formation"] = string.IsNullOrWhiteSpace(ArmyFormation) ? "auto" : ArmyFormation;
            
            night["custom_drop_order_enabled"] = CustomDropOrderEnabled;
            night["drop_order"] = string.IsNullOrWhiteSpace(DropOrder) ? "BattleMachine|BattleCopter|BoxerGiant|DropShip|HogGlider|Bomber|SuperPekka|PowerPekka|BabyDragon|CannonCart|ElectrofireWizard|NightWitch|RagedBarbarian|BetaMinion|SneakyArcher" : DropOrder;
            night["next_troop_delay_ms"] = Math.Clamp(NextTroopDelayMs, 0, 10000);
            night["same_troop_delay_ms"] = Math.Clamp(SameTroopDelayMs, 50, 5000);
            night["handle_bomber"] = HandleBomber;
            
            
            night["suggested_upgrades"] = SuggestedUpgrades;
            night["place_new_buildings"] = PlaceNewBuildings;
            night["ignore_gold_upgrades"] = IgnoreGoldUpgrades;
            night["ignore_elixir_upgrades"] = IgnoreElixirUpgrades;
            night["ignore_hall_upgrades"] = IgnoreHallUpgrades;
            night["ignore_wall_upgrades"] = IgnoreWallUpgrades;
            night["star_laboratory"] = StarLaboratory;
            night["star_laboratory_troop"] = string.IsNullOrWhiteSpace(StarLaboratoryTroop) ? "auto" : StarLaboratoryTroop;
            night["upgrade_battle_machine"] = UpgradeBattleMachine;
            night["upgrade_battle_copter"] = UpgradeBattleCopter;
            
            night["trophy_range_enabled"] = true;
            night["min_cups"] = Math.Clamp(MinCups, 0, 10000);
            night["max_cups"] = Math.Clamp(MaxCups, 0, 10000);
            night["halt_on_gold_full"] = HaltOnGoldFull;
            night["halt_on_elixir_full"] = HaltOnElixirFull;
            night["force_attack_for_clan_games"] = ForceAttackForClanGames;
        }

        private void LoadFromConfig()
        {
            JsonNode? night = _config["night_village"];
            string modeKey = ConfigStore.TryGetString(night?["farm_mode"], "trophy");
            SelectedFarmMode = FarmModeOptions.FirstOrDefault(x => x.Value.Equals(modeKey, StringComparison.OrdinalIgnoreCase)) ?? FarmModeOptions[0];
            FarmMode = SelectedFarmMode.Value;
            BoostClockTower = ConfigStore.TryGetBool(night?["boost_clock_tower"], false);
            UpgradeWall = ConfigStore.TryGetBool(night?["upgrade_wall"], false);
            ArmyFormation = ConfigStore.TryGetString(night?["army_formation"], "auto");
            
            CustomDropOrderEnabled = ConfigStore.TryGetBool(night?["custom_drop_order_enabled"], false);
            DropOrder = ConfigStore.TryGetString(night?["drop_order"], "BattleMachine|BattleCopter|BoxerGiant|DropShip|HogGlider|Bomber|SuperPekka|PowerPekka|BabyDragon|CannonCart|ElectrofireWizard|NightWitch|RagedBarbarian|BetaMinion|SneakyArcher");
            NextTroopDelayMs = Math.Clamp(ConfigStore.TryGetInt(night?["next_troop_delay_ms"], 600), 0, 10000);
            SameTroopDelayMs = Math.Clamp(ConfigStore.TryGetInt(night?["same_troop_delay_ms"], 180), 50, 5000);
            HandleBomber = ConfigStore.TryGetBool(night?["handle_bomber"], true);
            
            
            SuggestedUpgrades = ConfigStore.TryGetBool(night?["suggested_upgrades"], false);
            PlaceNewBuildings = ConfigStore.TryGetBool(night?["place_new_buildings"], false);
            IgnoreGoldUpgrades = ConfigStore.TryGetBool(night?["ignore_gold_upgrades"], false);
            IgnoreElixirUpgrades = ConfigStore.TryGetBool(night?["ignore_elixir_upgrades"], false);
            IgnoreHallUpgrades = ConfigStore.TryGetBool(night?["ignore_hall_upgrades"], true);
            IgnoreWallUpgrades = ConfigStore.TryGetBool(night?["ignore_wall_upgrades"], true);
            StarLaboratory = ConfigStore.TryGetBool(night?["star_laboratory"], false);
            StarLaboratoryTroop = ConfigStore.TryGetString(night?["star_laboratory_troop"], "auto");
            UpgradeBattleMachine = ConfigStore.TryGetBool(night?["upgrade_battle_machine"], false);
            UpgradeBattleCopter = ConfigStore.TryGetBool(night?["upgrade_battle_copter"], false);
            
            MinCups = Math.Clamp(ConfigStore.TryGetInt(night?["min_cups"], 0), 0, 10000);
            MaxCups = Math.Clamp(ConfigStore.TryGetInt(night?["max_cups"], 5000), 0, 10000);
            HaltOnGoldFull = ConfigStore.TryGetBool(night?["halt_on_gold_full"], false);
            HaltOnElixirFull = ConfigStore.TryGetBool(night?["halt_on_elixir_full"], false);
            ForceAttackForClanGames = ConfigStore.TryGetBool(night?["force_attack_for_clan_games"], false);
        }
    }

    public record FarmModeOption(string Value, string Label)
    {
        public override string ToString() => Label;
    }
}
