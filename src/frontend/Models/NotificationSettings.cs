namespace CvAut.Models
{
    /// <summary>
    /// Opt-in outbound notification settings. Disabled by default and the webhook URL is empty
    /// until the user pastes their own — the app never ships or embeds any endpoint or token.
    /// Only a user-provided Discord webhook URL is used, and only the selected events are sent.
    /// </summary>
    public sealed class NotificationSettings
    {
        public bool Enabled { get; set; }

        /// <summary>User-supplied Discord webhook URL (https://discord.com/api/webhooks/...).</summary>
        public string WebhookUrl { get; set; } = string.Empty;

        public bool NotifyOnError { get; set; } = true;
        public bool NotifyOnStopped { get; set; }
        public bool NotifyOnStarted { get; set; }

        /// <summary>True only when notifications are enabled AND a plausible webhook URL is present.</summary>
        public bool IsActionable =>
            Enabled && WebhookUrl.StartsWith("https://", System.StringComparison.OrdinalIgnoreCase);

        /// <summary>Whether a given bot status transition should produce a notification.</summary>
        public bool ShouldNotify(BotStatus status) => status switch
        {
            BotStatus.Error => NotifyOnError,
            BotStatus.Stopped => NotifyOnStopped,
            BotStatus.Running => NotifyOnStarted,
            _ => false,
        };
    }
}