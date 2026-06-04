using CvAut.WpfApp.Services;

namespace CvAut.WpfApp.ViewModels
{
    public class StatisticsViewModel : ViewModelBase
    {
        private readonly IBotService _botService;

        public StatisticsViewModel(IBotService botService)
        {
            _botService = botService;
            _botService.StatsUpdated += Refresh;
            Refresh();
        }

        public string GoldGainedText => FormatNumber(_botService.GoldGained);
        public string ElixirGainedText => FormatNumber(_botService.ElixirGained);
        public string DarkElixirGainedText => FormatNumber(_botService.DarkElixirGained);
        public string AvgGoldPerHourText => FormatNumber(_botService.AvgGoldPerHour) + "/h";
        public string AvgElixirPerHourText => FormatNumber(_botService.AvgElixirPerHour) + "/h";
        public string AvgDarkElixirPerHourText => FormatNumber(_botService.AvgDarkElixirPerHour) + "/h";
        public string AttacksCountText => _botService.AttacksCount.ToString();
        public string SuccessRateText => _botService.SuccessRateText;
        public int SuccessRateValue => ParsePercent(_botService.SuccessRateText);
        public string UptimeText => _botService.UptimeText;
        public string MemoryUsageText => _botService.MemoryUsageText;
        public string StarBreakdownText => $"{_botService.Star3Count} triple / {_botService.Star2Count} double / {_botService.Star1Count} single / {_botService.Star0Count} fail";

        private void Refresh()
        {
            OnPropertyChanged(nameof(GoldGainedText));
            OnPropertyChanged(nameof(ElixirGainedText));
            OnPropertyChanged(nameof(DarkElixirGainedText));
            OnPropertyChanged(nameof(AvgGoldPerHourText));
            OnPropertyChanged(nameof(AvgElixirPerHourText));
            OnPropertyChanged(nameof(AvgDarkElixirPerHourText));
            OnPropertyChanged(nameof(AttacksCountText));
            OnPropertyChanged(nameof(SuccessRateText));
            OnPropertyChanged(nameof(SuccessRateValue));
            OnPropertyChanged(nameof(UptimeText));
            OnPropertyChanged(nameof(MemoryUsageText));
            OnPropertyChanged(nameof(StarBreakdownText));
        }

        private static string FormatNumber(long value)
        {
            return value switch
            {
                >= 1_000_000 => (value / 1_000_000.0).ToString("0.#") + "M",
                >= 1_000 => (value / 1_000.0).ToString("0.#") + "K",
                _ => value.ToString()
            };
        }

        private static int ParsePercent(string value)
        {
            string digits = value.Replace("%", string.Empty).Trim();
            return int.TryParse(digits, out int parsed) ? parsed : 0;
        }
    }
}
