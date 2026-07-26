using System;
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CvAut.Models;
using CvAut.Services;
using CvAut.Services.Configuration;

namespace CvAut.ViewModels
{
    /// <summary>
    /// Dashboard page view model. The active device is pushed here by the shell host
    /// so the page stays self-contained for binding.
    ///
    /// This file covers construction, page state and the emulator filter. The rest is split by
    /// concern: <c>DashboardViewModel.Visibility.cs</c> (computed pane flags and status text),
    /// <c>DashboardViewModel.DeviceConfig.cs</c> (per-device config panel workflow) and
    /// <c>DashboardViewModel.Devices.cs</c> (attached device collection and fleet commands).
    /// </summary>
    public partial class DashboardViewModel : ViewModelBase
    {
        [ObservableProperty]
        private string _title = "Thiết bị";

        [NotifyPropertyChangedFor(nameof(ShowActivePanel))]
        [NotifyPropertyChangedFor(nameof(ShowSelectionPane))]
        [NotifyPropertyChangedFor(nameof(ShowDeviceList))]
        [NotifyPropertyChangedFor(nameof(ShowEmptyState))]
        [ObservableProperty]
        private DeviceViewModel? _activeDevice;

        [ObservableProperty]
        private string _selectedEmulatorFilter = "BlueStacks";

        [NotifyPropertyChangedFor(nameof(ShowGridPane))]
        [NotifyPropertyChangedFor(nameof(ShowActivePanel))]
        [NotifyPropertyChangedFor(nameof(ShowSelectionPane))]
        [ObservableProperty]
        private bool _isGridMode;

        [NotifyPropertyChangedFor(nameof(IsIdle))]
        [NotifyPropertyChangedFor(nameof(IsDetecting))]
        [NotifyPropertyChangedFor(nameof(HasNoDevices))]
        [NotifyPropertyChangedFor(nameof(ShowDeviceList))]
        [NotifyPropertyChangedFor(nameof(ShowActivePanel))]
        [NotifyPropertyChangedFor(nameof(ShowGridPane))]
        [NotifyPropertyChangedFor(nameof(ShowSelectionPane))]
        [NotifyPropertyChangedFor(nameof(ShowConfiguringPanel))]
        [NotifyPropertyChangedFor(nameof(ShowEmptyState))]
        [NotifyPropertyChangedFor(nameof(ShowEmptyNotDetected))]
        [NotifyPropertyChangedFor(nameof(ShowEmptyNoDevices))]
        [NotifyPropertyChangedFor(nameof(IsRunning))]
        [NotifyPropertyChangedFor(nameof(IsStopped))]
        [NotifyPropertyChangedFor(nameof(IsPaused))]
        [NotifyPropertyChangedFor(nameof(HasError))]
        [NotifyPropertyChangedFor(nameof(StatusText))]
        [ObservableProperty]
        private DashboardDeviceState _state = DashboardDeviceState.Idle;

        [NotifyPropertyChangedFor(nameof(DeviceSummaryText))]
        [ObservableProperty]
        private int _deviceCount;

        [NotifyPropertyChangedFor(nameof(DeviceSummaryText))]
        [ObservableProperty]
        private int _readyCount;

        [NotifyPropertyChangedFor(nameof(StatusText))]
        [ObservableProperty]
        private string _detectDetail = string.Empty;

        [ObservableProperty]
        private IRelayCommand? _selectDeviceCommand;

        [ObservableProperty]
        private IRelayCommand<DeviceViewModel>? _showDeviceLogsCommand;

        public ObservableCollection<string> EmulatorFilters { get; } = new()
        {
            "Tất cả", "BlueStacks", "LDPlayer", "MEmu"
        };

        private IAsyncRelayCommand? _detectDevicesCommand;

        public IAsyncRelayCommand? DetectDevicesCommand
        {
            get => _detectDevicesCommand;
            set
            {
                if (SetProperty(ref _detectDevicesCommand, value))
                {
                    if (value is not null && value.CanExecute(null))
                    {
                        value.Execute(null);
                    }
                }
            }
        }

        partial void OnSelectedEmulatorFilterChanged(string value)
        {
            _appPreferences.SaveSelectedEmulatorFilter(value);
            if (_detectDevicesCommand is not null && _detectDevicesCommand.CanExecute(null))
            {
                _detectDevicesCommand.Execute(null);
            }
        }

        private readonly SettingsViewModel _settingsViewModel;
        private readonly IConfigStore _configStore;
        private readonly IAppPreferences _appPreferences;

        public SettingsViewModel SettingsViewModel => _settingsViewModel;

        public DashboardViewModel(
            SettingsViewModel settingsViewModel,
            IConfigStore configStore,
            IAppPreferences appPreferences)
        {
            _settingsViewModel = settingsViewModel;
            _settingsViewModel.IsInstanceMode = true;
            _settingsViewModel.InstanceSaveRequested += () => SaveDeviceConfigCommand.Execute(null);
            _settingsViewModel.InstanceCancelRequested += () => CancelDeviceConfigCommand.Execute(null);
            _configStore = configStore;
            _appPreferences = appPreferences;
            LoadPreferences();
        }

        public DashboardViewModel(SettingsViewModel settingsViewModel, IConfigStore configStore)
            : this(settingsViewModel, configStore, new JsonAppPreferences())
        {
        }

        public DashboardViewModel()
            : this(new SettingsViewModel(), new ConfigStore(), new JsonAppPreferences())
        {
        }

        private void LoadPreferences()
        {
            string filter = _appPreferences.LoadSelectedEmulatorFilter();
            if (EmulatorFilters.Contains(filter))
            {
                SelectedEmulatorFilter = filter;
            }
        }
    }
}
