using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace CvAut.ViewModels
{
    /// <summary>
    /// Opt-in notifications (Discord webhook) form. Disabled by default; the URL is user-supplied
    /// and is only acted on when it is a valid https webhook.
    /// </summary>
    public partial class SettingsViewModel
    {
        [ObservableProperty] private bool _notifyEnabled;
        [ObservableProperty] private string _webhookUrl = string.Empty;
        [ObservableProperty] private bool _notifyOnError = true;
        [ObservableProperty] private bool _notifyOnStopped;
        [ObservableProperty] private bool _notifyOnStarted;
        [ObservableProperty] private string _notifyStatus = string.Empty;

        private void LoadNotificationSettings()
        {
            var s = _configStore.LoadNotificationSettings();
            NotifyEnabled = s.Enabled;
            WebhookUrl = s.WebhookUrl;
            NotifyOnError = s.NotifyOnError;
            NotifyOnStopped = s.NotifyOnStopped;
            NotifyOnStarted = s.NotifyOnStarted;
        }

        [RelayCommand]
        private void SaveNotifications()
        {
            var s = new Models.NotificationSettings
            {
                Enabled = NotifyEnabled,
                WebhookUrl = (WebhookUrl ?? string.Empty).Trim(),
                NotifyOnError = NotifyOnError,
                NotifyOnStopped = NotifyOnStopped,
                NotifyOnStarted = NotifyOnStarted,
            };
            _configStore.SaveNotificationSettings(s);
            NotifyStatus = s.IsActionable ? "Đã lưu — thông báo bật." : (s.Enabled ? "Đã lưu — cần URL webhook https hợp lệ." : "Đã lưu — thông báo tắt.");
        }
    }
}
