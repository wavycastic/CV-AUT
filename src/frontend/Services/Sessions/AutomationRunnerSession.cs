using System;
using System.Threading;
using System.Threading.Tasks;
using CvAut.Models;

namespace CvAut.Services.Sessions
{
    /// <summary>
    /// <see cref="IDeviceSession"/> backed by the real <c>CVAutomationFramework</c> engine.
    ///
    /// Bridges the backend's sync, console-logging, file-stats API to the session abstraction:
    /// - wraps <c>Start/Stop/Pause/Resume</c> in async and emits <see cref="StatusChanged"/>;
    /// - subscribes the global <c>AppLog.LineWritten</c> and re-tags each line with
    ///   <see cref="DeviceId"/> via <see cref="LogReceived"/> (multi-device log split — the
    ///   global Console tee is a known Phase 3 blocker; for the single session in Phase 1 it
    ///   is correct because only this device is running).
    /// - stats: the backend writes per-village stat files to disk (see <c>StatsFilePath</c>);
    ///   surfacing them as <see cref="StatsUpdated"/> events is Phase 2, so this impl does not
    ///   raise stats yet.
    /// </summary>
    public sealed class AutomationRunnerSession : IDeviceSession
    {
        private readonly AutomationRunner _runner;
        private readonly Action<string>? _onGlobalLog;

        public string DeviceId { get; }

        public BotStatus Status { get; private set; } = BotStatus.Idle;

        public SessionStats Stats { get; } = new();

        public Task Completion => _runner.Completion;

        public event Action<BotStatus>? StatusChanged;
        public event Action<LogEntry>? LogReceived;
        public event Action<SessionStats>? StatsUpdated;

        /// <param name="deviceId">Scope key (host:port or serial) — must match the device the
        /// config file points at, since the backend reads host/port from the file.</param>
        /// <param name="configPath">Config file the backend loads. Its <c>device_connection</c>
        /// host/port must correspond to <paramref name="deviceId"/>.</param>
        public AutomationRunnerSession(string deviceId, string configPath)
        {
            DeviceId = deviceId;
            _runner = new AutomationRunner(configPath);

            // Tap the global Console tee and re-tag each line with our DeviceId.
            // (Phase 1: only one session runs, so attribution is unambiguous. Phase 3 must
            //  replace the global tap with per-session structured events.)
            _onGlobalLog = line => OnLogLine(line);
            AppLog.LineWritten += _onGlobalLog;
        }

        public Task StartAsync(CancellationToken ct = default)
        {
            SetStatus(BotStatus.Starting);
            return Task.Run(() =>
            {
                try
                {
                    _runner.Start();
                    SetStatus(BotStatus.Running);
                }
                catch (Exception ex)
                {
                    OnLogLine("Start failed: " + ex.Message, LogLevel.Error);
                    SetStatus(BotStatus.Error);
                    throw;
                }
            }, ct);
        }

        public Task PauseAsync(CancellationToken ct = default)
        {
            return Task.Run(() =>
            {
                try
                {
                    _runner.Pause();
                    SetStatus(BotStatus.Paused);
                }
                catch (Exception ex)
                {
                    OnLogLine("Pause failed: " + ex.Message, LogLevel.Error);
                    SetStatus(BotStatus.Error);
                    throw;
                }
            }, ct);
        }

        public Task ResumeAsync(CancellationToken ct = default)
        {
            return Task.Run(() =>
            {
                try
                {
                    _runner.Resume();
                    SetStatus(BotStatus.Running);
                }
                catch (Exception ex)
                {
                    OnLogLine("Resume failed: " + ex.Message, LogLevel.Error);
                    SetStatus(BotStatus.Error);
                    throw;
                }
            }, ct);
        }

        public Task StopAsync(CancellationToken ct = default)
        {
            SetStatus(BotStatus.Stopping);
            return Task.Run(() =>
            {
                try
                {
                    _runner.Stop();
                    SetStatus(BotStatus.Stopped);
                }
                catch (Exception ex)
                {
                    OnLogLine("Stop failed: " + ex.Message, LogLevel.Error);
                    SetStatus(BotStatus.Error);
                    throw;
                }
            }, ct);
        }

        private void SetStatus(BotStatus value)
        {
            Status = value;
            StatusChanged?.Invoke(value);
        }

        private void OnLogLine(string line, LogLevel level = LogLevel.Info)
        {
            // Heuristic: the backend logs errors/warnings with [ERROR]/[FSM-CS ERROR]/[WARNING].
            if (level == LogLevel.Info)
            {
                level = Classify(line);
            }

            LogReceived?.Invoke(new LogEntry(line, level, DeviceId));
        }

        private static LogLevel Classify(string line)
        {
            if (line.Contains("[ERROR]", StringComparison.OrdinalIgnoreCase) ||
                line.Contains("status=fail", StringComparison.OrdinalIgnoreCase))
            {
                return LogLevel.Error;
            }

            if (line.Contains("[WARNING]", StringComparison.OrdinalIgnoreCase) ||
                line.Contains("WARNING", StringComparison.OrdinalIgnoreCase))
            {
                return LogLevel.Warning;
            }

            return LogLevel.Info;
        }

        public void Dispose()
        {
            if (_onGlobalLog is not null)
            {
                AppLog.LineWritten -= _onGlobalLog;
            }

            _runner.Dispose();
            SetStatus(BotStatus.Stopped);
        }
    }
}
