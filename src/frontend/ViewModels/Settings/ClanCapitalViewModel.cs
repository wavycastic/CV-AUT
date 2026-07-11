using System.Collections.ObjectModel;
using System.Text.Json.Nodes;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace CvAut.ViewModels.Settings
{
    public partial class ClanCapitalViewModel : ViewModelBase
    {
        private readonly IConfigStore _configStore;
        private JsonObject _config;

        [ObservableProperty] private string _title = "Kinh đô hội";
        [ObservableProperty] private bool _enabled = true;
        [ObservableProperty] private string _selectedAttackMode = "Tự động";

        public ObservableCollection<string> AttackModes { get; } = new() { "Tự động", "Bỏ qua" };

        public ClanCapitalViewModel(IConfigStore configStore)
        {
            _configStore = configStore;
            _config = _configStore.LoadActiveConfig();
            LoadFromConfig();
        }

        public ClanCapitalViewModel() : this(new ConfigStore())
        {
        }

        public void Reload()
        {
            _config = _configStore.LoadActiveConfig();
            LoadFromConfig();
        }

        public void ApplyTo(JsonObject config)
        {
            JsonObject capital = ConfigStore.GetOrCreateObject(config, "clan_capital");
            capital["enabled"] = Enabled;
            capital["attack_mode"] = SelectedAttackMode switch
            {
                "Bỏ qua" => "skip",
                _ => "auto"
            };
        }

        private void LoadFromConfig()
        {
            JsonNode? capital = _config["clan_capital"];
            Enabled = ConfigStore.TryGetBool(capital?["enabled"], true);
            string mode = ConfigStore.TryGetString(capital?["attack_mode"], "auto");
            SelectedAttackMode = mode switch
            {
                "skip" => "Bỏ qua",
                _ => "Tự động"
            };
        }
    }
}
