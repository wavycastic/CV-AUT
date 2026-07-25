using OpenCvSharp;

namespace CvAut
{
    /// <summary>
    /// Static layout data for Builder Base navigation: thresholds, ROIs and template names.
    /// Every value here is copied verbatim from the original BuilderBaseNavigator implementation.
    /// </summary>
    internal static class BuilderBaseNavigationLayout
    {
        internal const double MainVillageThreshold = 0.70;
        internal const double BuilderBaseThreshold = 0.70;
        internal const double SwitchButtonThreshold = 0.62;
        internal const int SwitchAttempts = 5;
        internal const int SwitchPollIntervalMs = 250;
        internal const int SwitchVerifyTimeoutMs = 5500;

        // Same village-marker search band used by MBR (150,600,680,720 on 860x780),
        // scaled to our 1600x900 screenshots.
        internal static readonly Rect VillageMarkerRoi = Rect.FromLTRB(279, 692, 1265, 831);
        internal static readonly Rect MainVillageMarkerRoi = VillageMarkerRoi;
        internal static readonly Rect BuilderBaseMarkerRoi = VillageMarkerRoi;
        internal static readonly Rect SwitchButtonRoi = Rect.FromLTRB(0, 360, 520, 850);
        internal static readonly Rect SwitchToMainButtonRoi = Rect.FromLTRB(0, 35, 260, 170);
        internal static readonly Rect StageTunnelRoi = Rect.FromLTRB(0, 90, 640, 420);
        internal static readonly Rect MainVillageUiRoi = Rect.FromLTRB(1180, 420, 1599, 890);
        internal static readonly Rect VillageTerrainRoi = Rect.FromLTRB(180, 80, 1280, 850);

        internal static readonly string[] MainVillageTemplates =
        {
            @"village\Page\MainVillage\MainVillage_100_90",
            @"village\Page\MainVillage\GobBuilder_100_92",
            @"ui\game_setting",
            @"ui\shop",
            "game_setting",
            "shop"
        };

        internal static readonly string[] BuilderBaseTemplates =
        {
            @"village\Page\BuilderBase\BuilderEye_0_90",
            @"village\Page\BuilderBase\MachineEye_0_90",
            @"ui\builder_available",
            @"ui\x_night"
        };

        internal static readonly string[] MainVillageMarkerTemplates =
        {
            @"village\Page\MainVillage\MainVillage_100_90",
            @"village\Page\MainVillage\GobBuilder_100_92"
        };

        internal static readonly string[] MainVillageUiTemplates =
        {
            @"ui\game_setting",
            @"ui\shop",
            "game_setting",
            "shop"
        };

        internal static readonly string[] BuilderBaseMarkerTemplates =
        {
            @"village\Page\BuilderBase\BuilderEye_0_90",
            @"village\Page\BuilderBase\MachineEye_0_90"
        };

        internal static readonly string[] BuilderBaseFallbackTemplates =
        {
            @"ui\builder_available",
            @"ui\x_night"
        };

        internal static readonly string[] SwitchToBuilderTemplates =
        {
            @"ui\switch_builder",
            @"clan_games\switch_builder"
        };

        internal static readonly string[] SwitchToMainTemplates =
        {
            @"ui\home",
            @"ui\return_home",
            @"ui\return_home_n"
        };

        internal static readonly string[] StageTunnelTemplates =
        {
            @"ui\otto_tunnel",
            @"ui\builder_tunnel",
            @"ui\tunnel",
            @"builder_base\otto_tunnel",
            @"builder_base\builder_tunnel",
            @"builder_base\tunnel"
        };

        internal static readonly string[] BuilderBaseStage1Templates =
        {
            @"village\Page\BuilderBase\BuilderEye_0_90"
        };

        internal static readonly string[] OttoStageTemplates =
        {
            @"village\Page\BuilderBase\MachineEye_0_90"
        };
    }
}
