using System;
using System.Diagnostics;
using System.Text;

namespace CvAut
{
    /// <summary>
    /// Structured logger for the Main Village Wall Upgrade phase ([WALL-MV]).
    /// Formats consistent key=value log lines for boundary timing, preflight, candidate searching,
    /// OCR decision making, batch adjustment, post-confirm polling, and cleanup/cancellation.
    /// </summary>
    internal static class WallLogger
    {
        public const string Prefix = "[WALL-MV]";

        public static string GenerateRunId()
        {
            return Random.Shared.Next(0x100000, 0xFFFFFF).ToString("x6");
        }

        public static void LogInfo(
            string phase,
            string status,
            string? reason = null,
            int? village = null,
            int? cycle = null,
            string? trigger = null,
            int? batchBudget = null,
            int? batchLimit = null,
            string? runId = null,
            long? elapsedMs = null,
            string? extra = null)
        {
            var sb = new StringBuilder();
            sb.Append(Prefix);
            sb.Append(" phase=").Append(phase);
            sb.Append(" status=").Append(status);

            if (!string.IsNullOrEmpty(reason)) sb.Append(" reason=").Append(reason);
            if (village.HasValue) sb.Append(" village=").Append(village.Value);
            if (cycle.HasValue) sb.Append(" cycle=").Append(cycle.Value);
            if (!string.IsNullOrEmpty(trigger)) sb.Append(" trigger=").Append(trigger);
            if (batchBudget.HasValue) sb.Append(" batch_budget=").Append(batchBudget.Value);
            if (batchLimit.HasValue) sb.Append(" batch_limit=").Append(batchLimit.Value);
            if (!string.IsNullOrEmpty(runId)) sb.Append(" run_id=").Append(runId);
            if (elapsedMs.HasValue) sb.Append(" elapsed_ms=").Append(elapsedMs.Value);
            if (!string.IsNullOrEmpty(extra)) sb.Append(' ').Append(extra);

            Console.WriteLine(sb.ToString());
        }

        public static Stopwatch StartTimer() => Stopwatch.StartNew();
    }
}
