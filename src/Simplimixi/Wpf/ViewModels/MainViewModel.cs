using System;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using CvAut.WpfApp.Services;

namespace CvAut.WpfApp.ViewModels
{
    public class MainViewModel : ViewModelBase
    {
        private readonly IBotService _botService;
        private ViewModelBase _currentViewModel;
        private string _statusPillText = "IDLE";
        private System.Windows.Media.Brush _statusPillBrush;
        private string _uptimeText = "00:00:00";
        private string _memoryUsageText = "0.0 MB";
        private string _successRateText = "100%";
        private int _attacksCount;
        private string _pauseButtonText = "PAUSE";
        private int _currentVillage = 1;

        // Sub ViewModels
        public GeneralViewModel GeneralVM { get; }
        public ArmyViewModel ArmyVM { get; }
        public MultiVillageViewModel MultiVillageVM { get; }
        public ClanGamesViewModel ClanGamesVM { get; }
        public ClanCapitalViewModel ClanCapitalVM { get; }
        public StatisticsViewModel StatisticsVM { get; }
        public LogsViewModel LogsVM { get; }

        // Navigation Commands
        public ICommand NavigateGeneralCommand { get; }
        public ICommand NavigateArmyCommand { get; }
        public ICommand NavigateMultiVillageCommand { get; }
        public ICommand NavigateClanGamesCommand { get; }
        public ICommand NavigateClanCapitalCommand { get; }
        public ICommand NavigateStatisticsCommand { get; }
        public ICommand NavigateLogsCommand { get; }

        // Bot Control Commands
        public ICommand StartCommand { get; }
        public ICommand StopCommand { get; }
        public ICommand TogglePauseCommand { get; }

        // Village switching commands
        public ICommand PrevVillageCommand { get; }
        public ICommand NextVillageCommand { get; }

        // Properties
        public ViewModelBase CurrentViewModel
        {
            get => _currentViewModel;
            set => SetProperty(ref _currentViewModel, value);
        }

        public string StatusPillText
        {
            get => _statusPillText;
            set => SetProperty(ref _statusPillText, value);
        }

        public System.Windows.Media.Brush StatusPillBrush
        {
            get => _statusPillBrush;
            set => SetProperty(ref _statusPillBrush, value);
        }

        public string UptimeText
        {
            get => _uptimeText;
            set => SetProperty(ref _uptimeText, value);
        }

        public string MemoryUsageText
        {
            get => _memoryUsageText;
            set => SetProperty(ref _memoryUsageText, value);
        }

        public string SuccessRateText
        {
            get => _successRateText;
            set => SetProperty(ref _successRateText, value);
        }

        public int AttacksCount
        {
            get => _attacksCount;
            set => SetProperty(ref _attacksCount, value);
        }

        public string PauseButtonText
        {
            get => _pauseButtonText;
            set => SetProperty(ref _pauseButtonText, value);
        }

        public int CurrentVillage
        {
            get => _currentVillage;
            set
            {
                if (SetProperty(ref _currentVillage, value))
                {
                    _botService.CurrentVillage = value;
                    OnPropertyChanged(nameof(CurrentVillageName));
                    // Reload configs for the selected village
                    GeneralVM.LoadConfig();
                    ArmyVM.LoadConfig();
                }
            }
        }

        public string CurrentVillageName => $"Village {CurrentVillage}";

        public bool IsStartButtonEnabled => !_botService.IsRunning;
        public bool IsEndButtonEnabled => _botService.IsRunning;
        public bool IsPauseButtonEnabled => _botService.IsRunning;

        public MainViewModel(IBotService botService)
        {
            _botService = botService;

            // Instantiation
            GeneralVM = new GeneralViewModel(_botService);
            ArmyVM = new ArmyViewModel(_botService);
            MultiVillageVM = new MultiVillageViewModel(_botService);
            ClanGamesVM = new ClanGamesViewModel(_botService);
            ClanCapitalVM = new ClanCapitalViewModel(_botService);
            StatisticsVM = new StatisticsViewModel(_botService);
            LogsVM = new LogsViewModel(_botService);

            // Default Page
            _currentViewModel = GeneralVM;

            // Colors
            _statusPillBrush = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(132, 153, 184)); // Muted gray

            // Wire up events
            _botService.StatusChanged += OnBotStatusChanged;
            _botService.StatsUpdated += OnBotStatsUpdated;

            // Command Wiring
            NavigateGeneralCommand = new RelayCommand(() => CurrentViewModel = GeneralVM);
            NavigateArmyCommand = new RelayCommand(() => CurrentViewModel = ArmyVM);
            NavigateMultiVillageCommand = new RelayCommand(() => CurrentViewModel = MultiVillageVM);
            NavigateClanGamesCommand = new RelayCommand(() => CurrentViewModel = ClanGamesVM);
            NavigateClanCapitalCommand = new RelayCommand(() => CurrentViewModel = ClanCapitalVM);
            NavigateStatisticsCommand = new RelayCommand(() => CurrentViewModel = StatisticsVM);
            NavigateLogsCommand = new RelayCommand(() => CurrentViewModel = LogsVM);

            StartCommand = new RelayCommand(StartBot);
            StopCommand = new RelayCommand(StopBot);
            TogglePauseCommand = new RelayCommand(TogglePause);

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

            OnBotStatusChanged();
            OnBotStatsUpdated();
        }

        private void StartBot()
        {
            // Save configuration first
            GeneralVM.SaveConfig();
            ArmyVM.SaveConfig();

            _botService.StartBot();
        }

        private void StopBot()
        {
            _botService.StopBot();
        }

        private void TogglePause()
        {
            _botService.TogglePause();
        }

        private void OnBotStatusChanged()
        {
            StatusPillText = _botService.StatusText;
            
            // Neon Green for running, Yellow/Amber for paused, Muted Blue-gray for idle
            if (StatusPillText == "RUNNING")
            {
                StatusPillBrush = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(74, 222, 128)); // #4ADE80 Emerald status
                PauseButtonText = "PAUSE";
            }
            else if (StatusPillText == "PAUSED")
            {
                StatusPillBrush = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(250, 204, 21)); // #FACC15 Amber warning
                PauseButtonText = "RESUME";
            }
            else // IDLE
            {
                StatusPillBrush = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(148, 163, 184)); // #94A3B8 Muted text
                PauseButtonText = "PAUSE";
            }

            // Trigger updates of control button enabled states
            OnPropertyChanged(nameof(IsStartButtonEnabled));
            OnPropertyChanged(nameof(IsEndButtonEnabled));
            OnPropertyChanged(nameof(IsPauseButtonEnabled));
        }

        private void OnBotStatsUpdated()
        {
            UptimeText = _botService.UptimeText;
            MemoryUsageText = _botService.MemoryUsageText;
            SuccessRateText = _botService.SuccessRateText;
            AttacksCount = _botService.AttacksCount;
        }
    }
}
