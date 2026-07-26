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
            return "⚠";
        }

        private string GetIconForInfo()
        {
            string stat = (Status ?? "").ToLowerInvariant();
            string act = (Action ?? "").ToLowerInvariant();

            if (stat == "succeeded" || stat == "thành công" || stat == "success" || stat == "ok" || stat == "hoàn tất") return "✓";
            if (stat == "skipped" || stat == "bỏ qua" || stat == "skip") return "⏭";
            if (stat == "started" || stat == "bắt đầu" || stat == "start") return "●";
            if (stat == "stopped" || stat == "đã dừng" || stat == "stop" || stat == "cancelled") return "■";
            if (stat == "failed" || stat == "thất bại" || stat == "fail") return "✕";
            if (stat == "retrying" || stat == "thử lại" || stat == "retry") return "⚠";

            return "ℹ";
        }
    }
}
