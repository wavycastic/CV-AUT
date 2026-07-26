using System;
using System.Collections.Generic;

namespace CvAut.Models
{
    /// <summary>
    /// One structured log line. Carries <see cref="DeviceId"/> so multi-device logs can be
    /// split into per-device buffers (no shared global log stream).
    /// </summary>
    /// <remarks>
    /// Split across partials: <c>LogEntry.Parsing.cs</c> reads the key=value fields,
    /// <c>LogEntry.Icons.cs</c> picks the row glyph and <c>LogEntry.Summary.cs</c> builds the
    /// Vietnamese sentence from <see cref="LogVocabulary"/>.
    /// </remarks>
    public sealed partial class LogEntry
    {
        public LogEntry(string message, LogLevel level = LogLevel.Info, string? deviceId = null)
        {
            Timestamp = DateTimeOffset.Now;
            Message = message;
            Level = level;
            DeviceId = deviceId;
            Fields = ParseFields(message);
            Module = ParseModule(message);
            Phase = GetField("phase");
            Status = GetField("status");
            Action = GetField("action");
            Reason = GetField("reason");
            Step = GetField("step");
        }

        public DateTimeOffset Timestamp { get; }

        public string Message { get; }

        public LogLevel Level { get; }

        /// <summary>Owning device id, or null for app-level lines.</summary>
        public string? DeviceId { get; }

        /// <summary>Short local time for console-style rows.</summary>
        public string TimeText => Timestamp.ToLocalTime().ToString("HH:mm:ss");

        public string LevelText => Level.ToShortText();

        public string Module { get; }

        public string? Phase { get; }

        public string? Status { get; }

        public string? Action { get; }

        public string? Reason { get; }

        public string? Step { get; }

        public IReadOnlyDictionary<string, string> Fields { get; }

        /// <summary>
        /// Xác định xem dòng log này có phù hợp để hiển thị ở Chế độ User hay không.
        /// Quy tắc chuẩn: Tự động ẩn toàn bộ Trace và Debug. Chỉ hiển thị từ Info trở lên (Info, Warning, Error, Critical).
        /// Hoàn toàn không phụ thuộc nội dung văn bản Message.
        /// </summary>
        public bool IsUserRelevant => Level >= LogLevel.Info;

        public string FormattedLevel => $"[{LevelText}]";

        public string FormattedModule => $"[{Module}]";

        public string CleanMessage
        {
            get
            {
                if (Message.StartsWith("[" + Module + "]", StringComparison.Ordinal))
                {
                    return Message.Substring(Module.Length + 2).Trim();
                }
                if (Message.StartsWith("[", StringComparison.Ordinal))
                {
                    int end = Message.IndexOf(']');
                    if (end > 1)
                    {
                        return Message.Substring(end + 1).Trim();
                    }
                }
                return Message;
            }
        }

        public string SearchText => Message + " " + Summary + " " + Module + " " + DeviceId;
    }
}
