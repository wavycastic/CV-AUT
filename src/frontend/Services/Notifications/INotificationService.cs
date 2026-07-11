using System;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using CvAut.Models;

namespace CvAut.Services.Notifications
{
    /// <summary>Sends opt-in outbound notifications for device events.</summary>
    public interface INotificationService
    {
        /// <summary>Fire-and-forget notify for a device status change; no-op unless opted in and the
        /// event type is selected. Never throws to the caller.</summary>
        Task NotifyStatusAsync(string deviceName, BotStatus status, string? detail = null, CancellationToken ct = default);
    }

    /// <summary>
    /// Posts a plain-text message to a user-provided Discord webhook. Opt-in only: does nothing
    /// unless <see cref="NotificationSettings.IsActionable"/> and the event is selected. Failures are
    /// swallowed (best-effort) so a webhook outage never blocks or crashes automation.
    /// </summary>
    public sealed class DiscordWebhookNotificationService : INotificationService
    {
        private readonly Func<NotificationSettings> _settingsProvider;
        private readonly HttpClient _http;

        public DiscordWebhookNotificationService(Func<NotificationSettings> settingsProvider, HttpClient? http = null)
        {
            _settingsProvider = settingsProvider;
            _http = http ?? new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
        }

        public async Task NotifyStatusAsync(string deviceName, BotStatus status, string? detail = null, CancellationToken ct = default)
        {
            NotificationSettings settings = _settingsProvider();
            if (!settings.IsActionable || !settings.ShouldNotify(status))
            {
                return;
            }

            string content = FormatMessage(deviceName, status, detail);
            try
            {
                string payload = new System.Text.Json.Nodes.JsonObject { ["content"] = content }.ToJsonString();
                using var body = new StringContent(payload, Encoding.UTF8, "application/json");
                using HttpResponseMessage resp = await _http.PostAsync(settings.WebhookUrl, body, ct);
                _ = resp; // status intentionally ignored — best effort
            }
            catch
            {
                // Best-effort: a webhook failure must never disrupt automation.
            }
        }

        /// <summary>Builds the human-readable message. Pure/deterministic for testability.</summary>
        public static string FormatMessage(string deviceName, BotStatus status, string? detail)
        {
            string label = status switch
            {
                BotStatus.Error => "❌ Lỗi",
                BotStatus.Stopped => "⏹️ Đã dừng",
                BotStatus.Running => "▶️ Đang chạy",
                BotStatus.Paused => "⏸️ Tạm dừng",
                _ => status.ToString(),
            };

            string msg = $"[{deviceName}] {label}";
            if (!string.IsNullOrWhiteSpace(detail))
            {
                msg += $" — {detail}";
            }

            return msg;
        }
    }
}
