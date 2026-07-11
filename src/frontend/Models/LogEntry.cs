using System;
using System.Collections.Generic;
using System.Text;

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

        public string LevelText => Level switch
        {
            LogLevel.Debug => "DBG",
            LogLevel.Info => "INF",
            LogLevel.Warning => "WRN",
            LogLevel.Error => "ERR",
            _ => Level.ToString().ToUpperInvariant(),
        };

        public string Module { get; }

        public string? Phase { get; }

        public string? Status { get; }

        public string? Action { get; }

        public string? Reason { get; }

        public string? Step { get; }

        public IReadOnlyDictionary<string, string> Fields { get; }

        /// <summary>
        /// Human-focused summary assembled from the structured key=value fields used by the backend.
        /// Keeps the raw message intact while making the UI much easier to scan during debugging.
        /// </summary>
        public string Summary
        {
            get
            {
                var parts = new List<string>();
                Add("phase", Phase);
                Add("step", Step);
                Add("status", Status);
                Add("action", Action);
                Add("reason", Reason);
                Add("details", GetField("details"));
                return parts.Count == 0 ? Message : string.Join(" | ", parts);

                void Add(string name, string? value)
                {
                    if (!string.IsNullOrWhiteSpace(value))
                    {
                        parts.Add(name + "=" + value);
                    }
                }
            }
        }

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

        public string SearchText => Message + " " + Summary + " " + Module;

        private string? GetField(string key)
            => Fields.TryGetValue(key, out string? value) ? value : null;

        private static string ParseModule(string message)
        {
            if (message.StartsWith("[", StringComparison.Ordinal))
            {
                int end = message.IndexOf(']');
                if (end > 1)
                {
                    return message.Substring(1, end - 1);
                }
            }

            return "APP";
        }

        private static IReadOnlyDictionary<string, string> ParseFields(string message)
        {
            var fields = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            int i = 0;
            while (i < message.Length)
            {
                while (i < message.Length && char.IsWhiteSpace(message[i]))
                {
                    i++;
                }

                int keyStart = i;
                while (i < message.Length && (char.IsLetterOrDigit(message[i]) || message[i] == '_' || message[i] == '-'))
                {
                    i++;
                }

                if (i <= keyStart || i >= message.Length || message[i] != '=')
                {
                    i++;
                    continue;
                }

                string key = message.Substring(keyStart, i - keyStart);
                i++; // '='
                string value;
                if (i < message.Length && message[i] == '"')
                {
                    i++;
                    var sb = new StringBuilder();
                    while (i < message.Length)
                    {
                        char c = message[i++];
                        if (c == '"')
                        {
                            break;
                        }

                        sb.Append(c);
                    }

                    value = sb.ToString();
                }
                else
                {
                    int valueStart = i;
                    while (i < message.Length && !char.IsWhiteSpace(message[i]))
                    {
                        i++;
                    }

                    value = message.Substring(valueStart, i - valueStart);
                }

                if (!string.IsNullOrWhiteSpace(key))
                {
                    fields[key] = value;
                }
            }

            return fields;
        }
    }
}
