using System;
using System.Text.Json.Nodes;
using System.Windows.Input;
using CvAut.WpfApp.Services;

namespace CvAut.WpfApp.ViewModels
{
    /// <summary>
    /// ViewModel cho màn hình "Cài đặt Chung" (GeneralView.xaml).
    /// Quản lý các ngưỡng tài nguyên đi cướp (Gold, Elixir, DE), cấu hình kết nối ADB giả lập,
    /// các tùy chọn nâng cấp tường (Wall Upgrades) và xin quân, đồng thời hiển thị bảng tin hoạt động rút gọn.
    /// </summary>
    public class GeneralViewModel : ViewModelBase
    {
        // Dịch vụ quản lý bot
        private readonly IBotService _botService;
        private const int MinSupportedWallLevel = 8;
        private const int MaxSupportedWallLevel = 17;

        /// <summary>
        /// ViewModel quản lý quân đội (Army Settings), dùng để chia sẻ thông số cấu hình và hiển thị trên giao diện chung.
        /// </summary>
        public ArmyViewModel ArmyVM { get; }

        public int[] SupportedWallLevels { get; } = { 8, 9, 10, 11, 12, 13, 14, 15, 16, 17 };

        // Ngưỡng Vàng (Gold) tối thiểu của đối thủ để quyết định tấn công
        private string _goldThreshold = "650000";

        // Ngưỡng Dầu hồng (Elixir) tối thiểu của đối thủ để quyết định tấn công
        private string _elixirThreshold = "650000";

        // Ngưỡng Dầu đen (Dark Elixir) tối thiểu của đối thủ để quyết định tấn công
        private string _darkThreshold = "1000";

        // Tùy chọn bật/tắt tính năng tự động nâng cấp tường
        private bool _upgradeWallEnabled;

        // Lượng Vàng dự trữ tối thiểu cần giữ lại (chỉ dùng Vàng vượt quá ngưỡng này để nâng tường)
        private string _wallGoldThreshold = "5000000";

        // Lượng Dầu hồng dự trữ tối thiểu cần giữ lại (chỉ dùng Dầu hồng vượt quá ngưỡng này để nâng tường)
        private string _wallElixirThreshold = "5000000";

        // Cấp độ tường đích muốn hướng tới (ví dụ: cấp 14)
        private int _wallLevel = 14;

        // Địa chỉ IP của máy chủ ADB (mặc định là localhost)
        private string _adbHost = "127.0.0.1";

        // Cổng kết nối ADB của thiết bị giả lập (ví dụ: 5556 cho MEmu)
        private int _adbPort = 5556;

        // Tùy chọn tự động xin quân (Request Troops) khi ở nhà
        private bool _requestTroopsEnabled;

        // Chuỗi văn bản hiển thị lịch sử hoạt động rút gọn trên Dashboard
        private string _activityText = "[00:00:00] INFO  Dashboard initialized\r\n[00:00:00] WAIT  Activity feed will mirror runtime logs\r\n[00:00:00] READY Configure resources and press START\r\n";

        // Properties (Thuộc tính liên kết Data Binding với View)

        /// <summary>
        /// Ngưỡng Vàng tối thiểu của đối thủ.
        /// </summary>
        public string GoldThreshold
        {
            get => _goldThreshold;
            set => SetProperty(ref _goldThreshold, value);
        }

        /// <summary>
        /// Ngưỡng Dầu hồng tối thiểu của đối thủ.
        /// </summary>
        public string ElixirThreshold
        {
            get => _elixirThreshold;
            set => SetProperty(ref _elixirThreshold, value);
        }

        /// <summary>
        /// Ngưỡng Dầu đen tối thiểu của đối thủ.
        /// </summary>
        public string DarkThreshold
        {
            get => _darkThreshold;
            set => SetProperty(ref _darkThreshold, value);
        }

        /// <summary>
        /// Trạng thái kích hoạt nâng cấp tường tự động.
        /// </summary>
        public bool UpgradeWallEnabled
        {
            get => _upgradeWallEnabled;
            set
            {
                if (SetProperty(ref _upgradeWallEnabled, value))
                {
                    // Phát thông báo thay đổi trạng thái để cập nhật thuộc tính IsWallInputsEnabled liên quan
                    OnPropertyChanged(nameof(IsWallInputsEnabled));
                }
            }
        }

        /// <summary>
        /// Cho phép nhập liệu các ô thông số tường (chỉ khi UpgradeWallEnabled bằng True).
        /// </summary>
        public bool IsWallInputsEnabled => UpgradeWallEnabled;

        /// <summary>
        /// Ngưỡng Vàng dự trữ tối thiểu để giữ lại.
        /// </summary>
        public string WallGoldThreshold
        {
            get => _wallGoldThreshold;
            set => SetProperty(ref _wallGoldThreshold, value);
        }

        /// <summary>
        /// Ngưỡng Dầu hồng dự trữ tối thiểu để giữ lại.
        /// </summary>
        public string WallElixirThreshold
        {
            get => _wallElixirThreshold;
            set => SetProperty(ref _wallElixirThreshold, value);
        }

        /// <summary>
        /// Cấp độ tường đích.
        /// </summary>
        public int WallLevel
        {
            get => _wallLevel;
            set => SetProperty(ref _wallLevel, ClampWallLevel(value));
        }

        /// <summary>
        /// Địa chỉ IP máy chủ ADB.
        /// </summary>
        public string AdbHost
        {
            get => _adbHost;
            set => SetProperty(ref _adbHost, value);
        }

        /// <summary>
        /// Cổng ADB của thiết bị giả lập.
        /// </summary>
        public int AdbPort
        {
            get => _adbPort;
            set => SetProperty(ref _adbPort, value);
        }

        /// <summary>
        /// Bật/tắt tự động gửi yêu cầu xin quân clan.
        /// </summary>
        public bool RequestTroopsEnabled
        {
            get => _requestTroopsEnabled;
            set => SetProperty(ref _requestTroopsEnabled, value);
        }

        /// <summary>
        /// Chuỗi văn bản chứa thông tin log rút gọn hiển thị trên màn hình chính.
        /// </summary>
        public string ActivityText
        {
            get => _activityText;
            set => SetProperty(ref _activityText, value);
        }

        /// <summary>
        /// Lệnh xóa bảng tin lịch sử hoạt động.
        /// </summary>
        public ICommand ClearActivityCommand { get; }

        /// <summary>
        /// Khởi tạo GeneralViewModel, đăng ký sự kiện nhận log từ BotService và thực hiện nạp cấu hình ban đầu.
        /// </summary>
        /// <param name="botService">Dịch vụ quản lý bot.</param>
        /// <param name="armyVM">ViewModel quản lý cấu hình quân đội.</param>
        public GeneralViewModel(IBotService botService, ArmyViewModel armyVM)
        {
            _botService = botService;
            ArmyVM = armyVM;

            // Đăng ký nhận log để cập nhật bảng tin hoạt động
            _botService.LogReceived += AppendActivityLog;

            // Nạp cấu hình
            LoadConfig();

            // Lệnh xóa log bảng tin hoạt động nhanh
            ClearActivityCommand = new RelayCommand(() => ActivityText = string.Empty);
        }

        /// <summary>
        /// Tải thông số cấu hình từ file cấu hình chính (test_config.json)
        /// và cấu hình cụ thể cho Làng hiện tại (Village_{id}.json).
        /// </summary>
        public void LoadConfig()
        {
            try
            {
                var root = _botService.LoadMainConfig();

                // 1. Nạp thông số kết nối ADB
                if (root["device_connection"] is JsonObject device)
                {
                    AdbHost = device["host"]?.ToString() ?? "127.0.0.1";
                    AdbPort = device["port"]?.GetValue<int>() ?? 5556;
                }

                // 2. Nạp thông số cấu hình riêng theo từng làng
                var profile = _botService.LoadProfile(_botService.CurrentVillage);

                // 3. Nạp ngưỡng tài nguyên đi cướp: ưu tiên profile làng hiện tại, root chỉ là fallback.
                var rootFarming = root["farming_thresholds"] as JsonObject;
                var legacyTarget = root["target_data_threshold"] as JsonObject;
                GoldThreshold = GetProfileOrRootThreshold(profile, rootFarming, legacyTarget, "gold_threshold", "gold", "650000");
                ElixirThreshold = GetProfileOrRootThreshold(profile, rootFarming, legacyTarget, "elixir_threshold", "elixir", "650000");
                DarkThreshold = GetProfileOrRootThreshold(profile, rootFarming, legacyTarget, "dark_elixir_threshold", "dark_elixir", "1000");

                // 4. Nạp thông số cấu hình riêng theo từng làng
                UpgradeWallEnabled = GetBool(profile, "upgrade_wall", false);
                WallLevel = GetInt(profile, "wall_level", 14);
                WallGoldThreshold = profile["wall_gold_threshold"]?.ToString() ?? "5000000";
                WallElixirThreshold = profile["wall_elixir_threshold"]?.ToString() ?? "5000000";
                RequestTroopsEnabled = GetBool(profile, "request_troops", false);
            }
            catch
            {
                // Bỏ qua lỗi và sử dụng các giá trị mặc định đã được gán sẵn ở thuộc tính
            }
        }

        /// <summary>
        /// Ghi các tùy chỉnh giao diện hiện tại vào tệp cấu hình chính và tệp cấu hình làng hiện tại thông qua BotService.
        /// </summary>
        public void SaveConfig()
        {
            try
            {
                // Tải cấu hình chính hiện tại lên để tiến hành hợp nhất tránh ghi đè mất thông tin khác
                var root = _botService.LoadMainConfig();

                // Lưu thông tin ADB
                root["device_connection"] = new JsonObject
                {
                    ["host"] = string.IsNullOrWhiteSpace(AdbHost) ? "127.0.0.1" : AdbHost.Trim(),
                    ["port"] = AdbPort
                };

                // Chuyển đổi chuỗi sang số nguyên
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

                // Lưu các thuộc tính tường vào cấu hình chính
                root["upgrade_wall"] = UpgradeWallEnabled;
                int wallLevel = ClampWallLevel(WallLevel);
                root["wall_level"] = wallLevel;
                root["wall_gold_threshold"] = ParseInt(WallGoldThreshold, 5000000);
                root["wall_elixir_threshold"] = ParseInt(WallElixirThreshold, 5000000);
                root["request_troops"] = RequestTroopsEnabled;

                _botService.SaveMainConfig(root);

                // Lưu cấu hình làng riêng lẻ tương ứng để phục vụ chạy luồng xoay vòng (Multi-Village loop)
                var profile = _botService.LoadProfile(_botService.CurrentVillage);
                profile["gold_threshold"] = gold;
                profile["elixir_threshold"] = elixir;
                profile["dark_elixir_threshold"] = dark;
                profile["upgrade_wall"] = UpgradeWallEnabled;
                profile["wall_level"] = wallLevel;
                profile["wall_gold_threshold"] = ParseInt(WallGoldThreshold, 5000000);
                profile["wall_elixir_threshold"] = ParseInt(WallElixirThreshold, 5000000);
                profile["request_troops"] = RequestTroopsEnabled;

                _botService.SaveProfile(_botService.CurrentVillage, profile);
            }
            catch
            {
                // Bỏ qua lỗi âm thầm khi lưu cấu hình
            }
        }

        /// <summary>
        /// Đưa log từ BotService vào bộ đệm hiển thị, chỉ giữ lại 15 dòng nhật ký mới nhất cho giao diện rút gọn.
        /// </summary>
        private void AppendActivityLog(string message)
        {
            var lines = ActivityText.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None).ToList();
            lines.Add(message);

            // Giới hạn độ dài tối đa 15 dòng
            if (lines.Count > 15)
            {
                lines.RemoveAt(0);
            }
            ActivityText = string.Join("\r\n", lines);
        }

        // Các hàm phụ trợ chuyển đổi kiểu dữ liệu an toàn từ JSON

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

        private static int ClampWallLevel(int value)
        {
            return Math.Clamp(value, MinSupportedWallLevel, MaxSupportedWallLevel);
        }

        private static string GetProfileOrRootThreshold(
            JsonObject profile,
            JsonObject? farming,
            JsonObject? legacyTarget,
            string profileKey,
            string legacyKey,
            string defaultValue)
        {
            if (profile.TryGetPropertyValue(profileKey, out var profileValue) && profileValue != null)
            {
                return profileValue.ToString();
            }

            if (farming?.TryGetPropertyValue(profileKey, out var farmingValue) == true && farmingValue != null)
            {
                return farmingValue.ToString();
            }

            if (legacyTarget?.TryGetPropertyValue(legacyKey, out var legacyValue) == true && legacyValue != null)
            {
                return legacyValue.ToString();
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
