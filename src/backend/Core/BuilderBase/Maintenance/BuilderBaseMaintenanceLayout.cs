using System;
using OpenCvSharp;

namespace CvAut
{
    internal static class BuilderBaseMaintenanceLayout
    {
        public const double ObjectThreshold = 0.62;
        public const double ButtonThreshold = 0.62;
        public const double RowThreshold = 0.58;

        public static readonly Rect MapRoi = Rect.FromLTRB(160, 75, 1440, 800);
        public static readonly Rect ActionButtonRoi = Rect.FromLTRB(360, 470, 1240, 850);
        public static readonly Rect BuilderMenuRoi = Rect.FromLTRB(430, 60, 1120, 650);
        public static readonly Rect SuggestedRowsRoi = Rect.FromLTRB(850, 90, 1190, 430);
        public static readonly Rect LaboratoryRoi = Rect.FromLTRB(120, 80, 1460, 790);
        public static readonly Rect ResearchRowsRoi = Rect.FromLTRB(220, 130, 1380, 760);
        public static readonly Rect ResearchTimerRoi = Rect.FromLTRB(610, 95, 990, 190);
        public static readonly Rect ResearchCostRoi = Rect.FromLTRB(720, 560, 1040, 650);
        public static readonly Rect ResearchConfirmTimeRoi = Rect.FromLTRB(820, 565, 1120, 650);
        public static readonly Rect BuildingInfoLevelRoi = Rect.FromLTRB(300, 430, 620, 535);
        public static readonly Rect HeroMapRoi = Rect.FromLTRB(120, 80, 1460, 790);

        public static readonly string[] BuilderHeadTemplates = { @"ui\master_builder_head", @"ui\builder_available", @"ui\builder_head" };
        public static readonly string[] UpgradeActionTemplates = { @"ui\open_upgrade", @"ui\open_upgrade2", @"ui\icon_up", @"icons\upgrade_more" };
        public static readonly string[] UpgradeConfirmGold = { @"builder_base\upgrade\gold", @"ui\upgrade_gold", @"resources\gold" };
        public static readonly string[] UpgradeConfirmElixir = { @"builder_base\upgrade\elixir", @"ui\upgrade_elixir", @"resources\elixir" };
        public static readonly string[] NoResourceTemplates = { @"builder_base\suggested\no_resources", @"ui\no_resources" };
        public static readonly string[] NewBuildingTemplates = { @"builder_base\suggested\new", @"ui\new_building" };
        public static readonly string[] StarLabTemplates = { @"builder_base\star_laboratory", @"ui\star_laboratory", @"ui\laboratory" };
        public static readonly string[] ResearchButtons = { @"ui\research", @"builder_base\research", @"buttons\research" };
        public static readonly string[] ResearchBusyTemplates = { @"ui\researching", @"builder_base\researching", @"builder_base\star_laboratory_busy" };
        public static readonly string[] ResearchMaxTemplates = { @"ui\max_level", @"builder_base\max_level", @"builder_base\research_max" };
        public static readonly string[] BattleMachineTemplates = { @"heroes\battle_machine", @"heroes\battle_machine2", @"builder_base\battle_machine" };
        public static readonly string[] BattleCopterTemplates = { @"heroes\battle_copter", @"builder_base\battle_copter" };
        public static readonly string[] BuilderHallTemplates = { @"builder_base\builder_hall", @"buildings\builder_hall" };
    }
}
