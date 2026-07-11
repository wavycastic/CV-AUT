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
    /// - stats: parses structured backend log lines and raises <see cref="StatsUpdated"/> so the
    ///   per-device UI can update battles, loot, walls and clan games totals in real time.
    /// </summary>
    public sealed class AutomationRunnerSession : IDeviceSession
    {
        private readonly AutomationRunner _runner;
        private readonly Action<string, string?>? _onGlobalLog;

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

            // Per-device attribution (Phase 3): subscribe to the context-aware tee and accept only
            // lines whose ambient device scope matches ours. StartAsync sets AppLog.DeviceContext on the
            // worker-starting thread, and ExecutionContext flows that scope into every nested Task the
            // backend spawns, so concurrent devices no longer cross-tag each other's logs or stats.
            _onGlobalLog = (line, ctx) =>
            {
                if (string.Equals(ctx, DeviceId, StringComparison.Ordinal))
                {
                    OnLogLine(line);
                }
            };
            AppLog.LineWrittenWithContext += _onGlobalLog;
        }

        public Task StartAsync(CancellationToken ct = default)
        {
            SetStatus(BotStatus.Starting);
            return Task.Run(() =>
            {
                try
                {
                    // Tag this execution context so every line the backend worker logs (across nested
                    // Tasks) is attributed to this device via ExecutionContext flow.
                    AppLog.DeviceContext.Value = DeviceId;
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
                    AppLog.DeviceContext.Value = DeviceId;
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
                    AppLog.DeviceContext.Value = DeviceId;
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
                    AppLog.DeviceContext.Value = DeviceId;
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
            ApplyStatsFromLog(line);
        }

        private void ApplyStatsFromLog(string line)
        {
            if (line.Contains("phase=battle_stats", StringComparison.OrdinalIgnoreCase))
            {
                int stars = ParseInt(line, "stars=");
                int gold = ParseInt(line, "gold=");
                int elixir = ParseInt(line, "elixir=");
                int de = ParseInt(line, "dark_elixir=");
                if (gold > 0 || elixir > 0 || de > 0 || stars > 0)
                {
                    Stats.Battles += 1;
                    Stats.Stars += stars;
                    Stats.Gold += gold;
                    Stats.Elixir += elixir;
                    Stats.DarkElixir += de;
                    StatsUpdated?.Invoke(Stats);
                }
            }
            else if (line.Contains("phase=wall_stats", StringComparison.OrdinalIgnoreCase))
            {
                int walls = ParseInt(line, "count=");
                if (walls > 0)
                {
                    Stats.WallsUpgraded += walls;
                    StatsUpdated?.Invoke(Stats);
                }
            }
            else if (line.Contains("clan_games", StringComparison.OrdinalIgnoreCase) ||
                     line.Contains("phase=clan", StringComparison.OrdinalIgnoreCase))
            {
                int points = ParseInt(line, "points=");
                if (points == 0)
                {
                    points = ParseInt(line, "clan_games_points=");
                }

                int tasks = ParseInt(line, "tasks=");
                if (tasks == 0 && line.Contains("task", StringComparison.OrdinalIgnoreCase) &&
                    (line.Contains("status=success", StringComparison.OrdinalIgnoreCase) || line.Contains("status=complete", StringComparison.OrdinalIgnoreCase)))
                {
                    tasks = 1;
                }

                if (points > 0 || tasks > 0)
                {
                    Stats.ClanGamesPoints += points;
                    Stats.ClanGamesTasks += tasks;
                    StatsUpdated?.Invoke(Stats);
                }
            }
        }

        private static int ParseInt(string message, string key)
        {
            int start = message.IndexOf(key, StringComparison.OrdinalIgnoreCase);
            if (start < 0)
            {
                return 0;
            }

            start += key.Length;
            int end = start;
            while (end < message.Length && char.IsDigit(message[end]))
            {
                end++;
            }

            return int.TryParse(message.AsSpan(start, end - start), out int value) ? value : 0;
        }

        private static LogLevel Classify(string line)
        {
            if (line.Contains("[ERROR]", StringComparison.OrdinalIgnoreCase) ||
                line.Contains(" ERROR]", StringComparison.OrdinalIgnoreCase) ||
                line.Contains("status=fail", StringComparison.OrdinalIgnoreCase))
            {
                return LogLevel.Error;
            }

            if (line.Contains("[WARNING]", StringComparison.OrdinalIgnoreCase) ||
                line.Contains(" WARNING]", StringComparison.OrdinalIgnoreCase) ||
                line.Contains("WARNING", StringComparison.OrdinalIgnoreCase) ||
                line.Contains("status=retry", StringComparison.OrdinalIgnoreCase))
            {
                return LogLevel.Warning;
            }

            return LogLevel.Info;
        }

        public void Dispose()
        {
            if (_onGlobalLog is not null)
            {
                AppLog.LineWrittenWithContext -= _onGlobalLog;
            }

            _runner.Dispose();
            SetStatus(BotStatus.Stopped);
        }
    }
}
