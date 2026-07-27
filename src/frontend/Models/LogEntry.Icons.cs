namespace CvAut.Models
{
    /// <summary>
    /// Picks the single glyph shown at the start of a log row. Severity decides first; for
    /// Info and Warning the <c>status=</c> field refines it.
    /// </summary>
    public sealed partial class LogEntry
    {
        public string Icon => Level switch
        {
            LogLevel.Critical => "🛑",
            LogLevel.Error => "✕",
            LogLevel.Warning => GetIconForWarning(),
            LogLevel.Info => GetIconForInfo(),
            LogLevel.Debug => "🐞",
            LogLevel.Trace => "🔍",
            _ => "ℹ"
        };

        private string GetIconForWarning()
        {
            string stat = (Status ?? "").ToLowerInvariant();
            if (stat == "skipped" || stat == "bỏ qua" || stat == "skip") return "⏭";
            if (stat == "retry" || stat == "thử lại") return "↻";
            if (stat == "fallback") return "↩";
            return "⚠";
        }

        private string GetIconForInfo()
        {
            string stat = (Status ?? "").ToLowerInvariant();

            if (stat == "succeeded" || stat == "thành công" || stat == "success" || stat == "ok" || stat == "hoàn tất" || stat == "complete") return "✓";
            if (stat == "skipped" || stat == "bỏ qua" || stat == "skip") return "⏭";
            if (stat == "started" || stat == "bắt đầu" || stat == "start") return "●";
            if (stat == "stopped" || stat == "đã dừng" || stat == "stop" || stat == "cancelled") return "■";
            if (stat == "stop_requested" || stat == "yêu cầu dừng") return "■";
            if (stat == "failed" || stat == "thất bại" || stat == "fail") return "✕";
            if (stat == "retrying" || stat == "thử lại" || stat == "retry") return "↻";

            // Statuses that used to fall back to the generic info glyph, which made whole
            // stretches of the log look identical.
            if (stat == "send" || stat == "gửi") return "➤";
            if (stat == "fallback") return "↩";
            if (stat == "pending" || stat == "đang chờ") return "⏳";
            if (stat == "check" || stat == "đang kiểm tra") return "🔎";
            if (stat == "not_found" || stat == "không tìm thấy" || stat == "missing" || stat == "thiếu") return "∅";
            if (stat == "ready" || stat == "sẵn sàng") return "✓";
            if (stat == "selected" || stat == "đã chọn" || stat == "confirmed" || stat == "đã xác nhận") return "✓";
            if (stat == "upgraded" || stat == "đã nâng cấp") return "⬆";

            return "ℹ";
        }
    }
}
