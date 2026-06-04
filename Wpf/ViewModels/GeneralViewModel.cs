using System;
using System.Text.Json.Nodes;
using CvAut.WpfApp.Services;

namespace CvAut.WpfApp.ViewModels
{
    public class GeneralViewModel : ViewModelBase
    {
        private readonly IBotService _botService;
        private string _goldThreshold = "650000";
        private string _elixirThreshold = "650000";
        private string _darkThreshold = "1000";

        private bool _upgradeWallEnabled = true;
        private string _wallGoldThreshold = "5000000";
        private string _wallElixirThreshold = "5000000";
        private int _wallLevel = 12;

        private string _adbHost = "127.0.0.1";
        private int _adbPort = 5556;
        private bool _requestTroopsEnabled;
        
        private string _activityText = "[00:00:00] INFO  Dashboard initialized\r\n[00:00:00] WAIT  Activity feed will mirror runtime logs\r\n[00:00:00] READY Configure resources and press START\r\n";

        // Properties
        public string GoldThreshold
        {
            get => _goldThreshold;
            set => SetProperty(ref _goldThreshold, value);
        }

        public string ElixirThreshold
        {
            get => _elixirThreshold;
            set => SetProperty(ref _elixirThreshold, value);
        }

        public string DarkThreshold
        {
            get => _darkThreshold;
            set => SetProperty(ref _darkThreshold, value);
        }

        public bool UpgradeWallEnabled
        {
            get => _upgradeWallEnabled;
            set
            {
                if (SetProperty(ref _upgradeWallEnabled, value))
                {
                    OnPropertyChanged(nameof(IsWallInputsEnabled));
                }
            }
        }

        public bool IsWallInputsEnabled => UpgradeWallEnabled;

        public string WallGoldThreshold
        {
            get => _wallGoldThreshold;
            set => SetProperty(ref _wallGoldThreshold, value);
        }

        public string WallElixirThreshold
        {
            get => _wallElixirThreshold;
            set => SetProperty(ref _wallElixirThreshold, value);
        }

        public int WallLevel
        {
            get => _wallLevel;
            set => SetProperty(ref _wallLevel, value);
        }

        public string AdbHost
        {
            get => _adbHost;
            set => SetProperty(ref _adbHost, value);
        }

        public int AdbPort
        {
            get => _adbPort;
            set => SetProperty(ref _adbPort, value);
        }

        public bool RequestTroopsEnabled
        {
            get => _requestTroopsEnabled;
            set => SetProperty(ref _requestTroopsEnabled, value);
        }

        public string ActivityText
        {
            get => _activityText;
            set => SetProperty(ref _activityText, value);
        }

        public System.Windows.Input.ICommand ClearActivityCommand { get; }

        public GeneralViewModel(IBotService botService)
        {
            _botService = botService;
            _botService.LogReceived += AppendActivityLog;
            LoadConfig();
            ClearActivityCommand = new RelayCommand(() => ActivityText = string.Empty);
        }

        public void LoadConfig()
        {
            try
            {
                var root = _botService.LoadMainConfig();
                
                // Connection
                if (root["device_connection"] is JsonObject device)
                {
                    AdbHost = device["host"]?.ToString() ?? "127.0.0.1";
                    AdbPort = device["port"]?.GetValue<int>() ?? 5556;
                }

                // Farming
                if (root["farming_thresholds"] is JsonObject farming)
                {
                    GoldThreshold = farming["gold_threshold"]?.ToString() ?? "650000";
                    ElixirThreshold = farming["elixir_threshold"]?.ToString() ?? "650000";
                    DarkThreshold = farming["dark_elixir_threshold"]?.ToString() ?? "1000";
                }
                else if (root["target_data_threshold"] is JsonObject target)
                {
                    GoldThreshold = target["gold"]?.ToString() ?? "650000";
                    ElixirThreshold = target["elixir"]?.ToString() ?? "650000";
                    DarkThreshold = target["dark_elixir"]?.ToString() ?? "1000";
                }

                // Profile-specific settings
                var profile = _botService.LoadProfile(_botService.CurrentVillage);
                UpgradeWallEnabled = GetBool(profile, "upgrade_wall", true);
                WallLevel = GetInt(profile, "wall_level", 12);
                WallGoldThreshold = profile["wall_gold_threshold"]?.ToString() ?? "5000000";
                WallElixirThreshold = profile["wall_elixir_threshold"]?.ToString() ?? "5000000";
                RequestTroopsEnabled = GetBool(profile, "request_troops", false);
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
                // Load existing main config first to merge
                var root = _botService.LoadMainConfig();

                root["device_connection"] = new JsonObject
                {
                    ["host"] = string.IsNullOrWhiteSpace(AdbHost) ? "127.0.0.1" : AdbHost.Trim(),
                    ["port"] = AdbPort
                };

                int gold = ParseInt(GoldThreshold, 650000);
                int elixir = ParseInt(ElixirThreshold, 650000);
                int dark = ParseInt(DarkThreshold, 1000);

                var thresholds = new JsonObject
                {
                    ["gold_threshold"] = gold,
                    ["elixir_threshold"] = elixir,
                    ["dark_elixir_threshold"] = dark
                };
                root["farming_thresholds"] = thresholds;

                root["target_data_threshold"] = new JsonObject
                {
                    ["gold"] = gold,
                    ["elixir"] = elixir,
                    ["dark_elixir"] = dark
                };

                root["upgrade_wall"] = UpgradeWallEnabled;
                root["wall_level"] = WallLevel;
                root["wall_gold_threshold"] = ParseInt(WallGoldThreshold, 5000000);
                root["wall_elixir_threshold"] = ParseInt(WallElixirThreshold, 5000000);
                root["request_troops"] = RequestTroopsEnabled;

                _botService.SaveMainConfig(root);

                // Save profile settings
                var profile = _botService.LoadProfile(_botService.CurrentVillage);
                profile["gold_threshold"] = gold;
                profile["elixir_threshold"] = elixir;
                profile["dark_elixir_threshold"] = dark;
                profile["upgrade_wall"] = UpgradeWallEnabled;
                profile["wall_level"] = WallLevel;
                profile["wall_gold_threshold"] = ParseInt(WallGoldThreshold, 5000000);
                profile["wall_elixir_threshold"] = ParseInt(WallElixirThreshold, 5000000);
                profile["request_troops"] = RequestTroopsEnabled;

                _botService.SaveProfile(_botService.CurrentVillage, profile);
            }
            catch
            {
                // Ignore errors
            }
        }

        private void AppendActivityLog(string message)
        {
            // Keep last 15 lines for the mini activity feed preview
            var lines = ActivityText.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None).ToList();
            lines.Add(message);
            if (lines.Count > 15)
            {
                lines.RemoveAt(0);
            }
            ActivityText = string.Join("\r\n", lines);
        }

        private static bool GetBool(JsonObject obj, string key, bool defaultValue)
        {
            if (obj.TryGetPropertyValue(key, out var val) && val != null)
            {
                try { return val.GetValue<bool>(); } catch { }
            }
            return defaultValue;
        }

        private static int GetInt(JsonObject obj, string key, int defaultValue)
        {
            if (obj.TryGetPropertyValue(key, out var val) && val != null)
            {
                try { return val.GetValue<int>(); } catch { }
            }
            return defaultValue;
        }

        private static int ParseInt(string text, int defaultValue)
        {
            if (int.TryParse(text.Replace(",", ""), out int val))
            {
                return val;
            }
            return defaultValue;
        }
    }
}
