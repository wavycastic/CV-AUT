namespace CvAut
{
    /// <summary>
    /// Định nghĩa tập trung các ngưỡng tin cậy (Threshold) và thời gian chờ (Timeout/Delay)
    /// cho khớp mẫu OpenCV và các chu kỳ máy trạng thái CV-AUT.
    /// </summary>
    public static class AutomationThresholds
    {
        // Ngưỡng khớp mẫu (Template Matching Thresholds)
        public const double HomeTemplateThreshold = 0.70;
        public const double ConnectionPopupThreshold = 0.88;
        public const double LegacyConnectionPopupThreshold = 0.55;
        public const double ConnIconPopupThreshold = 0.94;
        public const double NextButtonThreshold = 0.35;
        public const double ScoutUiThreshold = 0.70;
        public const double TreasureHuntThreshold = 0.70;
        public const double TreasureHuntMarkerThreshold = 0.82;
        public const double ResultContinueThreshold = 0.38;
        public const double ResultYouGotThreshold = 0.55;
        public const double StarBonusPopupThreshold = 0.70;

        // Cấu hình thời gian & chu kỳ
        public const int ResultScreenStableMatches = 2;
        public const int MaxWaitBattleSeconds = 170; // 3 phút tối đa cho trận đấu
        public const int NormalCycleDelayMs = 10000;
        public const int FastAttackCycleDelayMs = 500;

        // Danh sách mẫu popup lỗi kết nối
        public static readonly string[] ConnectionPopupTemplates =
        {
            @"ui\Another_device.png",
            @"ui\Connection_lost.png",
            @"ui\Client_error!.png",
            @"ui\rate_coc.png",
            @"ui\conn.png"
        };
    }
}
