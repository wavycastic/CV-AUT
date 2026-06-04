using System;
using System.Collections.Generic;
using System.Text.Json.Nodes;
using CvAut.WpfApp.Services;

namespace CvAut.WpfApp.ViewModels
{
    public class ArmyViewModel : ViewModelBase
    {
        private readonly IBotService _botService;
        private string _selectedAttackStrategy = "Dragon";
        private string _attackPreview = "DRAGON";
        private string _attackDescription = "DRAGON ATTACK";
        private string _attackImagePath = "pack://siteoforigin:,,,/Templates/attacks/Dragon_attack.png";
        private string _attackComposition = "13 Dragon / 15 Balloons / 4 Rage / 3 Freeze";
        private string _attackSupport = "Any Siege / All heroes / CC rage + freeze";
        private bool _isSmartTrain = true;
        private bool _isQuickTrain;
        private int _quickSlot = 1;

        public List<string> AttackStrategies { get; } = new() { "Dragon", "Electro Dragon" };

        public string SelectedAttackStrategy
        {
            get => _selectedAttackStrategy;
            set
            {
                if (SetProperty(ref _selectedAttackStrategy, value))
                {
                    UpdateAttackStrategyInfo();
                }
            }
        }

        public string AttackPreview
        {
            get => _attackPreview;
            set => SetProperty(ref _attackPreview, value);
        }

        public string AttackDescription
        {
            get => _attackDescription;
            set => SetProperty(ref _attackDescription, value);
        }

        public string AttackImagePath
        {
            get => _attackImagePath;
            set => SetProperty(ref _attackImagePath, value);
        }

        public string AttackComposition
        {
            get => _attackComposition;
            set => SetProperty(ref _attackComposition, value);
        }

        public string AttackSupport
        {
            get => _attackSupport;
            set => SetProperty(ref _attackSupport, value);
        }

        public bool IsSmartTrain
        {
            get => _isSmartTrain;
            set
            {
                if (SetProperty(ref _isSmartTrain, value))
                {
                    IsQuickTrain = !value;
                }
            }
        }

        public bool IsQuickTrain
        {
            get => _isQuickTrain;
            set
            {
                if (SetProperty(ref _isQuickTrain, value))
                {
                    IsSmartTrain = !value;
                    OnPropertyChanged(nameof(IsQuickSlotEnabled));
                }
            }
        }

        public bool IsQuickSlotEnabled => IsQuickTrain;

        public int QuickSlot
        {
            get => _quickSlot;
            set => SetProperty(ref _quickSlot, value);
        }

        public ArmyViewModel(IBotService botService)
        {
            _botService = botService;
            LoadConfig();
        }

        public void LoadConfig()
        {
            try
            {
                var profile = _botService.LoadProfile(_botService.CurrentVillage);

                string attack = profile["attack"]?.ToString() ?? "Dragon_Attack";
                SelectedAttackStrategy = attack == "ElectroDragon_Attack" ? "Electro Dragon" : "Dragon";

                string trainMode = profile["train_mode"]?.ToString() ?? "smart";
                IsQuickTrain = trainMode == "quick";
                IsSmartTrain = trainMode != "quick";

                QuickSlot = GetInt(profile, "quick_slot", 1);
            }
            catch
            {
                // Fallbacks already set
            }
        }

        public void SaveConfig()
        {
            try
            {
                var root = _botService.LoadMainConfig();
                string attackKey = SelectedAttackStrategy == "Electro Dragon" ? "ElectroDragon_Attack" : "Dragon_Attack";
                string trainMode = IsQuickTrain ? "quick" : "smart";

                root["attack"] = attackKey;
                root["train_mode"] = trainMode;
                root["quick_slot"] = QuickSlot;
                _botService.SaveMainConfig(root);

                var profile = _botService.LoadProfile(_botService.CurrentVillage);
                profile["attack"] = attackKey;
                profile["train_mode"] = trainMode;
                profile["quick_slot"] = QuickSlot;
                _botService.SaveProfile(_botService.CurrentVillage, profile);
            }
            catch
            {
                // Ignore errors
            }
        }

        private void UpdateAttackStrategyInfo()
        {
            if (SelectedAttackStrategy == "Electro Dragon")
            {
                AttackPreview = "ELECTRO DRAGON";
                AttackDescription = "ELECTRO DRAGON ATTACK";
                AttackImagePath = "pack://siteoforigin:,,,/Templates/attacks/ElectroDragon_Attack.png";
                AttackComposition = "10 E-Drag / 15 Balloons / 4 Rage / 3 Freeze";
                AttackSupport = "Any Siege / All heroes / CC rage + freeze";
            }
            else
            {
                AttackPreview = "DRAGON";
                AttackDescription = "DRAGON ATTACK";
                AttackImagePath = "pack://siteoforigin:,,,/Templates/attacks/Dragon_attack.png";
                AttackComposition = "13 Dragon / 15 Balloons / 4 Rage / 3 Freeze";
                AttackSupport = "Any Siege / All heroes / CC rage + freeze";
            }
        }

        private static int GetInt(JsonObject obj, string key, int defaultValue)
        {
            if (obj.TryGetPropertyValue(key, out var val) && val != null)
            {
                try { return val.GetValue<int>(); } catch { }
            }
            return defaultValue;
        }
    }
}
