using System;
using System.Collections.Generic;
using System.Text.Json.Nodes;
using CvAut.WpfApp.Services;

namespace CvAut.WpfApp.ViewModels
{
    /// <summary>
    /// ViewModel cho màn hình "Quân đội & Thả quân" (ArmyView.xaml).
    /// Quản lý việc lựa chọn chiến thuật tấn công (Dragon, Electro Dragon), hiển thị chi tiết đội hình mẫu,
    /// thiết lập chế độ luyện quân (Smart Train tự động tính toán thiếu hụt hoặc Quick Train dùng Slot lưu sẵn trong game).
    /// </summary>
    public class ArmyViewModel : ViewModelBase
    {
        // Dịch vụ quản lý bot
        private readonly IBotService _botService;

        // Chiến thuật tấn công đang được chọn (Dragon / Electro Dragon)
        private string _selectedAttackStrategy = "Dragon";

        // Tên xem trước chiến thuật trên UI
        private string _attackPreview = "DRAGON";

        // Mô tả chi tiết chiến thuật
        private string _attackDescription = "DRAGON ATTACK";

        // Đường dẫn ảnh mẫu minh họa sơ đồ thả quân của chiến thuật
        private string _attackImagePath = "pack://siteoforigin:,,,/assets/Templates/attacks/Dragon_attack.png";

        // Thành phần đội hình lính và phép dự kiến (ví dụ: 13 Dragon / 15 Balloons...)
        private string _attackComposition = "13 Dragon / 15 Balloons / 4 Rage / 3 Freeze";

        // Thành phần hỗ trợ đi kèm (như Tướng, xe công thành, lính xin Clan)
        private string _attackSupport = "Any Siege / All heroes / CC rage + freeze";

        private List<ArmyCardViewModel> _attackCards = new();

        // Chế độ huấn luyện thông minh (đếm lính thiếu trong doanh trại để luyện bù)
        private bool _isSmartTrain = true;

        // Chế độ huấn luyện nhanh (bấm vào Slot Quick Train có sẵn)
        private bool _isQuickTrain;

        // Số thứ tự Slot Quick Train muốn sử dụng (thường là 1, 2 hoặc 3)
        private int _quickSlot = 1;

        /// <summary>
        /// Danh sách các chiến thuật tấn công được hỗ trợ hiển thị trên ComboBox.
        /// </summary>
        public List<string> AttackStrategies { get; } = new() { "Dragon", "Electro Dragon" };

        /// <summary>
        /// Chiến thuật tấn công đang được chọn.
        /// Khi thay đổi sẽ tự động cập nhật các thông số trực quan đi kèm.
        /// </summary>
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

        /// <summary>
        /// Tên xem trước của chiến thuật.
        /// </summary>
        public string AttackPreview
        {
            get => _attackPreview;
            set => SetProperty(ref _attackPreview, value);
        }

        /// <summary>
        /// Mô tả chi tiết chiến thuật.
        /// </summary>
        public string AttackDescription
        {
            get => _attackDescription;
            set => SetProperty(ref _attackDescription, value);
        }

        /// <summary>
        /// Đường dẫn ảnh sơ đồ thả quân.
        /// </summary>
        public string AttackImagePath
        {
            get => _attackImagePath;
            set => SetProperty(ref _attackImagePath, value);
        }

        /// <summary>
        /// Chi tiết danh sách lính & phép của chiến thuật.
        /// </summary>
        public string AttackComposition
        {
            get => _attackComposition;
            set => SetProperty(ref _attackComposition, value);
        }

        /// <summary>
        /// Chi tiết xe công thành và tướng hỗ trợ.
        /// </summary>
        public string AttackSupport
        {
            get => _attackSupport;
            set => SetProperty(ref _attackSupport, value);
        }

        public List<ArmyCardViewModel> AttackCards
        {
            get => _attackCards;
            set => SetProperty(ref _attackCards, value);
        }

        /// <summary>
        /// Kích hoạt chế độ huấn luyện lính thông minh (Smart Train).
        /// </summary>
        public bool IsSmartTrain
        {
            get => _isSmartTrain;
            set
            {
                if (SetProperty(ref _isSmartTrain, value))
                {
                    // Đảm bảo Smart Train và Quick Train là hai chế độ loại trừ lẫn nhau
                    IsQuickTrain = !value;
                }
            }
        }

        /// <summary>
        /// Kích hoạt chế độ huấn luyện lính nhanh (Quick Train).
        /// </summary>
        public bool IsQuickTrain
        {
            get => _isQuickTrain;
            set
            {
                if (SetProperty(ref _isQuickTrain, value))
                {
                    IsSmartTrain = !value;
                    // Phát thông báo thay đổi trạng thái để cập nhật nút chọn Slot Quick Train liên quan
                    OnPropertyChanged(nameof(IsQuickSlotEnabled));
                }
            }
        }

        /// <summary>
        /// Cho phép chọn Slot Quick Train (chỉ bật khi chọn chế độ huấn luyện nhanh).
        /// </summary>
        public bool IsQuickSlotEnabled => IsQuickTrain;

        /// <summary>
        /// Vị trí Slot Quick Train cần nhấn huấn luyện (1, 2, 3).
        /// </summary>
        public int QuickSlot
        {
            get => _quickSlot;
            set => SetProperty(ref _quickSlot, value);
        }

        /// <summary>
        /// Khởi tạo ArmyViewModel và nạp cấu hình quân đội.
        /// </summary>
        /// <param name="botService">Dịch vụ quản lý bot.</param>
        public ArmyViewModel(IBotService botService)
        {
            _botService = botService;
            LoadConfig();
            UpdateAttackStrategyInfo();
        }

        /// <summary>
        /// Tải thông số cấu hình quân đội và chiến thuật từ file cấu hình của Làng hiện tại.
        /// </summary>
        public void LoadConfig()
        {
            try
            {
                var profile = _botService.LoadProfile(_botService.CurrentVillage);

                // Tải khóa chiến thuật (Dragon_Attack hoặc ElectroDragon_Attack)
                string attack = profile["attack"]?.ToString() ?? "Dragon_Attack";
                SelectedAttackStrategy = attack == "ElectroDragon_Attack" ? "Electro Dragon" : "Dragon";

                // Tải chế độ luyện quân (smart hoặc quick)
                string trainMode = profile["train_mode"]?.ToString() ?? "smart";
                IsQuickTrain = trainMode == "quick";
                IsSmartTrain = trainMode != "quick";

                // Tải vị trí Slot huấn luyện nhanh
                QuickSlot = GetInt(profile, "quick_slot", 1);
            }
            catch
            {
                // Bỏ qua lỗi và giữ giá trị mặc định của lớp
            }
        }

        /// <summary>
        /// Lưu các tùy chọn cấu hình quân đội hiện tại xuống file cấu hình chung và file cấu hình của làng hiện tại.
        /// </summary>
        public void SaveConfig()
        {
            try
            {
                // Tải cấu hình chính để tránh mất dữ liệu các tab khác
                var root = _botService.LoadMainConfig();
                string attackKey = SelectedAttackStrategy == "Electro Dragon" ? "ElectroDragon_Attack" : "Dragon_Attack";
                string trainMode = IsQuickTrain ? "quick" : "smart";

                root["attack"] = attackKey;
                root["train_mode"] = trainMode;
                root["quick_slot"] = QuickSlot;
                _botService.SaveMainConfig(root);

                // Lưu riêng vào cấu hình của làng hiện tại để hỗ trợ luân chuyển tài khoản
                var profile = _botService.LoadProfile(_botService.CurrentVillage);
                profile["attack"] = attackKey;
                profile["train_mode"] = trainMode;
                profile["quick_slot"] = QuickSlot;
                _botService.SaveProfile(_botService.CurrentVillage, profile);
            }
            catch
            {
                // Bỏ qua lỗi âm thầm khi lưu cấu hình
            }
        }

        /// <summary>
        /// Cập nhật các thông số chi tiết mô tả lính/phép/ảnh minh họa tương ứng với chiến thuật được chọn.
        /// </summary>
        private void UpdateAttackStrategyInfo()
        {
            if (SelectedAttackStrategy == "Electro Dragon")
            {
                AttackPreview = "ELECTRO DRAGON";
                AttackDescription = "ELECTRO DRAGON ATTACK";
                AttackImagePath = "pack://siteoforigin:,,,/assets/Templates/attacks/ElectroDragon_Attack.png";
                AttackComposition = "10 E-Drag / 15 Balloons / 4 Rage / 3 Freeze";
                AttackSupport = "Any Siege / All heroes / CC rage + freeze";
                AttackCards = BuildAttackCards("E-Drag", "x10", "pack://siteoforigin:,,,/assets/Templates/troops/E_Drag.png");
            }
            else
            {
                AttackPreview = "DRAGON";
                AttackDescription = "DRAGON ATTACK";
                AttackImagePath = "pack://siteoforigin:,,,/assets/Templates/attacks/Dragon_attack.png";
                AttackComposition = "13 Dragon / 15 Balloons / 4 Rage / 3 Freeze";
                AttackSupport = "Any Siege / All heroes / CC rage + freeze";
                AttackCards = BuildAttackCards("Dragon", "x13", "pack://siteoforigin:,,,/assets/Templates/troops/dragon.png");
            }
        }

        private static List<ArmyCardViewModel> BuildAttackCards(string mainTroopName, string mainTroopCount, string mainTroopPath)
        {
            return new List<ArmyCardViewModel>
            {
                new(mainTroopName, mainTroopCount, mainTroopPath),
                new("Balloon", "x15", "pack://siteoforigin:,,,/assets/Templates/troops/balloon.png"),
                new("Rage", "x4", "pack://siteoforigin:,,,/assets/Templates/spells/rage.png"),
                new("Freeze", "x3", "pack://siteoforigin:,,,/assets/Templates/spells/freeze.png"),
                new("King", "Hero", "pack://siteoforigin:,,,/assets/Templates/heroes/bk.png"),
                new("Queen", "Hero", "pack://siteoforigin:,,,/assets/Templates/heroes/queen.png"),
                new("Warden", "Hero", "pack://siteoforigin:,,,/assets/Templates/heroes/warden.png"),
                new("Champion", "Hero", "pack://siteoforigin:,,,/assets/Templates/heroes/rc.png"),
            };
        }

        /// <summary>
        /// Lấy giá trị số nguyên từ JsonObject an toàn.
        /// </summary>
        private static int GetInt(JsonObject obj, string key, int defaultValue)
        {
            if (obj.TryGetPropertyValue(key, out var val) && val != null)
            {
                try { return val.GetValue<int>(); } catch { }
            }
            return defaultValue;
        }
    }

    public sealed class ArmyCardViewModel
    {
        public ArmyCardViewModel(string name, string count, string imagePath)
        {
            Name = name;
            Count = count;
            ImagePath = imagePath;
        }

        public string Name { get; }
        public string Count { get; }
        public string ImagePath { get; }
    }
}
