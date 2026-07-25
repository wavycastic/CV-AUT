using System;
using System.Collections.Generic;
using OpenCvSharp;
using Point = OpenCvSharp.Point;

namespace CvAut
{
    internal static class BuilderBaseAttackLayout
    {
        public const double ButtonThreshold = 0.62;
        public const double TroopThreshold = 0.50;
        public const int ScreenWidth = 1600;
        public const int ScreenHeight = 900;

        public static readonly Rect HomeAttackButtonRoi = Rect.FromLTRB(0, 560, 230, 850);
        public static readonly Rect AttackPrepTroopRoi = Rect.FromLTRB(40, 180, 360, 430);
        public static readonly Rect BattleButtonRoi = Rect.FromLTRB(500, 500, 1120, 850);
        public static readonly Rect FindNowButtonRoi = Rect.FromLTRB(1080, 520, 1540, 820);
        public static readonly Rect DeployBarRoi = Rect.FromLTRB(0, 700, 1250, 900);
        public static readonly Rect EnemyVillageRoi = Rect.FromLTRB(260, 60, 1340, 700);
        public static readonly Rect HeroBarRoi = Rect.FromLTRB(0, 690, 260, 900);
        public static readonly Rect CloseButtonRoi = Rect.FromLTRB(1320, 20, 1590, 220);
        public static readonly Rect ResultRoi = Rect.FromLTRB(250, 320, 1200, 880);
        public static readonly Rect DamageRoi = Rect.FromLTRB(720, 665, 880, 725);
        public static readonly Rect ResultDamageRoi = Rect.FromLTRB(700, 390, 900, 470);
        public static readonly Rect ResultStarsRoi = Rect.FromLTRB(610, 300, 990, 430);

        public static readonly string[] OpenAttackTemplates =
        {
            @"ui\attack_button",
            @"ui\icon_attack",
            @"ui\battle"
        };

        public static readonly string[] StartBattleTemplates =
        {
            @"ui\start_battle",
            @"ui\start_attack",
            @"ui\start_attack_n",
            @"ui\battle"
        };

        public static readonly string[] FindNowTemplates =
        {
            @"ui\find_now",
            @"ui\findnow",
            @"ui\start_battle",
            @"ui\battle"
        };

        public static readonly string[] ObstructedTemplates =
        {
            @"ui\obstructed",
            @"builder_base\obstructed",
            @"ui\cant_deploy"
        };

        public static readonly string[] CloseTemplates =
        {
            @"ui\x_night",
            @"ui\close",
            "close"
        };

        public static readonly string[] ReturnHomeTemplates =
        {
            @"ui\return_home",
            @"ui\return_home_n",
            @"ui\okay_battle_rank",
            @"ui\okay",
            @"ui\okay_n",
            @"ui\okay_n2",
            @"ui\okay_star",
            @"ui\bonus",
            @"ui\challenge_complete",
            @"ui\star_bonus_received"
        };

        public static readonly string[] ProblemAffectTemplates =
        {
            @"ui\Another_device.png",
            @"ui\Connection_lost.png",
            @"ui\Client_error!.png",
            @"ui\rate_coc.png",
            @"ui\conn.png",
            @"ui\maintenance",
            @"ui\reload",
            @"ui\out_of_sync",
            @"ui\disconnected"
        };

        public static readonly string[] SurrenderTemplates =
        {
            @"ui\surrender_button",
            @"ui\surrender",
            @"ui\surrender_rank"
        };

        public static readonly string[] SurrenderConfirmTemplates =
        {
            @"ui\surrender_window",
            @"ui\okay",
            @"ui\okay_n",
            @"ui\okay_n2"
        };

        public static readonly string[] BuilderTroopTemplates =
        {
            @"troops\builder_base\raged_barbarian",
            @"troops\builder_base\sneaky_archer",
            @"troops\builder_base\boxer_giant",
            @"troops\builder_base\beta_minion",
            @"troops\builder_base\bomber",
            @"troops\builder_base\baby_dragon_builder",
            @"troops\builder_base\cannon_cart",
            @"troops\builder_base\night_witch",
            @"troops\builder_base\drop_ship",
            @"troops\builder_base\power_pekka",
            @"troops\builder_base\hog_glider",
            @"troops\builder_base\electrofire_wizard",
            @"heroes\battle_machine",
            @"heroes\battle_machine2",
            @"heroes\battle_copter"
        };

        public static readonly string[] ActiveHeroTemplates =
        {
            @"heroes\battle_machine_a",
            @"heroes\battle_copter_a"
        };

        public static readonly string[] BomberAbilityTemplates =
        {
            @"troops\builder_base\bomber_ability",
            @"troops\builder_base\bomber_click",
            @"troops\builder_base\bomber"
        };

        public static readonly Dictionary<string, string> TroopNamesByTemplate = new(StringComparer.OrdinalIgnoreCase)
        {
            [@"troops\builder_base\raged_barbarian"] = "RagedBarbarian",
            [@"troops\builder_base\sneaky_archer"] = "SneakyArcher",
            [@"troops\builder_base\boxer_giant"] = "BoxerGiant",
            [@"troops\builder_base\beta_minion"] = "BetaMinion",
            [@"troops\builder_base\bomber"] = "Bomber",
            [@"troops\builder_base\baby_dragon_builder"] = "BabyDragon",
            [@"troops\builder_base\cannon_cart"] = "CannonCart",
            [@"troops\builder_base\night_witch"] = "NightWitch",
            [@"troops\builder_base\drop_ship"] = "DropShip",
            [@"troops\builder_base\power_pekka"] = "PowerPekka",
            [@"troops\builder_base\hog_glider"] = "HogGlider",
            [@"troops\builder_base\electrofire_wizard"] = "ElectrofireWizard",
            [@"heroes\battle_machine"] = "BattleMachine",
            [@"heroes\battle_machine2"] = "BattleMachine",
            [@"heroes\battle_copter"] = "BattleCopter"
        };

        public static readonly List<Point> TopLeftDrop = new()
        {
            new(210, 390), new(255, 350), new(305, 310), new(360, 270), new(425, 225), new(500, 170), new(590, 105)
        };

        public static readonly List<Point> TopRightDrop = new()
        {
            new(1390, 390), new(1345, 350), new(1295, 310), new(1240, 270), new(1175, 225), new(1100, 170), new(1010, 105)
        };

        public static readonly List<Point> BottomLeftDrop = new()
        {
            new(210, 520), new(270, 565), new(335, 610), new(405, 655), new(485, 695), new(575, 705), new(665, 670)
        };

        public static readonly List<Point> BottomRightDrop = new()
        {
            new(1390, 520), new(1330, 565), new(1265, 610), new(1195, 655), new(1115, 695), new(1025, 705), new(935, 670)
        };
    }
}
