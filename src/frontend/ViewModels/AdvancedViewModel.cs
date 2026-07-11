using System.Collections.ObjectModel;
using System.Text.Json.Nodes;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace CvAut.ViewModels
{
    public sealed partial class CoordinatePointViewModel : ObservableObject
    {
        [ObservableProperty] private string _direction = "Top";
        [ObservableProperty] private string _kind = "Deploy";
        [ObservableProperty] private int _x;
        [ObservableProperty] private int _y;

        public CoordinatePointViewModel(string direction, string kind, int x, int y)
        {
            Direction = direction;
            Kind = kind;
            X = x;
            Y = y;
        }

        public string DisplayDirection => Direction switch
        {
            "Top" => "Phía trên",
            "Right" => "Phía phải",
            "Bottom" => "Phía dưới",
            "Left" => "Phía trái",
            _ => Direction
        };

        public string DisplayKind => Kind switch
        {
            "Deploy" => "Thả quân",
            "Spell" => "Dùng phép",
            "Hero" => "Thả tướng",
            _ => Kind
        };
    }

    public partial class AdvancedViewModel : ViewModelBase
    {
        private readonly IConfigStore _configStore;
        private JsonObject _config;
        private JsonObject? _snapshot;

        [ObservableProperty] private string _title = "Nâng cao";
        [ObservableProperty] private string _status = "Đã tải";
        [ObservableProperty] private int _searchDelayMs;
        [ObservableProperty] private int _deployDelayMs;
        [ObservableProperty] private int _returnHomeDelayMs;

        public ObservableCollection<CoordinatePointViewModel> Coordinates { get; } = new();

        public bool HasCoordinates => Coordinates.Count > 0;

        public AdvancedViewModel(IConfigStore configStore)
        {
            _configStore = configStore;
            _config = _configStore.LoadActiveConfig();
            SeedCoordinates();
            LoadFromConfig();
        }

        public AdvancedViewModel() : this(new ConfigStore())
        {
        }

        [RelayCommand]
        private void Save()
        {
            ApplyToConfig();
            _configStore.SaveActiveConfig(_config);
            _snapshot = Clone(_config);
            Status = "Đã lưu cài đặt nâng cao";
        }

        [RelayCommand]
        private void Undo()
        {
            if (_snapshot is not null)
            {
                _config = Clone(_snapshot);
                LoadFromConfig();
                Status = "Đã hoàn tác";
            }
        }

        [RelayCommand]
        private void ClearCoordinates()
        {
            foreach (CoordinatePointViewModel point in Coordinates)
            {
                point.X = 0;
                point.Y = 0;
            }

            Status = "Đã xóa tọa độ";
        }

        private void LoadFromConfig()
        {
            _snapshot = Clone(_config);
            JsonNode? advanced = _config["advanced"];
            SearchDelayMs = ConfigStore.TryGetInt(advanced?["search_delay_ms"], 800);
            DeployDelayMs = ConfigStore.TryGetInt(advanced?["deploy_delay_ms"], 120);
            ReturnHomeDelayMs = ConfigStore.TryGetInt(advanced?["return_home_delay_ms"], 1500);

            JsonNode? coords = advanced?["coordinates"];
            foreach (CoordinatePointViewModel point in Coordinates)
            {
                string key = point.Direction + "." + point.Kind;
                point.X = ConfigStore.TryGetInt(coords?[key]?["x"], point.X);
                point.Y = ConfigStore.TryGetInt(coords?[key]?["y"], point.Y);
            }
        }

        private void ApplyToConfig()
        {
            JsonObject advanced = ConfigStore.GetOrCreateObject(_config, "advanced");
            advanced["search_delay_ms"] = SearchDelayMs;
            advanced["deploy_delay_ms"] = DeployDelayMs;
            advanced["return_home_delay_ms"] = ReturnHomeDelayMs;

            var coords = new JsonObject();
            foreach (CoordinatePointViewModel point in Coordinates)
            {
                coords[point.Direction + "." + point.Kind] = new JsonObject
                {
                    ["x"] = point.X,
                    ["y"] = point.Y,
                };
            }

            advanced["coordinates"] = coords;
        }

        private void SeedCoordinates()
        {
            string[] directions = { "Top", "Right", "Bottom", "Left" };
            string[] kinds = { "Deploy", "Spell", "Hero" };
            foreach (string direction in directions)
            {
                foreach (string kind in kinds)
                {
                    Coordinates.Add(new CoordinatePointViewModel(direction, kind, 0, 0));
                }
            }
        }

        private static JsonObject Clone(JsonObject source)
        {
            return JsonNode.Parse(source.ToJsonString())!.AsObject();
        }
    }
}
