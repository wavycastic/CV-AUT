using System;
using System.Threading.Tasks;
using System.Windows;
using CvAut.WpfApp.Services;
using Wpf.Ui.Controls;

namespace CvAut.WpfApp.Views
{
    public partial class UpdateDialog : Window
    {
        private readonly UpdateDecision _decision;
        private bool _startedInstall;

        public UpdateDialog(UpdateDecision decision)
        {
            _decision = decision;
            InitializeComponent();

            ShowInTaskbar = false;
            TitleTextBlock.Text = decision.IsForced ? "Bắt buộc cập nhật SimpliMixi" : "Đã có bản cập nhật mới";
            SubtitleTextBlock.Text = $"Phiên bản hiện tại: {FormatVersion(decision.CurrentVersion)}  •  Phiên bản mới: {FormatVersion(decision.LatestVersion)}";
            NotesTextBlock.Text = string.IsNullOrWhiteSpace(decision.Manifest.Notes)
                ? "Không có ghi chú phát hành."
                : decision.Manifest.Notes;

            LaterButton.Visibility = decision.IsForced ? Visibility.Collapsed : Visibility.Visible;
        }

        protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
        {
            base.OnClosing(e);

            if (_decision.IsForced && !_startedInstall)
            {
                Application.Current.Shutdown();
            }
        }

        private async void UpdateButton_Click(object sender, RoutedEventArgs e)
        {
            await DownloadAndInstallAsync();
        }

        private void LaterButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }

        private async Task DownloadAndInstallAsync()
        {
            SetUpdatingState(true, "Đang tải bản cập nhật...");

            try
            {
                var progress = new Progress<double>(value =>
                {
                    DownloadProgressBar.Value = value * 100;
                    ProgressTextBlock.Text = $"Đang tải bản cập nhật... {value:P0}";
                });

                string installerPath = await UpdateService.DownloadInstallerAsync(_decision, progress);
                ProgressTextBlock.Text = "Đã tải xong. Đang mở trình cài đặt...";
                _startedInstall = true;
                UpdateService.StartInstallerAndExit(installerPath);
                Application.Current.Shutdown();
            }
            catch (Exception ex)
            {
                UpdateService.WriteLog($"install_failed error=\"{ex}\"");
                SetUpdatingState(false, "Không tải được bản cập nhật. Vui lòng thử lại.");
                await new Wpf.Ui.Controls.MessageBox
                {
                    Owner = this,
                    Title = "Không thể cập nhật",
                    Content = "SimpliMixi không tải hoặc chạy được bản cập nhật. Vui lòng kiểm tra mạng rồi thử lại.",
                    CloseButtonText = "OK",
                    WindowStartupLocation = WindowStartupLocation.CenterOwner
                }.ShowDialogAsync();
            }
        }

        private void SetUpdatingState(bool isUpdating, string statusText)
        {
            ProgressTextBlock.Text = statusText;
            ProgressPanel.Visibility = isUpdating ? Visibility.Visible : Visibility.Collapsed;
            ActionPanel.Visibility = isUpdating ? Visibility.Collapsed : Visibility.Visible;
            DownloadProgressBar.Value = 0;
        }

        private static string FormatVersion(Version version)
        {
            return version.Build > 0
                ? $"{version.Major}.{version.Minor}.{version.Build}"
                : $"{version.Major}.{version.Minor}";
        }
    }
}
