using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CvAut.Models;

namespace CvAut.ViewModels
{
    /// <summary>
    /// Device fleet half of the shell view model: the device collection, the active-device
    /// pointer, the per-device event subscriptions that keep the TopBar summary honest in grid
    /// mode, and the All-commands. Shell composition lives in <c>MainWindowViewModel.cs</c>.
    /// </summary>
    public partial class MainWindowViewModel
    {
        /// <summary>Device-scoped view models — one per connected/configured device.</summary>
        public ObservableCollection<DeviceViewModel> Devices { get; } = new();

        [ObservableProperty]
        private DeviceViewModel? _activeDevice;

        /// <summary>Raised when <see cref="ActiveDevice"/> changes so listeners can re-subscribe.</summary>
        public event Action<DeviceViewModel?>? ActiveDeviceChanged;

        partial void OnActiveDeviceChanged(DeviceViewModel? value)
        {
            _appState.ActiveDeviceId = value?.DeviceId;
            _dashboard.ActiveDevice = value;
            ActiveDeviceChanged?.Invoke(value);

            if (value is not null)
            {
                SyncDashboardState(value.Status);
            }
            else
            {
                _dashboard.State = Devices.Count > 0 ? DashboardDeviceState.DeviceSelected : DashboardDeviceState.NoDevices;
            }
        }

        /// <summary>Any device's bot-status change refreshes the TopBar running summary so grid mode
        /// (multiple concurrent sessions) reports accurate running/paused counts, not just the active one.</summary>
        private void OnDeviceStatusChangedForSummary(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(DeviceViewModel.Status) || e.PropertyName == nameof(DeviceViewModel.IsSelected))
            {
                Dispatcher.UIThread.Post(() =>
                {
                    TopBar.RefreshSummary();
                    TopBar.UpdateStatusFromDevices(Devices);
                    TopBar.RefreshAggregate(Devices);
                    NotifyFleetCommands();
                });

                if (e.PropertyName == nameof(DeviceViewModel.Status) && _notifications is not null && sender is DeviceViewModel vm)
                {
                    _ = _notifications.NotifyStatusAsync(vm.DisplayName, vm.Status);
                }
            }
        }

        private void OnDeviceStatsChangedForAggregate(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            Dispatcher.UIThread.Post(() => TopBar.RefreshAggregate(Devices));
        }

        private void OnActiveDeviceStatusChanged(DeviceViewModel? device)
        {
            if (device is null)
            {
                return;
            }

            SyncDashboardState(device.Status);

            device.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(DeviceViewModel.Status))
                {
                    Dispatcher.UIThread.Post(() =>
                    {
                        TopBar.RefreshSummary();
                        if (ActiveDevice == device)
                        {
                            SyncDashboardState(device.Status);
                        }
                    });
                }
            };
        }

        /// <summary>Detect connected ADB devices and build a <see cref="DeviceViewModel"/> for each.</summary>
        [RelayCommand]
        private async Task DetectDevicesAsync()
        {
            _dashboard.State = DashboardDeviceState.Detecting;
            _dashboard.DetectDetail = string.Empty;
            try
            {
                var found = await _discovery.DiscoverAsync(_dashboard.SelectedEmulatorFilter);
                foreach (DeviceViewModel existing in Devices)
                {
                    existing.PropertyChanged -= OnDeviceStatusChangedForSummary;
                    existing.Stats.PropertyChanged -= OnDeviceStatsChangedForAggregate;
                }

                Devices.Clear();
                _appState.Devices.Clear();

                foreach (Device device in found)
                {
                    _appState.Devices.Add(device);
                    var vm = new DeviceViewModel(device, (dev, cfgPath) => _sessions.GetOrCreate(dev, cfgPath), _configStore, _discovery);
                    vm.PropertyChanged += OnDeviceStatusChangedForSummary;
                    vm.Stats.PropertyChanged += OnDeviceStatsChangedForAggregate;
                    Devices.Add(vm);
                }

                _logs.SetDevices(Devices);
                _dashboard.NotifyDevicesChanged();
                TopBar.RefreshAggregate(Devices);
                NotifyFleetCommands();
                _dashboard.DeviceCount = Devices.Count;
                _dashboard.ReadyCount = Devices.Count(d => d.Device.Status == DeviceStatus.Ready);

                ActiveDevice = null;
                TopBar.RefreshSummary();

                if (Devices.Count > 0)
                {
                    _dashboard.State = DashboardDeviceState.DeviceSelected;
                }
                else
                {
                    _dashboard.State = DashboardDeviceState.NoDevices;
                }
            }
            catch (Exception ex)
            {
                _dashboard.DetectDetail = ex.Message;
                _dashboard.State = DashboardDeviceState.NoDevices;
            }
        }

        private bool CanStartAll() => Devices.Any(vm => vm.IsSelected && vm.StartCommand.CanExecute(null));
        private bool CanPauseAll() => Devices.Any(vm => vm.IsSelected && vm.PauseCommand.CanExecute(null));
        private bool CanStopAll() => Devices.Any(vm => vm.IsSelected && vm.StopCommand.CanExecute(null));

        private void NotifyFleetCommands()
        {
            StartAllCommand.NotifyCanExecuteChanged();
            PauseAllCommand.NotifyCanExecuteChanged();
            StopAllCommand.NotifyCanExecuteChanged();
        }

        /// <summary>Start every device that can start. Sessions are created lazily inside each
        /// DeviceViewModel.Start, so All-commands must iterate Devices — not the session manager,
        /// which is empty until a device has started at least once.</summary>
        [RelayCommand(CanExecute = nameof(CanStartAll))]
        private async Task StartAll()
        {
            foreach (DeviceViewModel vm in Devices)
            {
                if (vm.IsSelected && vm.StartCommand.CanExecute(null))
                {
                    await vm.StartCommand.ExecuteAsync(null);
                }
            }

            TopBar.RefreshSummary();
            NotifyFleetCommands();
        }

        [RelayCommand(CanExecute = nameof(CanPauseAll))]
        private async Task PauseAll()
        {
            foreach (DeviceViewModel vm in Devices)
            {
                if (vm.PauseCommand.CanExecute(null))
                {
                    await vm.PauseCommand.ExecuteAsync(null);
                }
            }

            TopBar.RefreshSummary();
            NotifyFleetCommands();
        }

        [RelayCommand(CanExecute = nameof(CanStopAll))]
        private async Task StopAll()
        {
            foreach (DeviceViewModel vm in Devices)
            {
                if (vm.IsSelected && vm.StopCommand.CanExecute(null))
                {
                    await vm.StopCommand.ExecuteAsync(null);
                }
            }

            TopBar.RefreshSummary();
            NotifyFleetCommands();
        }

        [RelayCommand]
        private void SelectDevice(DeviceViewModel vm)
        {
            ActiveDevice = vm;
        }

        [RelayCommand]
        private void ShowDeviceLogs(DeviceViewModel vm)
        {
            Logs.ShowDevice(vm);
        }
    }
}
