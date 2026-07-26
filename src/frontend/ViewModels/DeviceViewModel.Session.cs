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
    /// Session subscription and the per-device log buffer. Every session event is posted to the
    /// UI thread before it touches state, because sessions raise them from background work.
    /// </summary>
    public partial class DeviceViewModel
    {
        private const int MaxLogEntries = 500;

        /// <summary>Per-device log buffer (never shared across devices).</summary>
        public ObservableCollection<LogEntry> Logs { get; } = new();

        public bool HasLogs => Logs.Count > 0;

        /// <summary>
        /// Subscribes session events and seeds initial status/stats. Called by Start once the
        /// session has been built with the up-to-date config, or by the design-time ctor.
        /// </summary>
        public void AttachSession(IDeviceSession session)
        {
            if (_session is not null)
            {
                Detach();
            }

            _session = session;
            _stats.Apply(_session.Stats);
            _session.StatusChanged += OnSessionStatusChanged;
            _session.LogReceived += OnLogReceived;
            _session.StatsUpdated += OnStatsUpdated;
            Status = _session.Status;
        }

        /// <summary>Detach session event handlers. Called by the manager when removing this device.</summary>
        public void Detach()
        {
            if (_session is null)
            {
                return;
            }

            _session.StatusChanged -= OnSessionStatusChanged;
            _session.LogReceived -= OnLogReceived;
            _session.StatsUpdated -= OnStatsUpdated;
        }

        /// <summary>
        /// Appends one locally produced entry. <see cref="HasLogs"/> is computed from
        /// <see cref="Logs"/> rather than stored, so it has to be raised by hand on every write.
        /// </summary>
        private void AddLog(string message, LogLevel level)
        {
            Logs.Add(new LogEntry(message, level, DeviceId));
            OnPropertyChanged(nameof(HasLogs));
        }

        private void OnSessionStatusChanged(BotStatus status)
        {
            Dispatcher.UIThread.Post(() => Status = status);
        }

        private void OnLogReceived(LogEntry entry)
        {
            Dispatcher.UIThread.Post(() =>
            {
                Logs.Add(entry);
                while (Logs.Count > MaxLogEntries)
                {
                    Logs.RemoveAt(0);
                }

                OnPropertyChanged(nameof(HasLogs));
            });
        }

        private void OnStatsUpdated(SessionStats stats)
        {
            Dispatcher.UIThread.Post(() => _stats.Apply(stats));
        }
    }
}
