using System;

namespace CvAut.Models
{
    public enum LogLevel
    {
        Debug,
        Info,
        Warning,
        Error,
    }

    /// <summary>
    /// One structured log line. Carries <see cref="DeviceId"/> so multi-device logs can be
    /// split into per-device buffers (no shared global log stream).
    /// </summary>
    public sealed class LogEntry
    {
        public LogEntry(string message, LogLevel level = LogLevel.Info, string? deviceId = null)
        {
            Timestamp = DateTimeOffset.Now;
            Message = message;
            Level = level;
            DeviceId = deviceId;
        }

        public DateTimeOffset Timestamp { get; }

        public string Message { get; }

        public LogLevel Level { get; }

        /// <summary>Owning device id, or null for app-level lines.</summary>
        public string? DeviceId { get; }
    }
}
