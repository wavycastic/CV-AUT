using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.IO;
using System.Linq;
using System.Text;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CvAut.Models;
using Avalonia;

namespace CvAut.ViewModels
{
    public partial class LogsViewModel : ViewModelBase
    {
        private DeviceViewModel? _subscribedDevice;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(GridMargin))]
        private bool _isDialogMode;

        public Thickness GridMargin => IsDialogMode ? new Thickness(0) : new Thickness(24);

        [ObservableProperty] private string _title = "Nhật ký";
        [ObservableProperty] private DeviceViewModel? _selectedDevice;
        [ObservableProperty] private LogLevel? _levelFilter;
        [ObservableProperty] private string _searchText = string.Empty;
        [ObservableProperty] private bool _autoScroll = true;
        [ObservableProperty] private string _status = "Sẵn sàng";
        [ObservableProperty] private bool _isOpen;

        public bool HasFilteredLogs => FilteredLogs.Count > 0;

        public ObservableCollection<DeviceViewModel> Devices { get; } = new();
        public ObservableCollection<LogEntry> FilteredLogs { get; } = new();
        public ObservableCollection<LogLevel?> LevelFilters { get; } = new() { null, LogLevel.Debug, LogLevel.Info, LogLevel.Warning, LogLevel.Error };

        public event Action<string>? CopyRequested;

        public void SetDevices(IEnumerable<DeviceViewModel> devices)
        {
            Devices.Clear();
            foreach (DeviceViewModel device in devices)
            {
                Devices.Add(device);
            }

            var firstDevice = Devices.FirstOrDefault();
            if (SelectedDevice == firstDevice)
            {
                UnsubscribeLogBuffer();
                SubscribeLogBuffer();
            }
            else
            {
                SelectedDevice = firstDevice;
            }
        }

        public void ShowDevice(DeviceViewModel device)
        {
            IsDialogMode = true;
            SetDevices(new[] { device });
            SelectedDevice = device;
            IsOpen = true;
            Refresh();
        }

        [RelayCommand]
        private void Close()
        {
            IsOpen = false;
        }

        partial void OnSelectedDeviceChanged(DeviceViewModel? value)
        {
            UnsubscribeLogBuffer();
            SubscribeLogBuffer();
            Refresh();
        }

        partial void OnLevelFilterChanged(LogLevel? value)
        {
            Refresh();
        }

        partial void OnSearchTextChanged(string value)
        {
            Refresh();
        }

        [RelayCommand]
        public void Refresh()
        {
            FilteredLogs.Clear();
            OnPropertyChanged(nameof(HasFilteredLogs));
            if (SelectedDevice is null)
            {
                Status = "Chưa chọn thiết bị.";
                return;
            }

            foreach (LogEntry entry in SelectedDevice.Logs.Where(Matches))
            {
                FilteredLogs.Add(entry);
            }

            Status = $"{FilteredLogs.Count} dòng nhật ký.";
            OnPropertyChanged(nameof(HasFilteredLogs));
        }

        [RelayCommand]
        private void Copy()
        {
            string text = BuildLogText();
            if (string.IsNullOrWhiteSpace(text))
            {
                Status = "Không có nhật ký để sao chép.";
                return;
            }

            CopyRequested?.Invoke(text);
            Status = "Đã sao chép nhật ký vào bộ nhớ tạm.";
        }

        [RelayCommand]
        private void Export()
        {
            string text = BuildLogText();
            if (string.IsNullOrWhiteSpace(text))
            {
                Status = "Không có nhật ký để xuất.";
                return;
            }

            string root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AutoClashOfClan20206", "logs");
            Directory.CreateDirectory(root);
            string deviceId = SanitizeFileName(SelectedDevice?.DeviceId ?? "device");
            string path = Path.Combine(root, $"{deviceId}_{DateTimeOffset.Now:yyyyMMdd_HHmmss}.log");
            File.WriteAllText(path, text, Encoding.UTF8);
            Status = "Đã xuất file: " + path;
        }

        private void SubscribeLogBuffer()
        {
            if (SelectedDevice is null)
            {
                return;
            }

            _subscribedDevice = SelectedDevice;
            _subscribedDevice.Logs.CollectionChanged += OnDeviceLogsChanged;
        }

        private void UnsubscribeLogBuffer()
        {
            if (_subscribedDevice is not null)
            {
                _subscribedDevice.Logs.CollectionChanged -= OnDeviceLogsChanged;
                _subscribedDevice = null;
            }
        }

        private void OnDeviceLogsChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            if (e.Action == NotifyCollectionChangedAction.Add && e.NewItems is not null)
            {
                foreach (LogEntry entry in e.NewItems.OfType<LogEntry>().Where(Matches))
                {
                    FilteredLogs.Add(entry);
                }

                Status = $"{FilteredLogs.Count} dòng nhật ký.";
                OnPropertyChanged(nameof(HasFilteredLogs));
                return;
            }

            Refresh();
        }

        private string BuildLogText()
        {
            var builder = new StringBuilder();
            foreach (LogEntry entry in FilteredLogs)
            {
                builder.Append(entry.Timestamp.ToString("O"))
                    .Append('\t')
                    .Append(entry.Level)
                    .Append('\t')
                    .Append(entry.DeviceId)
                    .Append('\t')
                    .AppendLine(entry.Message);
            }

            return builder.ToString();
        }

        private bool Matches(LogEntry entry)
        {
            if (LevelFilter is not null && entry.Level != LevelFilter)
            {
                return false;
            }

            if (!string.IsNullOrWhiteSpace(SearchText) && !entry.SearchText.Contains(SearchText, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            return true;
        }

        private static string SanitizeFileName(string value)
        {
            foreach (char invalid in Path.GetInvalidFileNameChars())
            {
                value = value.Replace(invalid, '_');
            }

            return value.Replace(':', '_');
        }
    }
}
