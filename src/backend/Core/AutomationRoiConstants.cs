using OpenCvSharp;

namespace CvAut
{
    /// <summary>
    /// Định nghĩa tập trung các vùng ROI (Region of Interest) chuẩn 1600x900px và điểm nhấp (Tap Point)
    /// phục vụ xác thực giao diện và điều hướng của CV-AUT.
    /// </summary>
    public static class AutomationRoiConstants
    {
        // Giao diện chính và chiến đấu
        public static readonly Rect GameSettingHomeRoi = Rect.FromLTRB(1445, 499, 1599, 708);
        public static readonly Rect NextButtonRoi = Rect.FromLTRB(1291, 563, 1592, 721);
        public static readonly Rect ScoutUiRoi = Rect.FromLTRB(2, 612, 222, 724);
        public static readonly Rect BattleEndedRoi = Rect.FromLTRB(632, 222, 989, 841);
        public static readonly Rect ResultYouGotRoi = Rect.FromLTRB(720, 330, 910, 390);
        public static readonly Rect ResultContinueRoi = Rect.FromLTRB(590, 670, 1020, 860);

        // Popups sự kiện & kết nối
        public static readonly Rect ConnectionPopupRoi = Rect.FromLTRB(360, 180, 1240, 720);
        public static readonly Rect StarBonusPopupRoi = Rect.FromLTRB(430, 55, 1170, 145);
        public static readonly Rect TreasureHuntRoi = Rect.FromLTRB(940, 80, 1450, 830);
        public static readonly Rect TreasureHuntChestTemplateRoi = Rect.FromLTRB(105, 65, 210, 145);
        public static readonly Rect TreasureHuntTextTemplateRoi = Rect.FromLTRB(15, 210, 300, 275);

        // Tọa độ Tap chuẩn & ROI nhận thưởng sau trận
        public static readonly Point TreasureHuntOpenedChestTapPoint = new(800, 455);
        public static readonly Point TreasureHuntRewardContinueTapPoint = new(800, 750);
        public static readonly Point StarBonusOkayTapPoint = new(808, 766);
        public static readonly Rect ClaimRewardSafeRoi = Rect.FromLTRB(724, 750, 948, 822);
        public static readonly Point ClaimRewardSafeTapPoint = new(836, 786);
    }
}
