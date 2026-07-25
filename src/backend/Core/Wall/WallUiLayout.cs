using System;
using OpenCvSharp;
using Point = OpenCvSharp.Point;

namespace CvAut
{
    /// <summary>
    /// Toàn bộ toạ độ, ROI và ngưỡng đã hiệu chỉnh cho giao diện nâng cấp tường (độ phân giải 1600x900).
    /// Port từ legacy/NX-ClashClient rois.json; không được đổi giá trị nếu chưa hiệu chỉnh lại trên máy thật.
    /// </summary>
    internal static class WallUiLayout
    {
        // Vùng ROI tìm kiếm tường trong Builder menu
        internal static readonly Rect BuilderUpgradeMenuRoi = new(646, 107, 347, 474);
        // Tọa độ điểm kiểm tra màu nền xám/trắng nhạt để xác nhận bảng nâng cấp đang mở
        internal static readonly Point PanelCheckPoint = new(800, 750);
        // Nút bấm gợi ý Thợ xây ở top-center
        internal static readonly Point BuilderMenuPoint = new(738, 36);
        // Điểm an toàn ngoài rìa bản đồ để bấm giải tỏa các menu/popup
        internal static readonly Point HomeMenuPoint = new(140, 606);
        // Tọa độ vuốt cuộn bảng gợi ý Thợ xây
        internal static readonly Point RetrySwipeStart = new(977, 157);
        internal static readonly Point RetrySwipeEnd = new(999, 432);
        // Các điểm chạm điều hướng giao diện nâng cấp tường
        internal static readonly Point DismissPoint = new(1143, 209);
        internal static readonly Point FixedGoldUpgradePoint = new(920, 707);
        internal static readonly Point FixedElixirUpgradePoint = new(1095, 702);
        internal static readonly Point AddWallPlusOneButton = new(660, 650);
        internal static readonly Point RemoveWallMinusOneButton = new(330, 650);
        internal static readonly Rect GoldUpgradeCostRoi = new(860, 635, 120, 33);
        internal static readonly Rect ElixirUpgradeCostRoi = new(1035, 635, 120, 33);
        internal static readonly Point ConfirmUpgradePoint = new(1115, 782);
        internal static readonly Point ConfirmMultiPoint = new(990, 620);
        internal static readonly Rect ConfirmDialogRoi = new(820, 540, 430, 300);

        internal const int WallUiAnimationDelayMs = 400;
        internal const int RedCostPixelCountThreshold = 120;
        // Ngưỡng so khớp mẫu để tìm tường (cần độ tin cậy cao để tránh nhận diện nhầm các vật thể khác)
        internal const double WallSearchThreshold = 0.90;
        internal const int SwipeDurationMs = 600;
        internal const double MaxCostMismatchRatio = 1.15;
        internal const int SupportedScreenshotWidth = 1600;
        internal const int SupportedScreenshotHeight = 900;
        internal const int MaxCandidateAttempts = 3;

        /// <summary>Trả về ROI đọc chi phí tương ứng với loại tài nguyên.</summary>
        internal static Rect CostRoiFor(string resource)
            => resource.Equals("gold", StringComparison.OrdinalIgnoreCase)
                ? GoldUpgradeCostRoi
                : ElixirUpgradeCostRoi;

        /// <summary>Trả về điểm chạm nút nâng cấp tương ứng với loại tài nguyên.</summary>
        internal static Point UpgradePointFor(string resource)
            => resource.Equals("gold", StringComparison.OrdinalIgnoreCase)
                ? FixedGoldUpgradePoint
                : FixedElixirUpgradePoint;
    }
}
