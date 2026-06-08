using System;
using System.Collections.Generic;
using System.Text.Json.Nodes;

namespace CvAut.WpfApp.Services
{
    public sealed record BattleLootPoint(DateTime Timestamp, long Gold, long Elixir, long DarkElixir, int Stars);
    /// <summary>
    /// Giao diện (Interface) định nghĩa các dịch vụ điều khiển bot tự động hóa.
    /// Cung cấp các thuộc tính trạng thái, chỉ số hiệu suất chạy bot, các sự kiện log/status
    /// và các phương thức lưu/tải cấu hình của từng làng cũng như tệp cấu hình chính.
    /// </summary>
    public interface IBotService
    {
        /// <summary>
        /// Xác định xem luồng xử lý tự động của bot có đang hoạt động hay không.
        /// </summary>
        bool IsRunning { get; }

        /// <summary>
        /// Xác định xem bot có đang ở trạng thái tạm dừng (Pause) hay không.
        /// </summary>
        bool IsPaused { get; }

        /// <summary>
        /// ID định danh của Làng hiện tại đang được chọn điều khiển (từ 1 đến 5).
        /// Khi thay đổi sẽ tự động chuyển đổi cấu hình hiển thị và số liệu tương ứng.
        /// </summary>
        int CurrentVillage { get; set; }

        /// <summary>
        /// Chuỗi văn bản đại diện cho trạng thái hiện tại của bot (IDLE, RUNNING, PAUSED).
        /// </summary>
        string StatusText { get; }

        // CÁC CHỈ SỐ THỐNG KÊ HIỆU SUẤT PHIÊN CHẠY (Runtime Stats)

        /// <summary>
        /// Thời gian bot chạy liên tục dưới dạng chuỗi HH:mm:ss.
        /// </summary>
        string UptimeText { get; }

        /// <summary>
        /// Dung lượng bộ nhớ RAM đang sử dụng bởi ứng dụng (ví dụ: "45.2 MB").
        /// </summary>
        string MemoryUsageText { get; }

        /// <summary>
        /// Tỷ lệ phần trăm các trận cướp thành công (đạt ít nhất 1 Sao).
        /// </summary>
        string SuccessRateText { get; }

        /// <summary>
        /// Tổng số trận cướp đã thực hiện trong phiên hiện tại.
        /// </summary>
        int AttacksCount { get; }

        /// <summary>
        /// Tổng lượng Vàng (Gold) thu hoạch cướp được.
        /// </summary>
        long GoldGained { get; }

        /// <summary>
        /// Tổng lượng Dầu hồng (Elixir) cướp được.
        /// </summary>
        long ElixirGained { get; }

        /// <summary>
        /// Tổng lượng Dầu đen (Dark Elixir) cướp được.
        /// </summary>
        long DarkElixirGained { get; }

        /// <summary>
        /// Lượng Vàng trung bình cướp được mỗi giờ.
        /// </summary>
        long AvgGoldPerHour { get; }

        /// <summary>
        /// Lượng Dầu hồng trung bình cướp được mỗi giờ.
        /// </summary>
        long AvgElixirPerHour { get; }

        /// <summary>
        /// Lượng Dầu đen trung bình cướp được mỗi giờ.
        /// </summary>
        long AvgDarkElixirPerHour { get; }

        /// <summary>
        /// Số trận kết thúc với 0 Sao (Thất bại).
        /// </summary>
        int Star0Count { get; }

        /// <summary>
        /// Số trận kết thúc với 1 Sao.
        /// </summary>
        int Star1Count { get; }

        /// <summary>
        /// Số trận kết thúc với 2 Sao.
        /// </summary>
        int Star2Count { get; }

        /// <summary>
        /// Số trận kết thúc với 3 Sao (Thành công tuyệt đối).
        /// </summary>
        int Star3Count { get; }

        IReadOnlyList<BattleLootPoint> SessionBattleHistory { get; }

        // SỰ KIỆN LIÊN KẾT GIAO DIỆN (Events)

        /// <summary>
        /// Sự kiện kích hoạt khi có dòng log mới được gửi ra từ Core.
        /// </summary>
        event Action<string>? LogReceived;

        /// <summary>
        /// Sự kiện kích hoạt khi trạng thái hoạt động của bot thay đổi (Start/Stop/Pause).
        /// </summary>
        event Action? StatusChanged;

        /// <summary>
        /// Sự kiện kích hoạt khi các chỉ số thống kê hiệu suất được cập nhật.
        /// </summary>
        event Action? StatsUpdated;

        // PHƯƠNG THỨC ĐIỀU KHIỂN (Control Methods)

        /// <summary>
        /// Khởi chạy bot tự động hóa: thiết lập các cổng adb, hook console và khởi tạo FSM.
        /// </summary>
        void StartBot();

        /// <summary>
        /// Dừng luồng bot và khôi phục đầu ra console hệ thống.
        /// </summary>
        void StopBot();

        /// <summary>
        /// Chuyển đổi trạng thái Tạm dừng (Pause) hoặc Tiếp tục chạy (Resume).
        /// </summary>
        void TogglePause();

        // THAO TÁC CẤU HÌNH JSON (Configuration Operations)

        /// <summary>
        /// Tải đối tượng tệp cấu hình kiểm thử chính của bot (test_config.json).
        /// </summary>
        /// <returns>Đối tượng JsonObject chứa cấu hình chính.</returns>
        JsonObject LoadMainConfig();

        /// <summary>
        /// Ghi dữ liệu cấu hình chính của bot xuống tệp tin tương ứng.
        /// </summary>
        /// <param name="root">Đối tượng JsonObject chứa nội dung cấu hình.</param>
        void SaveMainConfig(JsonObject root);

        /// <summary>
        /// Tải tệp cấu hình của riêng một làng cụ thể theo ID làng.
        /// </summary>
        /// <param name="villageId">ID định danh của làng (1-5).</param>
        /// <returns>Đối tượng JsonObject cấu hình riêng biệt.</returns>
        JsonObject LoadProfile(int villageId);

        /// <summary>
        /// Lưu tệp cấu hình của riêng một làng xuống tệp tin tương ứng.
        /// </summary>
        /// <param name="villageId">ID định danh của làng (1-5).</param>
        /// <param name="profile">Đối tượng JsonObject chứa cấu hình riêng biệt.</param>
        void SaveProfile(int villageId, JsonObject profile);
    }
}
