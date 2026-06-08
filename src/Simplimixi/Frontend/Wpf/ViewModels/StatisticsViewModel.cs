using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using CvAut.WpfApp.Services;

namespace CvAut.WpfApp.ViewModels
{
    /// <summary>
    /// ViewModel cho màn hình "Thống kê" (StatisticsView.xaml).
    /// Quản lý dữ liệu hiệu suất hoạt động chi tiết, thực hiện định dạng rút gọn số liệu tài nguyên
    /// (ví dụ: triệu "M", nghìn "K") và phân tích số sao (Star Breakdown) để hiển thị lên UI trực quan.
    /// </summary>
    public class StatisticsViewModel : ViewModelBase
    {
        // Dịch vụ quản lý bot
        private readonly IBotService _botService;

        /// <summary>
        /// Khởi tạo StatisticsViewModel, đăng ký sự kiện StatsUpdated để tự động cập nhật số liệu.
        /// </summary>
        /// <param name="botService">Dịch vụ quản lý bot.</param>
        public StatisticsViewModel(IBotService botService)
        {
            _botService = botService;

            // Đăng ký sự kiện làm mới dữ liệu thống kê
            _botService.StatsUpdated += Refresh;

            // Thực hiện nạp dữ liệu ban đầu
            Refresh();
        }

        // Properties (Thuộc tính Data Binding có định dạng)

        /// <summary>
        /// Tổng lượng Vàng cướp được dưới dạng định dạng chữ (ví dụ: 1.5M, 200K).
        /// </summary>
        public string GoldGainedText => FormatNumber(_botService.GoldGained);

        /// <summary>
        /// Tổng lượng Dầu hồng cướp được dưới dạng định dạng chữ.
        /// </summary>
        public string ElixirGainedText => FormatNumber(_botService.ElixirGained);

        /// <summary>
        /// Tổng lượng Dầu đen cướp được dưới dạng định dạng chữ.
        /// </summary>
        public string DarkElixirGainedText => FormatNumber(_botService.DarkElixirGained);

        /// <summary>
        /// Tốc độ cướp Vàng trung bình mỗi giờ dưới dạng "/h" (ví dụ: 500K/h).
        /// </summary>
        public string AvgGoldPerHourText => FormatNumber(_botService.AvgGoldPerHour) + "/h";

        /// <summary>
        /// Tốc độ cướp Dầu hồng trung bình mỗi giờ dưới dạng "/h".
        /// </summary>
        public string AvgElixirPerHourText => FormatNumber(_botService.AvgElixirPerHour) + "/h";

        /// <summary>
        /// Tốc độ cướp Dầu đen trung bình mỗi giờ dưới dạng "/h".
        /// </summary>
        public string AvgDarkElixirPerHourText => FormatNumber(_botService.AvgDarkElixirPerHour) + "/h";

        public bool HasGoldStats => _botService.SessionBattleHistory.Count > 0;

        public bool HasElixirStats => _botService.SessionBattleHistory.Count > 0;

        public bool HasDarkElixirStats => _botService.SessionBattleHistory.Count > 0;

        public PointCollection GoldSparklinePoints => CreateSparklinePoints(_botService.SessionBattleHistory.Select(point => point.Gold));

        public PointCollection ElixirSparklinePoints => CreateSparklinePoints(_botService.SessionBattleHistory.Select(point => point.Elixir));

        public PointCollection DarkElixirSparklinePoints => CreateSparklinePoints(_botService.SessionBattleHistory.Select(point => point.DarkElixir));

        /// <summary>
        /// Tổng số trận tấn công.
        /// </summary>
        public string AttacksCountText => _botService.AttacksCount.ToString();

        /// <summary>
        /// Chuỗi tỷ lệ tấn công thành công (ví dụ: "85%").
        /// </summary>
        public string SuccessRateText => _botService.SuccessRateText;

        /// <summary>
        /// Giá trị số nguyên của tỷ lệ thành công (ví dụ: 85) để liên kết với thuộc tính Value của ProgressBar.
        /// </summary>
        public int SuccessRateValue => ParsePercent(_botService.SuccessRateText);

        /// <summary>
        /// Thời gian bot chạy liên tục.
        /// </summary>
        public string UptimeText => _botService.UptimeText;

        public string StatusText => _botService.StatusText;

        /// <summary>
        /// Bộ nhớ RAM tiến trình chiếm dụng.
        /// </summary>
        public string MemoryUsageText => _botService.MemoryUsageText;

        /// <summary>
        /// Chuỗi phân rã kết quả sao trận đánh (ví dụ: "5 triple / 3 double / 2 single / 1 fail").
        /// </summary>
        public string StarBreakdownText => $"{_botService.Star3Count} triple / {_botService.Star2Count} double / {_botService.Star1Count} single / {_botService.Star0Count} fail";

        public string Star0CountText => _botService.Star0Count.ToString();

        public string Star1CountText => _botService.Star1Count.ToString();

        public string Star2CountText => _botService.Star2Count.ToString();

        public string Star3CountText => _botService.Star3Count.ToString();

        /// <summary>
        /// Thông báo cho giao diện WPF cập nhật toàn bộ các thuộc tính hiển thị liên quan đến thống kê.
        /// </summary>
        private void Refresh()
        {
            OnPropertyChanged(nameof(GoldGainedText));
            OnPropertyChanged(nameof(ElixirGainedText));
            OnPropertyChanged(nameof(DarkElixirGainedText));
            OnPropertyChanged(nameof(AvgGoldPerHourText));
            OnPropertyChanged(nameof(AvgElixirPerHourText));
            OnPropertyChanged(nameof(AvgDarkElixirPerHourText));
            OnPropertyChanged(nameof(HasGoldStats));
            OnPropertyChanged(nameof(HasElixirStats));
            OnPropertyChanged(nameof(HasDarkElixirStats));
            OnPropertyChanged(nameof(GoldSparklinePoints));
            OnPropertyChanged(nameof(ElixirSparklinePoints));
            OnPropertyChanged(nameof(DarkElixirSparklinePoints));
            OnPropertyChanged(nameof(AttacksCountText));
            OnPropertyChanged(nameof(SuccessRateText));
            OnPropertyChanged(nameof(SuccessRateValue));
            OnPropertyChanged(nameof(UptimeText));
            OnPropertyChanged(nameof(StatusText));
            OnPropertyChanged(nameof(MemoryUsageText));
            OnPropertyChanged(nameof(StarBreakdownText));
            OnPropertyChanged(nameof(Star0CountText));
            OnPropertyChanged(nameof(Star1CountText));
            OnPropertyChanged(nameof(Star2CountText));
            OnPropertyChanged(nameof(Star3CountText));
        }

        private static PointCollection CreateSparklinePoints(IEnumerable<long> sourceValues)
        {
            const double width = 132.0;
            const double height = 40.0;
            const double midY = 24.0;

            long[] values = sourceValues.TakeLast(30).ToArray();
            if (values.Length == 0)
            {
                return new PointCollection();
            }

            if (values.Length == 1)
            {
                return new PointCollection(new[] { new Point(42, midY), new Point(90, midY) });
            }

            long min = values.Min();
            long max = values.Max();
            if (max == min)
            {
                double step = width / (values.Length - 1);
                return new PointCollection(values.Select((_, index) => new Point(index * step, midY)));
            }

            double range = max - min;
            double xStep = width / (values.Length - 1);
            return new PointCollection(values.Select((value, index) =>
            {
                double normalized = (value - min) / range;
                double y = height - (normalized * (height - 8.0)) - 4.0;
                return new Point(index * xStep, y);
            }));
        }

        /// <summary>
        /// Rút gọn các con số lớn bằng hậu tố M (triệu) hoặc K (nghìn) để giao diện hiển thị gọn gàng.
        /// </summary>
        private static string FormatNumber(long value)
        {
            return value switch
            {
                >= 1_000_000 => (value / 1_000_000.0).ToString("0.#") + "M",
                >= 1_000 => (value / 1_000.0).ToString("0.#") + "K",
                _ => value.ToString()
            };
        }

        /// <summary>
        /// Phân tích và chuyển đổi chuỗi phần trăm (ví dụ: "85%") thành giá trị số nguyên (85).
        /// </summary>
        private static int ParsePercent(string value)
        {
            string digits = value.Replace("%", string.Empty).Trim();
            return int.TryParse(digits, out int parsed) ? parsed : 0;
        }
    }
}
