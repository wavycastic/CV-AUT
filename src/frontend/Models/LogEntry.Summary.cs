using System;
using System.Collections.Generic;
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
        /// <summary>Fields that the sentence itself already speaks.</summary>
        private static readonly HashSet<string> s_spokenFields = new(StringComparer.OrdinalIgnoreCase)
        {
            "phase", "status", "action", "reason", "step", "details", "detail", "stage", "item",
        };

        /// <summary>
        /// Reading order for the trailing measurement clause. A dictionary has no order the
        /// user can rely on, and the important numbers should not move between rows.
        /// </summary>
        private static readonly string[] s_fieldOrder =
        {
            "stars", "gold", "elixir", "dark_elixir", "total", "total_ok", "trophies",
            "troop", "spell", "unit", "strategy", "village", "mode", "level",
            "score", "threshold", "best_scale", "best_scale_score", "confidence",
            "current", "capacity", "remaining", "count", "index", "slot", "candidates",
            "tap_count", "taps", "total_taps", "attempt", "attempts",
            "x", "y", "x1", "y1", "x2", "y2",
            "duration", "duration_ms", "elapsed_ms", "baseline_ms", "wait_ms", "timeout_ms",
        };

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

                // Battle results are the one line a user always reads, so they get a full
                // sentence instead of "thống kê trận đấu: hoàn tất".
                string? battleResult = BuildBattleResultSentence();
                if (battleResult is not null)
                {
                    return battleResult + DescribeRemainingFields("stars", "gold", "elixir", "dark_elixir");
                }

                string? phase = LogVocabulary.Translate("phase", Phase);
                string? step = LogVocabulary.Translate("step", Step);
                string? status = LogVocabulary.Translate("status", Status);
                string? action = LogVocabulary.Translate("action", Action);
                string? reason = LogVocabulary.Translate("reason", Reason);
                string? details = LogVocabulary.Translate("details", GetField("details"));
                string? stage = LogVocabulary.Translate("stage", GetField("stage"));
                string? item = LogVocabulary.Translate("item", GetField("item"));
                string? detail = LogVocabulary.TranslateDetail(GetField("detail"));

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

                // 3b. Append pipeline stage. Without it every pipeline line reads the same.
                if (!string.IsNullOrWhiteSpace(stage))
                {
                    sb.Append($" [chặng {stage.ToLowerInvariant()}]");
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

                // 5. Append the subject the line is about, when the backend named one.
                if (!string.IsNullOrWhiteSpace(item))
                {
                    sb.Append($" (đối tượng: {item})");
                }

                // 6. Append details
                if (!string.IsNullOrWhiteSpace(details))
                {
                    sb.Append($" \u2014 {details}");
                }

                // 7. Append the diagnostic detail: it carries the measurements that explain
                // the line, so it reads best as a trailing clause.
                if (!string.IsNullOrWhiteSpace(detail))
                {
                    sb.Append($" \u2014 {detail}");
                }

                if (sb.Length == 0)
                {
                    return LogVocabulary.TranslateRawMessage(Message);
                }

                // 8. Finally every field the sentence did not speak. Dropping them used to
                // hide the numbers the user is actually watching.
                sb.Append(DescribeRemainingFields());

                return sb.ToString();
            }
        }

        /// <summary>
        /// Full sentence for the end-of-battle line, which carries the stars and the loot.
        /// Returns null for every other line.
        /// </summary>
        private string? BuildBattleResultSentence()
        {
            if (!string.Equals(Phase, "battle_stats", StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            var sb = new StringBuilder("Kết thúc trận");

            string? stars = GetField("stars");
            sb.Append(string.IsNullOrWhiteSpace(stars) ? string.Empty : $": {stars} sao");

            var loot = new List<string>();
            AppendLoot(loot, "gold", "vàng");
            AppendLoot(loot, "elixir", "dầu");
            AppendLoot(loot, "dark_elixir", "dầu đen");

            if (loot.Count > 0)
            {
                sb.Append(" \u2014 cướp được " + string.Join(", ", loot));
            }

            return sb.ToString();
        }

        private void AppendLoot(List<string> loot, string key, string label)
        {
            string? value = GetField(key);
            if (string.IsNullOrWhiteSpace(value)) return;
            loot.Add(LogVocabulary.FormatFieldValue(key, value) + " " + label);
        }

        /// <summary>
        /// Renders every field the sentence did not speak, in a stable reading order and
        /// with a Vietnamese label. Unknown keys are shown under their raw name: a field
        /// nobody has translated yet is still better than a value that disappears.
        /// </summary>
        private string DescribeRemainingFields(params string[] alsoSkip)
        {
            if (Fields.Count == 0) return string.Empty;

            var rendered = new List<string>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            void Render(string key)
            {
                if (s_spokenFields.Contains(key)) return;
                if (!seen.Add(key)) return;
                foreach (string skip in alsoSkip)
                {
                    if (string.Equals(skip, key, StringComparison.OrdinalIgnoreCase)) return;
                }

                if (!Fields.TryGetValue(key, out string? value) || string.IsNullOrWhiteSpace(value)) return;

                string label = LogVocabulary.TranslateFieldLabel(key) ?? key;
                rendered.Add(label + ": " + LogVocabulary.FormatFieldValue(key, value));
            }

            foreach (string key in s_fieldOrder)
            {
                Render(key);
            }

            // Anything the order list does not know about, alphabetically so the row is
            // reproducible rather than dictionary-ordered.
            var leftovers = new List<string>();
            foreach (var kv in Fields)
            {
                if (s_spokenFields.Contains(kv.Key)) continue;
                if (seen.Contains(kv.Key)) continue;
                leftovers.Add(kv.Key);
            }

            leftovers.Sort(StringComparer.OrdinalIgnoreCase);
            foreach (string key in leftovers)
            {
                Render(key);
            }

            return rendered.Count == 0 ? string.Empty : " (" + string.Join("; ", rendered) + ")";
        }

        private static string Capitalize(string s)
        {
            if (string.IsNullOrEmpty(s)) return s;
            return char.ToUpperInvariant(s[0]) + s.Substring(1);
        }
    }
}
