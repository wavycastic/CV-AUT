using System;

namespace CvAut
{
    /// <summary>
    /// Formats and writes the [BB_NAV] structured log lines. The output format must stay
    /// byte-for-byte identical because log parsing depends on it.
    /// </summary>
    internal static class BuilderBaseNavigationLog
    {
        internal static void Write(string phase, string status, string target, int? attempt = null, string? details = null)
        {
            Console.WriteLine(Format(phase, status, target, attempt, details));
        }

        internal static void WriteDebug(string phase, string status, string target, int? attempt = null, string? details = null)
        {
            Console.WriteLine("[DEBUG]" + Format(phase, status, target, attempt, details));
        }

        internal static string Format(string phase, string status, string target, int? attempt = null, string? details = null)
        {
            string attemptText = attempt.HasValue ? $" attempt={attempt.Value}" : string.Empty;
            string detailsText = string.IsNullOrWhiteSpace(details) ? string.Empty : $" details=\"{Sanitize(details)}\"";
            return $"[BB_NAV] phase={SanitizeToken(phase)} status={SanitizeToken(status)} target={SanitizeToken(target)}{attemptText}{detailsText}";
        }

        private static string SanitizeToken(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return "unknown";
            }

            return Sanitize(value).Replace(' ', '_');
        }

        private static string Sanitize(string value)
        {
            return value
                .Replace("\r", " ", StringComparison.Ordinal)
                .Replace("\n", " ", StringComparison.Ordinal)
                .Replace("\"", "'", StringComparison.Ordinal)
                .Trim();
        }
    }
}
