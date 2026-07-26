using System;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CvAut.Models;

namespace CvAut.ViewModels
{
    /// <summary>
    /// Per-device config panel workflow: opening the settings panel for one device, mirroring the
    /// device's play mode into the shared <see cref="SettingsViewModel"/> while it is open, and
    /// saving or cancelling back to the device list.
    /// </summary>
    public partial class DashboardViewModel
    {
        [NotifyPropertyChangedFor(nameof(ShowConfiguringPanel))]
        [NotifyPropertyChangedFor(nameof(ShowSelectionPane))]
        [ObservableProperty]
        private DeviceViewModel? _selectedDeviceForConfig;

        private DeviceViewModel? _trackedDevice;

        partial void OnSelectedDeviceForConfigChanged(DeviceViewModel? value)
        {
            if (_trackedDevice is not null)
            {
                _trackedDevice.PropertyChanged -= OnTrackedDevicePropertyChanged;
            }

            _trackedDevice = value;

            if (_trackedDevice is not null)
            {
                _trackedDevice.PropertyChanged += OnTrackedDevicePropertyChanged;
            }
        }

        private void OnTrackedDevicePropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(DeviceViewModel.SelectedPlayMode) && _trackedDevice is not null)
            {
                _settingsViewModel.SelectedPlayMode = _trackedDevice.SelectedPlayMode;
            }
        }

        [RelayCommand]
        private void ConfigureDevice(DeviceViewModel device)
        {
            SelectedDeviceForConfig = device;
            string profileName = device.Device.ProfileKey;

            if (!_configStore.Profiles.Any(p => string.Equals(p.Name, profileName, StringComparison.OrdinalIgnoreCase)))
            {
                _configStore.LoadProfile("Default");
                var defaultTemplate = _configStore.LoadActiveConfig();
                _configStore.SaveProfileAs(profileName, defaultTemplate);
            }

            _configStore.LoadProfile(profileName);
            _settingsViewModel.LoadSelectedProfileDirectly(profileName, device.SelectedPlayMode);
            State = DashboardDeviceState.ConfiguringDevice;
        }

        [RelayCommand]
        private void SaveDeviceConfig()
        {
            if (SelectedDeviceForConfig is not null)
            {
                _settingsViewModel.UpdateProfileDirectly();
                SelectedDeviceForConfig.SelectedPlayMode = _settingsViewModel.SelectedPlayMode;
            }
            State = DashboardDeviceState.DeviceSelected;
            SelectedDeviceForConfig = null;
        }

        [RelayCommand]
        private void CancelDeviceConfig()
        {
            State = DashboardDeviceState.DeviceSelected;
            SelectedDeviceForConfig = null;
        }
    }
}
