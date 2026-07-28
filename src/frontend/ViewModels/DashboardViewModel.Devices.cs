using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.Input;
using CvAut.Models;

namespace CvAut.ViewModels
{
    /// <summary>
    /// The device collection the dashboard renders: it is owned by the shell host and attached
    /// here, so this half also carries the subscription bookkeeping that keeps the derived
    /// running/stopped flags accurate, plus the fleet commands the page exposes directly.
    /// </summary>
    public partial class DashboardViewModel
    {
        public ObservableCollection<DeviceViewModel>? Devices { get; private set; }

        /// <summary>Tracks whether the fleet was stopped, so "stopped" is only reported after a run
        /// rather than on a fresh page that has never started anything.</summary>
        private bool _hasBeenStopped;

        public event Action<string>? CopyDeviceLogsRequested;

        public void AttachDevices(ObservableCollection<DeviceViewModel> devices)
        {
            if (Devices != null)
            {
                Devices.CollectionChanged -= OnDevicesCollectionChanged;
                foreach (var dev in Devices)
                {
                    dev.PropertyChanged -= OnDevicePropertyChanged;
                }
            }

            Devices = devices;

            if (Devices != null)
            {
                Devices.CollectionChanged += OnDevicesCollectionChanged;
                foreach (var dev in Devices)
                {
                    dev.PropertyChanged += OnDevicePropertyChanged;
                }
            }

            NotifyDevicesChanged();
        }

        private void OnDevicesCollectionChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
        {
            if (e.OldItems != null)
            {
                foreach (DeviceViewModel item in e.OldItems)
                {
                    item.PropertyChanged -= OnDevicePropertyChanged;
                }
            }
            if (e.NewItems != null)
            {
                foreach (DeviceViewModel item in e.NewItems)
                {
                    item.PropertyChanged += OnDevicePropertyChanged;
                }
            }

            NotifyDevicesChanged();
        }

        private void OnDevicePropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(DeviceViewModel.Status) || e.PropertyName == nameof(DeviceViewModel.IsSelected))
            {
                if (sender is DeviceViewModel dev)
                {
                    if (dev.Status is BotStatus.Running or BotStatus.Starting)
                    {
                        _hasBeenStopped = false;
                    }
                    else if (dev.Status is BotStatus.Stopped or BotStatus.Stopping)
                    {
                        _hasBeenStopped = true;
                    }
                }

                NotifyDevicesChanged();
            }
        }

        public void NotifyDevicesChanged()
        {
            OnPropertyChanged(nameof(ShowGridPane));
            OnPropertyChanged(nameof(IsRunning));
            OnPropertyChanged(nameof(IsStopped));
            OnPropertyChanged(nameof(RunnableDeviceCount));
            OnPropertyChanged(nameof(DeviceSummaryText));
            OnPropertyChanged(nameof(SelectedDeviceCount));
            OnPropertyChanged(nameof(RunnableSelectedCount));
            OnPropertyChanged(nameof(StoppableSelectedCount));
            OnPropertyChanged(nameof(FleetSelectionText));
            OnPropertyChanged(nameof(FleetStartHint));
            OnPropertyChanged(nameof(FleetStopHint));
            StartAllCommand.NotifyCanExecuteChanged();
            StopAllCommand.NotifyCanExecuteChanged();
            SelectAllDevicesCommand.NotifyCanExecuteChanged();
            ClearDeviceSelectionCommand.NotifyCanExecuteChanged();
        }

        private bool CanSelectAllDevices() => Devices?.Any(device => !device.IsSelected) == true;

        [RelayCommand(CanExecute = nameof(CanSelectAllDevices))]
        private void SelectAllDevices()
        {
            if (Devices is null)
            {
                return;
            }

            foreach (DeviceViewModel device in Devices)
            {
                device.IsSelected = true;
            }
        }

        private bool CanClearDeviceSelection() => Devices?.Any(device => device.IsSelected) == true;

        [RelayCommand(CanExecute = nameof(CanClearDeviceSelection))]
        private void ClearDeviceSelection()
        {
            if (Devices is null)
            {
                return;
            }

            foreach (DeviceViewModel device in Devices)
            {
                device.IsSelected = false;
            }
        }

        private bool CanStartAll() => RunnableSelectedCount > 0;

        [RelayCommand(CanExecute = nameof(CanStartAll))]
        private async Task StartAllAsync()
        {
            DeviceViewModel[] devices = Devices?
                .Where(device => device.IsSelected && device.CanStart)
                .ToArray() ?? Array.Empty<DeviceViewModel>();

            if (devices.Length == 0)
            {
                return;
            }

            _hasBeenStopped = false;
            NotifyDevicesChanged();
            await Task.WhenAll(devices.Select(device => device.StartCommand.ExecuteAsync(null)));
            NotifyDevicesChanged();
        }

        private bool CanStopAll() => StoppableSelectedCount > 0;

        [RelayCommand(CanExecute = nameof(CanStopAll))]
        private async Task StopAllAsync()
        {
            DeviceViewModel[] devices = Devices?
                .Where(device => device.IsSelected && device.CanStop)
                .ToArray() ?? Array.Empty<DeviceViewModel>();

            if (devices.Length == 0)
            {
                return;
            }

            _hasBeenStopped = true;
            NotifyDevicesChanged();
            await Task.WhenAll(devices.Select(device => device.StopCommand.ExecuteAsync(null)));
            NotifyDevicesChanged();
        }

        [RelayCommand]
        private void DeselectDevice()
        {
            ActiveDevice = null;
            State = DashboardDeviceState.DeviceSelected;
        }

        [RelayCommand]
        private void CopyDeviceLogs(DeviceViewModel device)
        {
            CopyDeviceLogsRequested?.Invoke(BuildDeviceLogText(device));
        }

        private static string BuildDeviceLogText(DeviceViewModel device)
        {
            var builder = new StringBuilder();
            foreach (LogEntry entry in device.Logs)
            {
                builder.AppendLine($"[{entry.TimeText}] {entry.LevelText} {entry.Message}");
            }

            return builder.ToString().TrimEnd();
        }
    }
}
