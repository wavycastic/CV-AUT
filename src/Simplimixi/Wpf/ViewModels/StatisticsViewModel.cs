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

        /// <summary>
        /// Bộ nhớ RAM tiến trình chiếm dụng.
        /// </summary>
        public string MemoryUsageText => _botService.MemoryUsageText;

        /// <summary>
        /// Chuỗi phân rã kết quả sao trận đánh (ví dụ: "5 triple / 3 double / 2 single / 1 fail").
        /// </summary>
        public string StarBreakdownText => $"{_botService.Star3Count} triple / {_botService.Star2Count} double / {_botService.Star1Count} single / {_botService.Star0Count} fail";

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
            OnPropertyChanged(nameof(AttacksCountText));
            OnPropertyChanged(nameof(SuccessRateText));
            OnPropertyChanged(nameof(SuccessRateValue));
            OnPropertyChanged(nameof(UptimeText));
            OnPropertyChanged(nameof(MemoryUsageText));
            OnPropertyChanged(nameof(StarBreakdownText));
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
