using System;
using System.Threading;
using System.Threading.Tasks;
using CvAut.Models;

namespace CvAut.Services.Sessions
{
    /// <summary>
    /// In-process <see cref="IDeviceSession"/> that never touches ADB or the backend. Used to
    /// drive the UI shell when no real emulator is connected, so the TopBar/Sidebar/DevicePanel
    /// flow can be exercised end-to-end without a device. Emits synthetic log lines and status
    /// transitions on a timer.
    /// </summary>
    public sealed class MockDeviceSession : IDeviceSession
    {
        private static readonly string[] SampleLines =
        {
            "[FSM-CS] phase=home_check status=start",
            "[FSM-CS] phase=search status=found target gold=720000 elixir=510000 de=2400",
            "[FSM-CS] phase=attack status=deploy army=Dragon_Attack",
            "[FSM-CS] phase=battle status=stars=2 loot_gained=720000",
            "[FSM-CS] phase=return status=home",
        };

        private CancellationTokenSource? _cts;
        private Task? _loop;

        public string DeviceId { get; }

        public BotStatus Status { get; private set; } = BotStatus.Idle;

        public SessionStats Stats { get; } = new();

        public Task Completion => _loop ?? Task.CompletedTask;

        public event Action<BotStatus>? StatusChanged;
        public event Action<LogEntry>? LogReceived;
        public event Action<SessionStats>? StatsUpdated;

        public MockDeviceSession(string deviceId)
        {
            DeviceId = deviceId;
        }

        public Task StartAsync(CancellationToken ct = default)
        {
            SetStatus(BotStatus.Starting);
            _cts = new CancellationTokenSource();
            SetStatus(BotStatus.Running);
            _loop = Task.Run(() => EmitLoop(_cts.Token), ct);
            return Task.CompletedTask;
        }

        public Task PauseAsync(CancellationToken ct = default)
        {
            SetStatus(BotStatus.Paused);
            return Task.CompletedTask;
        }

        public Task ResumeAsync(CancellationToken ct = default)
        {
            SetStatus(BotStatus.Running);
            return Task.CompletedTask;
        }

        public Task StopAsync(CancellationToken ct = default)
        {
            SetStatus(BotStatus.Stopping);
            _cts?.Cancel();
            SetStatus(BotStatus.Stopped);
            return Task.CompletedTask;
        }

        private async Task EmitLoop(CancellationToken token)
        {
            int i = 0;
            try
            {
                while (!token.IsCancellationRequested)
                {
                    await Task.Delay(1500, token).ConfigureAwait(false);
                    if (Status != BotStatus.Running)
                    {
                        continue;
                    }

                    string line = SampleLines[i % SampleLines.Length];
                    i++;

                    LogReceived?.Invoke(new LogEntry(line, LogLevel.Info, DeviceId));

                    // Bump stats on the "battle" line so the UI shows live totals.
                    if (line.Contains("stars=", StringComparison.Ordinal))
                    {
                        Stats.Battles++;
                        Stats.Stars += 1;
                        Stats.Gold += 720_000;
                        Stats.Elixir += 510_000;
                        Stats.DarkElixir += 2_400;
                        StatsUpdated?.Invoke(Stats);
                    }
                }
            }
            catch (OperationCanceledException)
            {
                // Expected on stop.
            }
        }

        private void SetStatus(BotStatus value)
        {
            Status = value;
            StatusChanged?.Invoke(value);
        }

        public void Dispose()
        {
            _cts?.Cancel();
            _cts?.Dispose();
            SetStatus(BotStatus.Stopped);
        }
    }
}
