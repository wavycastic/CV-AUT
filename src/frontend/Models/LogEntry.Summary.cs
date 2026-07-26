using System;
using System.Text;

namespace CvAut.Models
{
    /// <summary>
    /// Turns the parsed fields into text. <see cref="DevStructuredText"/> keeps the raw
    /// key=value shape; <see cref="Summary"/> builds a Vietnamese sentence using the
    /// phrase book in <see cref="LogVocabulary"/>.
    /// </summary>
    public sealed partial class LogEntry
    {
        /// <summary>
        /// Nhật ký dành cho Dev (Structured Text).
        /// </summary>
        public string DevStructuredText
        {
            get
            {
                if (Fields.Count == 0) return Message;
                var sb = new StringBuilder();
                sb.Append($"[{Module}] ");
                if (!string.IsNullOrWhiteSpace(Action)) sb.Append($"{Action} ");
                if (!string.IsNullOrWhiteSpace(Status)) sb.Append($"status={Status} ");
                if (!string.IsNullOrWhiteSpace(Reason)) sb.Append($"reason={Reason} ");

                foreach (var kv in Fields)
                {
                    if (kv.Key.Equals("phase", StringComparison.OrdinalIgnoreCase) ||
                        kv.Key.Equals("status", StringComparison.OrdinalIgnoreCase) ||
                        kv.Key.Equals("action", StringComparison.OrdinalIgnoreCase) ||
                        kv.Key.Equals("reason", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }
                    sb.Append($"{kv.Key}={kv.Value} ");
                }
                return sb.ToString().TrimEnd();
            }
        }

        /// <summary>
        /// Human-focused summary assembled from the structured key=value fields used by the backend.
        /// Keeps the raw message intact while making the UI much easier to scan during debugging.
        /// </summary>
        public string Summary
        {
            get
            {
                if (Fields.Count == 0)
                {
                    return LogVocabulary.TranslateRawMessage(Message);
                }

                string? phase = LogVocabulary.Translate("phase", Phase);
                string? step = LogVocabulary.Translate("step", Step);
                string? status = LogVocabulary.Translate("status", Status);
                string? action = LogVocabulary.Translate("action", Action);
                string? reason = LogVocabulary.Translate("reason", Reason);
                string? details = LogVocabulary.Translate("details", GetField("details"));

                var sb = new StringBuilder();

                // 1. Build core action + status sentence
                if (!string.IsNullOrWhiteSpace(action))
                {
                    string act = action.ToLowerInvariant();
                    string stat = status?.ToLowerInvariant() ?? "";

                    if (stat == "bắt đầu" || stat == "start")
                    {
                        sb.Append($"Bắt đầu {act}");
                    }
                    else if (stat == "thành công" || stat == "success" || stat == "hoàn tất" || stat == "ok")
                    {
                        sb.Append($"Đã {act} thành công");
                    }
                    else if (stat == "thất bại" || stat == "fail")
                    {
                        sb.Append($"Thất bại khi {act}");
                    }
                    else if (stat == "đang chờ" || stat == "pending")
                    {
                        sb.Append($"Đang {act}");
                    }
                    else if (stat == "thử lại" || stat == "retry")
                    {
                        sb.Append($"Đang thử lại {act}");
                    }
                    else if (stat == "bỏ qua" || stat == "skip")
                    {
                        sb.Append($"Bỏ qua {act}");
                    }
                    else if (stat == "đã dừng" || stat == "stopped")
                    {
                        sb.Append($"Đã dừng {act}");
                    }
                    else
                    {
                        sb.Append($"{Capitalize(action)}");
                        if (!string.IsNullOrWhiteSpace(status))
                        {
                            sb.Append($" {status}");
                        }
                    }
                }
                else if (!string.IsNullOrWhiteSpace(status))
                {
                    string stat = status.ToLowerInvariant();
                    string ph = phase?.ToLowerInvariant() ?? "";

                    if (stat == "bắt đầu" || stat == "start")
                    {
                        sb.Append(string.IsNullOrWhiteSpace(ph) ? "Bắt đầu tiến trình" : $"Bắt đầu {ph}");
                    }
                    else if (stat == "đang chờ" || stat == "pending")
                    {
                        sb.Append(string.IsNullOrWhiteSpace(ph) ? "Đang chờ" : $"Đang chờ {ph}");
                    }
                    else if (stat == "đã dừng" || stat == "stopped")
                    {
                        sb.Append(string.IsNullOrWhiteSpace(ph) ? "Đã dừng tiến trình" : $"{Capitalize(ph)} đã dừng");
                    }
                    else if (stat == "yêu cầu dừng" || stat == "stop_requested")
                    {
                        sb.Append(string.IsNullOrWhiteSpace(ph) ? "Yêu cầu dừng tiến trình" : $"Yêu cầu dừng {ph}");
                    }
                    else
                    {
                        sb.Append(string.IsNullOrWhiteSpace(ph) ? $"Trạng thái: {status}" : $"{Capitalize(ph)}: {status}");
                    }
                }
                else
                {
                    if (!string.IsNullOrWhiteSpace(phase))
                    {
                        sb.Append(Capitalize(phase));
                    }
                }

                // 2. Append phase context if not already covered as core subject
                if (!string.IsNullOrWhiteSpace(phase) && string.IsNullOrWhiteSpace(action))
                {
                    // Already covered as subject
                }
                else if (!string.IsNullOrWhiteSpace(phase) && !string.IsNullOrWhiteSpace(action))
                {
                    sb.Append($" trong giai đoạn {phase.ToLowerInvariant()}");
                }

                // 3. Append step
                if (!string.IsNullOrWhiteSpace(step))
                {
                    sb.Append($" (bước {step})");
                }

                // 4. Append reason do/vì
                if (!string.IsNullOrWhiteSpace(reason))
                {
                    string r = reason.ToLowerInvariant();
                    if (r.Contains("chưa") || r.Contains("không") || r.Contains("lỗi") || r.Contains("hết giờ") || r.Contains("đã"))
                    {
                        sb.Append($" do {reason}");
                    }
                    else
                    {
                        sb.Append($" vì {reason}");
                    }
                }

                // 5. Append details
                if (!string.IsNullOrWhiteSpace(details))
                {
                    sb.Append($" \u2014 {details}");
                }

                if (sb.Length == 0)
                {
                    return LogVocabulary.TranslateRawMessage(Message);
                }

                return sb.ToString();
            }
        }

        private static string Capitalize(string s)
        {
            if (string.IsNullOrEmpty(s)) return s;
            return char.ToUpperInvariant(s[0]) + s.Substring(1);
        }
    }
}
