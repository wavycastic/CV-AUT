using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CvAut.Configuration;
using CvAut.Models;
using CvAut.Services;
using CvAut.Services.Configuration;
using CvAut.Services.Sessions;

namespace CvAut.ViewModels
{
    /// <summary>
    /// The bot lifecycle exposed to the UI. Each command swallows its exception into the device's
    /// own log instead of surfacing a dialog, so one device failing never disturbs the others.
    /// </summary>
    public partial class DeviceViewModel
    {
        // Start is allowed when the bot is idle/stopped and either ADB is already online,
        // the emulator install is known, or an offline ADB endpoint still carries a known
        // emulator executable. In the latter two cases EmulatorBootstrapper launches/restarts
        // the exact instance and waits for ADB; an already-ready instance is reused as-is.
        public bool CanStart => Status is BotStatus.Idle or BotStatus.Stopped
            && DeviceCanStart;

        /// <summary>Device-side start eligibility, independent of bot status (for UI hints).</summary>
        public bool DeviceCanStart => Device.Status is DeviceStatus.Ready or DeviceStatus.Installed
            || (Device.Status == DeviceStatus.Offline && Device.CanAutoStart);
        public bool CanPause => Status is BotStatus.Running;
        public bool CanResume => Status is BotStatus.Paused;
        public bool CanStop => Status is BotStatus.Running or BotStatus.Paused or BotStatus.Starting;

        [RelayCommand(CanExecute = nameof(CanStart))]
        private async Task StartAsync()
        {
            try
            {
                // Item 8: session is built lazily on first Start so the active config is written
                // (and read by CVAutomationFramework) with the user's final selection — never a
                // stale host/port captured during Detect.
                if (_session is null && _startHandler is not null)
                {
                    // Phase 3: each device gets its own config file whose device_connection points at
                    // this device, so concurrent sessions never share a host/port or clobber each other.
                    // The provider prepares that file and stamps the selected play mode into it.
                    string configPath = _configSnapshots.PrepareDevice(Device, SelectedVillagePlayMode);
                    IDeviceSession session = _startHandler(Device, configPath);
                    AttachSession(session);
                }

                if (_session is null)
                {
                    return;
                }

                await WarnIfDisplayMismatchAsync();

                await _session.StartAsync();
            }
            catch (Exception ex)
            {
                AddLog("Khởi động thất bại: " + ex.Message, LogLevel.Error);
            }
        }

        [RelayCommand(CanExecute = nameof(CanPause))]
        private async Task PauseAsync()
        {
            if (_session is null)
            {
                return;
            }

            try
            {
                await _session.PauseAsync();
            }
            catch (Exception ex)
            {
                AddLog("Tạm dừng thất bại: " + ex.Message, LogLevel.Error);
            }
        }

        [RelayCommand(CanExecute = nameof(CanResume))]
        private async Task ResumeAsync()
        {
            if (_session is null)
            {
                return;
            }

            try
            {
                await _session.ResumeAsync();
            }
            catch (Exception ex)
            {
                AddLog("Tiếp tục thất bại: " + ex.Message, LogLevel.Error);
            }
        }

        [RelayCommand(CanExecute = nameof(CanStop))]
        private async Task StopAsync()
        {
            if (_session is null)
            {
                return;
            }

            try
            {
                await _session.StopAsync();
            }
            catch (Exception ex)
            {
                AddLog("Dừng thất bại: " + ex.Message, LogLevel.Error);
            }
        }

        [RelayCommand]
        private void ClearLog()
        {
            Logs.Clear();
            OnPropertyChanged(nameof(HasLogs));
        }
    }
}
