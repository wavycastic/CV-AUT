using System;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using CvAut.WpfApp.Services;

namespace CvAut.WpfApp.ViewModels
{
    /// <summary>
    /// ViewModel chính (Shell ViewModel) cho giao diện WPF của SimpliMixi.
    /// Quản lý việc chuyển hướng giữa các View con, trạng thái hoạt động của bot,
    /// liên kết dữ liệu (Data Binding) với các thành phần điều khiển trên Window chính và đồng bộ với BotService.
    /// </summary>
    public class MainViewModel : ViewModelBase
    {
        // Dịch vụ quản lý bot
        private readonly IBotService _botService;

        // ViewModel con đang được hiển thị hiện tại trên vùng nội dung chính
        private ViewModelBase _currentViewModel;
        // ID định danh Tab hiện đang được chọn (ví dụ: "general", "army",...)
        private string _currentTab = "general";

        // Chuỗi văn bản hiển thị trạng thái bot (RUNNING, PAUSED, NOT RUNNING)
        private string _statusPillText = "NOT RUNNING";

        // Màu sắc đại diện cho trạng thái bot trên UI (ví dụ: Xanh lá cho RUNNING, Vàng cho PAUSED)
        private System.Windows.Media.Brush _statusPillBrush;

        // Thời gian bot đã chạy liên tục
        private string _uptimeText = "00:00:00";

        // Lượng RAM hệ thống tiến trình đang chiếm dụng
        private string _memoryUsageText = "0.0 MB";

        // Tỷ lệ tấn công thành công
        private string _successRateText = "100%";

        // Số trận cướp đã thực hiện
        private int _attacksCount;

        // Nhãn văn bản trên nút Tạm dừng (PAUSE / RESUME)
        private string _pauseButtonText = "PAUSE";

        // ID làng hiện tại đang chọn (từ 1 đến 5)
        private int _currentVillage = 1;

        // Sub ViewModels (Các ViewModel con quản lý các Tab/Trang giao diện tương ứng)

        /// <summary>
        /// ViewModel quản lý Cài đặt Chung (General settings).
        /// </summary>
        public GeneralViewModel GeneralVM { get; }

        /// <summary>
        /// ViewModel quản lý Cấu hình Quân đội/Thả quân (Army settings).
        /// </summary>
        public ArmyViewModel ArmyVM { get; }

        /// <summary>
        /// ViewModel quản lý chế độ chơi nhiều làng (Multi-Village).
        /// </summary>
        public MultiVillageViewModel MultiVillageVM { get; }

        /// <summary>
        /// ViewModel quản lý tự động sự kiện Clan Games.
        /// </summary>
        public ClanGamesViewModel ClanGamesVM { get; }

        /// <summary>
        /// ViewModel quản lý tự động Clan Capital (Thủ đô Clan).
        /// </summary>
        public ClanCapitalViewModel ClanCapitalVM { get; }

        /// <summary>
        /// ViewModel hiển thị Biểu đồ/Số liệu Thống kê hiệu suất cướp.
        /// </summary>
        public StatisticsViewModel StatisticsVM { get; }

        /// <summary>
        /// ViewModel quản lý giao diện hiển thị Log thời gian thực.
        /// </summary>
        public LogsViewModel LogsVM { get; }

        /// <summary>
        /// ViewModel quản lý cài đặt và cảnh báo an toàn.
        /// </summary>
        public SettingsViewModel SettingsVM { get; }

        // Navigation Commands (Lệnh chuyển đổi giữa các màn hình con)

        /// <summary>
        /// Lệnh chuyển sang màn hình Cài đặt Chung.
        /// </summary>
        public ICommand NavigateGeneralCommand { get; }

        /// <summary>
        /// Lệnh chuyển sang màn hình Cấu hình Quân đội.
        /// </summary>
        public ICommand NavigateArmyCommand { get; }

        /// <summary>
        /// Lệnh chuyển sang màn hình Cấu hình Nhiều Làng.
        /// </summary>
        public ICommand NavigateMultiVillageCommand { get; }

        /// <summary>
        /// Lệnh chuyển sang màn hình Clan Games.
        /// </summary>
        public ICommand NavigateClanGamesCommand { get; }

        /// <summary>
        /// Lệnh chuyển sang màn hình Clan Capital.
        /// </summary>
        public ICommand NavigateClanCapitalCommand { get; }

        /// <summary>
        /// Lệnh chuyển sang màn hình Thống kê.
        /// </summary>
        public ICommand NavigateStatisticsCommand { get; }

        /// <summary>
        /// Lệnh chuyển sang màn hình Xem Logs.
        /// </summary>
        public ICommand NavigateLogsCommand { get; }

        /// <summary>
        /// Lệnh chuyển sang màn hình Cài đặt.
        /// </summary>
        public ICommand NavigateSettingsCommand { get; }

        // Bot Control Commands (Lệnh điều khiển hoạt động của Bot)

        /// <summary>
        /// Lệnh kích hoạt chạy Bot.
        /// </summary>
        public ICommand StartCommand { get; }

        /// <summary>
        /// Lệnh dừng Bot.
        /// </summary>
        public ICommand StopCommand { get; }

        /// <summary>
        /// Lệnh tạm dừng hoặc tiếp tục chạy Bot.
        /// </summary>
        public ICommand TogglePauseCommand { get; }

        // Village switching commands (Lệnh chuyển đổi qua lại giữa 5 cấu hình làng)

        /// <summary>
        /// Lệnh chuyển về làng phía trước.
        /// </summary>
        public ICommand PrevVillageCommand { get; }

        /// <summary>
        /// Lệnh chuyển đến làng tiếp theo.
        /// </summary>
        public ICommand NextVillageCommand { get; }

        // Properties (Thuộc tính Data Binding)

        /// <summary>
        /// ViewModel con hiện tại đang được hiển thị trong NavigationView của MainWindow.
        /// </summary>
        public ViewModelBase CurrentViewModel
        {
            get => _currentViewModel;
            set => SetProperty(ref _currentViewModel, value);
        }

        /// <summary>
        /// ID định danh của Tab/Trang hiện tại đang được hiển thị trên giao diện.
        /// </summary>
        public string CurrentTab
        {
            get => _currentTab;
            set => SetProperty(ref _currentTab, value);
        }

        /// <summary>
        /// Văn bản trạng thái bot (IDLE, RUNNING, PAUSED, STOPPING).
        /// </summary>
        public string StatusPillText
        {
            get => _statusPillText;
            set => SetProperty(ref _statusPillText, value);
        }

        /// <summary>
        /// Màu sắc của hình tròn/nền trạng thái tương ứng trên giao diện.
        /// </summary>
        public System.Windows.Media.Brush StatusPillBrush
        {
            get => _statusPillBrush;
            set => SetProperty(ref _statusPillBrush, value);
        }

        /// <summary>
        /// Thời gian hoạt động liên tục dạng chuỗi.
        /// </summary>
        public string UptimeText
        {
            get => _uptimeText;
            set => SetProperty(ref _uptimeText, value);
        }

        /// <summary>
        /// Lượng RAM tiêu thụ dạng chuỗi.
        /// </summary>
        public string MemoryUsageText
        {
            get => _memoryUsageText;
            set => SetProperty(ref _memoryUsageText, value);
        }

        /// <summary>
        /// Tỷ lệ tấn công thành công dạng chuỗi.
        /// </summary>
        public string SuccessRateText
        {
            get => _successRateText;
            set => SetProperty(ref _successRateText, value);
        }

        /// <summary>
        /// Số trận cướp đã thực hiện.
        /// </summary>
        public int AttacksCount
        {
            get => _attacksCount;
            set => SetProperty(ref _attacksCount, value);
        }

        /// <summary>
        /// Văn bản hiển thị trên nút tạm dừng (PAUSE hoặc RESUME).
        /// </summary>
        public string PauseButtonText
        {
            get => _pauseButtonText;
            set => SetProperty(ref _pauseButtonText, value);
        }

        /// <summary>
        /// Chỉ số ID làng hiện tại (1-5). Khi thay đổi sẽ đồng bộ sang BotService
        /// và yêu cầu các ViewModel con (GeneralVM, ArmyVM) tự động tải lại cấu hình tương ứng.
        /// </summary>
        public int CurrentVillage
        {
            get => _currentVillage;
            set
            {
                if (SetProperty(ref _currentVillage, value))
                {
                    _botService.CurrentVillage = value;
                    OnPropertyChanged(nameof(CurrentVillageName));
                    // Tải lại cấu hình của làng vừa được chọn
                    GeneralVM.LoadConfig();
                    ArmyVM.LoadConfig();
                }
            }
        }

        /// <summary>
        /// Chuỗi tên hiển thị của làng hiện tại (ví dụ: "Village 1").
        /// </summary>
        public string CurrentVillageName => $"Village {CurrentVillage}";

        /// <summary>
        /// Điều kiện để kích hoạt nút Khởi chạy Bot (chỉ khi bot đang không chạy).
        /// </summary>
        public bool IsStartButtonEnabled => !_botService.IsRunning;

        /// <summary>
        /// Điều kiện để kích hoạt nút Dừng Bot (khi bot đang chạy).
        /// </summary>
        public bool IsEndButtonEnabled => _botService.IsRunning && _botService.StatusText != "STOPPING";

        /// <summary>
        /// Điều kiện để kích hoạt nút Tạm dừng Bot (khi bot đang chạy).
        /// </summary>
        public bool IsPauseButtonEnabled => _botService.IsRunning;

        /// <summary>
        /// Khởi tạo MainViewModel, liên kết dịch vụ BotService, khởi tạo các ViewModel con và thiết lập các Command.
        /// </summary>
        /// <param name="botService">Dịch vụ điều khiển bot.</param>
        public MainViewModel(IBotService botService)
        {
            _botService = botService;

            // Khởi tạo các trang ViewModel con
            ArmyVM = new ArmyViewModel(_botService);
            GeneralVM = new GeneralViewModel(_botService, ArmyVM);
            MultiVillageVM = new MultiVillageViewModel(_botService);
            ClanGamesVM = new ClanGamesViewModel(_botService);
            ClanCapitalVM = new ClanCapitalViewModel(_botService);
            StatisticsVM = new StatisticsViewModel(_botService);
            LogsVM = new LogsViewModel(_botService);
            SettingsVM = new SettingsViewModel(_botService);

            // Trang mặc định khi mở app là Cài đặt Chung
            _currentViewModel = GeneralVM;

            // Màu mặc định cho trạng thái IDLE (Xám nhạt)
            _statusPillBrush = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(132, 153, 184));

            // Đăng ký nhận sự kiện thay đổi trạng thái và cập nhật số liệu từ BotService
            _botService.StatusChanged += OnBotStatusChanged;
            _botService.StatsUpdated += OnBotStatsUpdated;

            // Đăng ký các lệnh chuyển trang (Navigation)
            NavigateGeneralCommand = new RelayCommand(() => NavigateTo(GeneralVM, "general"));
            NavigateArmyCommand = new RelayCommand(() => NavigateTo(ArmyVM, "army"));
            NavigateMultiVillageCommand = new RelayCommand(() => NavigateTo(MultiVillageVM, "multivillage"));
            NavigateClanGamesCommand = new RelayCommand(() => NavigateTo(ClanGamesVM, "clangames"));
            NavigateClanCapitalCommand = new RelayCommand(() => NavigateTo(ClanCapitalVM, "clancapital"));
            NavigateStatisticsCommand = new RelayCommand(() => NavigateTo(StatisticsVM, "statistics"));
            NavigateLogsCommand = new RelayCommand(() => NavigateTo(LogsVM, "logs"));
            NavigateSettingsCommand = new RelayCommand(() => NavigateTo(SettingsVM, "settings"));

            // Đăng ký các lệnh điều khiển bot
            StartCommand = new RelayCommand(StartBot);
            StopCommand = new RelayCommand(StopBot);
            TogglePauseCommand = new RelayCommand(TogglePause);

            // Đăng ký lệnh chuyển đổi giữa 5 tài khoản làng vòng lặp (1 -> 5 -> 1)
            PrevVillageCommand = new RelayCommand(() =>
            {
                if (CurrentVillage > 1) CurrentVillage--;
                else CurrentVillage = 5;
            });
            NextVillageCommand = new RelayCommand(() =>
            {
                if (CurrentVillage < 5) CurrentVillage++;
                else CurrentVillage = 1;
            });

            // Cập nhật trạng thái và thống kê ban đầu
            OnBotStatusChanged();
            OnBotStatsUpdated();
        }

        /// <summary>
        /// Điều hướng sang trang (ViewModel) chỉ định đồng thời cập nhật thuộc tính tab tương ứng.
        /// </summary>
        /// <param name="viewModel">ViewModel đích muốn điều hướng tới.</param>
        /// <param name="tab">Tên định danh của tab tương ứng.</param>
        private void NavigateTo(ViewModelBase viewModel, string tab)
        {
            CurrentViewModel = viewModel;
            CurrentTab = tab;
        }

        /// <summary>
        /// Thực hiện lưu cấu hình hiện tại ở các Tab xuống tệp tin trước khi bắt đầu khởi chạy bot.
        /// </summary>
        private void StartBot()
        {
            GeneralVM.SaveConfig();
            ArmyVM.SaveConfig();

            _botService.StartBot();
        }

        /// <summary>
        /// Yêu cầu dừng bot thông qua BotService.
        /// </summary>
        private void StopBot()
        {
            _botService.StopBot();
        }

        /// <summary>
        /// Yêu cầu tạm dừng hoặc tiếp tục chạy bot thông qua BotService.
        /// </summary>
        private void TogglePause()
        {
            _botService.TogglePause();
        }

        /// <summary>
        /// Cập nhật màu sắc trạng thái và nhãn hiển thị khi trạng thái bot thay đổi.
        /// </summary>
        private void OnBotStatusChanged()
        {
            string serviceStatus = _botService.StatusText;
            StatusPillText = serviceStatus;

            // Xanh neon lá cây cho RUNNING, Cam cho PAUSED/STOPPING, Xám nhạt cho IDLE
            if (serviceStatus == "RUNNING")
            {
                StatusPillBrush = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(74, 222, 128)); // #4ADE80
                PauseButtonText = "PAUSE";
            }
            else if (serviceStatus == "PAUSED")
            {
                StatusPillBrush = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(250, 204, 21)); // #FACC15
                PauseButtonText = "RESUME";
            }
            else if (serviceStatus == "STOPPING")
            {
                StatusPillBrush = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(251, 146, 60)); // #FB923C
                PauseButtonText = "PAUSE";
            }
            else // IDLE
            {
                StatusPillBrush = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(148, 163, 184)); // #94A3B8
                PauseButtonText = "PAUSE";
            }

            // Phát sự kiện thay đổi trạng thái kích hoạt của các nút điều khiển để UI cập nhật
            OnPropertyChanged(nameof(IsStartButtonEnabled));
            OnPropertyChanged(nameof(IsEndButtonEnabled));
            OnPropertyChanged(nameof(IsPauseButtonEnabled));
        }

        /// <summary>
        /// Đồng bộ các chỉ số thống kê từ BotService lên các thuộc tính giao diện của MainViewModel.
        /// </summary>
        private void OnBotStatsUpdated()
        {
            UptimeText = _botService.UptimeText;
            MemoryUsageText = _botService.MemoryUsageText;
            SuccessRateText = _botService.SuccessRateText;
            AttacksCount = _botService.AttacksCount;
        }
    }
}
