using System;
using System.Threading;
using System.Threading.Tasks;
using CvAut.Models;

namespace CvAut.Services.Sessions
{
    /// <summary>
    /// One bot session bound to a single device. The UI's <c>DeviceViewModel</c> talks to the
    /// backend only through this abstraction — it never calls ADB / <c>CVAutomationFramework</c>
    /// directly (roadmap principle: "ViewModel không gọi ADB trực tiếp; chỉ qua IDeviceSession").
    ///
    /// Per spec §8. Phase 1 ships a single-session <see cref="IDeviceSessionManager"/>; the
    /// interface itself is multi-ready (no global runtime state, every event carries DeviceId).
    /// </summary>
    public interface IDeviceSession : IDisposable
    {
        /// <summary>Runtime scope key (host:port or ADB serial). Matches <see cref="Device.Id"/>.</summary>
        string DeviceId { get; }

        /// <summary>Current lifecycle/health state of this session.</summary>
        BotStatus Status { get; }

        /// <summary>Per-device running totals. Updated via <see cref="StatsUpdated"/>.</summary>
        SessionStats Stats { get; }

        /// <summary>Completes when the background worker exits (stop, cancel, or fault).</summary>
        Task Completion { get; }

        /// <summary>Raised (background thread) when <see cref="Status"/> transitions.</summary>
        event Action<BotStatus>? StatusChanged;

        /// <summary>
        /// Raised (background thread) for each log line produced by this session.
        /// Every entry carries <see cref="LogEntry.DeviceId"/> so multi-device logs can be split.
        /// Subscribers must marshal to the UI thread.
        /// </summary>
        event Action<LogEntry>? LogReceived;

        /// <summary>Raised (background thread) when <see cref="Stats"/> changes.</summary>
        event Action<SessionStats>? StatsUpdated;

        Task StartAsync(CancellationToken ct = default);

        Task PauseAsync(CancellationToken ct = default);

        Task ResumeAsync(CancellationToken ct = default);

        Task StopAsync(CancellationToken ct = default);
    }
}
