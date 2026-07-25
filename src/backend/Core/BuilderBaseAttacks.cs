using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using OpenCvSharp;
using Point = OpenCvSharp.Point;

namespace CvAut
{
    internal partial class BuilderBaseAttacks
    {
        private readonly IADBHelper _adb;
        private readonly IVisionEngine _vision;
        private readonly BuilderBaseNavigator _navigator;
        private readonly Random _random = new();
        private readonly List<BuilderBaseTroopSlot> _activeBomberSlots = new();
        private BuilderBaseDropPlanner? _currentDropPlanner;
        private int _startSlotMem = 21;
        private int _startSlotMem2 = 21;
        private int _clanGamesNoCompleteBarChecks;
        private int _machineLoopWaitCount;
        private int _machineLoopAbilityCount;

        private const double ButtonThreshold = 0.62;
        private const double TroopThreshold = 0.50;
        private const int ScreenWidth = 1600;
        private const int ScreenHeight = 900;

        private static readonly Rect HomeAttackButtonRoi = Rect.FromLTRB(0, 560, 230, 850);
        private static readonly Rect AttackPrepTroopRoi = Rect.FromLTRB(40, 180, 360, 430);
        private static readonly Rect BattleButtonRoi = Rect.FromLTRB(500, 500, 1120, 850);
        private static readonly Rect FindNowButtonRoi = Rect.FromLTRB(1080, 520, 1540, 820);
        private static readonly Rect DeployBarRoi = Rect.FromLTRB(0, 700, 1250, 900);
        private static readonly Rect EnemyVillageRoi = Rect.FromLTRB(260, 60, 1340, 700);
        private static readonly Rect HeroBarRoi = Rect.FromLTRB(0, 690, 260, 900);
        private static readonly Rect CloseButtonRoi = Rect.FromLTRB(1320, 20, 1590, 220);
        private static readonly Rect ResultRoi = Rect.FromLTRB(250, 320, 1200, 880);
        private static readonly Rect DamageRoi = Rect.FromLTRB(720, 665, 880, 725);
        private static readonly Rect ResultDamageRoi = Rect.FromLTRB(700, 390, 900, 470);
        private static readonly Rect ResultStarsRoi = Rect.FromLTRB(610, 300, 990, 430);

        private static readonly string[] OpenAttackTemplates =
        {
            @"ui\attack_button",
            @"ui\icon_attack",
            @"ui\battle"
        };

        private static readonly string[] StartBattleTemplates =
        {
            @"ui\start_battle",
            @"ui\start_attack",
            @"ui\start_attack_n",
            @"ui\battle"
        };

        private static readonly string[] FindNowTemplates =
        {
            @"ui\find_now",
            @"ui\findnow",
            @"ui\start_battle",
            @"ui\battle"
        };

        private static readonly string[] ObstructedTemplates =
        {
            @"ui\obstructed",
            @"builder_base\obstructed",
            @"ui\cant_deploy"
        };

        private static readonly string[] CloseTemplates =
        {
            @"ui\x_night",
            @"ui\close",
            "close"
        };

        private static readonly string[] ReturnHomeTemplates =
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

        private static readonly string[] ProblemAffectTemplates =
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

        private static readonly string[] SurrenderTemplates =
        {
            @"ui\surrender_button",
            @"ui\surrender",
            @"ui\surrender_rank"
        };

        private static readonly string[] SurrenderConfirmTemplates =
        {
            @"ui\surrender_window",
            @"ui\okay",
            @"ui\okay_n",
            @"ui\okay_n2"
        };

        private static readonly string[] BuilderTroopTemplates =
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

        private static readonly string[] ActiveHeroTemplates =
        {
            @"heroes\battle_machine_a",
            @"heroes\battle_copter_a"
        };

        private static readonly string[] BomberAbilityTemplates =
        {
            @"troops\builder_base\bomber_ability",
            @"troops\builder_base\bomber_click",
            @"troops\builder_base\bomber"
        };

        private static readonly Dictionary<string, string> TroopNamesByTemplate = new(StringComparer.OrdinalIgnoreCase)
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

        private static readonly List<Point> TopLeftDrop = new()
        {
            new(210, 390), new(255, 350), new(305, 310), new(360, 270), new(425, 225), new(500, 170), new(590, 105)
        };

        private static readonly List<Point> TopRightDrop = new()
        {
            new(1390, 390), new(1345, 350), new(1295, 310), new(1240, 270), new(1175, 225), new(1100, 170), new(1010, 105)
        };

        private static readonly List<Point> BottomLeftDrop = new()
        {
            new(210, 520), new(270, 565), new(335, 610), new(405, 655), new(485, 695), new(575, 705), new(665, 670)
        };

        private static readonly List<Point> BottomRightDrop = new()
        {
            new(1390, 520), new(1330, 565), new(1265, 610), new(1195, 655), new(1115, 695), new(1025, 705), new(935, 670)
        };

        public BuilderBaseAttacks(IADBHelper adb, IVisionEngine vision, BuilderBaseNavigator navigator)
        {
            _adb = adb;
            _vision = vision;
            _navigator = navigator;
        }

        public BuilderBaseBattleResult RunDropTrophyAttack(CancellationToken token)
        {
            Console.WriteLine("[BB-ATTACK] phase=attack status=start mode=drop_trophy");
            if (!_navigator.IsOnBuilderBase())
            {
                Console.WriteLine("[BB-ATTACK] phase=attack status=skip reason=not_on_builder_base");
                return new(false, 0, 0, false);
            }

            if (!WaitForAttackReady(token, "attack_entry", 3))
            {
                Console.WriteLine("[BB-ATTACK] phase=attack status=fail reason=attack_not_ready_after_retry");
                CaptureDebugSnapshot("attack_not_ready_after_retry");
                return new(false, 0, 0, false);
            }

            if (!TapFirstVisible(OpenAttackTemplates, ButtonThreshold, HomeAttackButtonRoi, token, out string openTemplate))
            {
                Console.WriteLine("[BB-ATTACK] phase=open_attack status=fail reason=button_not_found");
                CaptureDebugSnapshot("open_attack_button_not_found");
                return new(false, 0, 0, false);
            }

            if (Sleep(1800, token)) return new(false, 0, 0, false);

            if (!TapFirstVisible(StartBattleTemplates, ButtonThreshold, BattleButtonRoi, token, out string startTemplate))
            {
                Console.WriteLine("[BB-ATTACK] phase=start_battle status=fail reason=button_not_found");
                CloseAttackPrep(token);
                return new(false, 0, 0, false);
            }

            if (!ClickFindNowIfRequired(token))
            {
                CloseAttackPrep(token);
                return new(false, 0, 0, false);
            }

            if (!WaitCloudsAndEnemyVillage(token))
            {
                return new(false, 0, 0, false);
            }

            // Drop 1 troop then immediately surrender
            _adb.Tap(100, 750); // Tap troop slot
            if (Sleep(500, token)) return new(false, 0, 0, false);
            _adb.Tap(400, 450); // Drop troop on field
            if (Sleep(800, token)) return new(false, 0, 0, false);

            bool surrendered = ReturnHomeDropTrophyBB(token);
            Console.WriteLine($"[BB-ATTACK] phase=drop_trophy status=complete surrendered={surrendered}");
            return new(surrendered, 0, 0, false);
        }

        public bool RunSingleAttack(CancellationToken token)
            => RunSingleAttack(DefaultOptions(), token).ReturnedHome;

        public BuilderBaseBattleResult RunSingleAttack(BuilderBaseBattleOptions options, CancellationToken token)
        {
            Console.WriteLine($"[BB-ATTACK] phase=attack status=start mode=full custom_order={options.UseCustomDropOrder} next_delay={options.NextTroopDelayMs} same_delay={options.SameTroopDelayMs} bomber={options.HandleBomber} hero_loop=true");
            _clanGamesNoCompleteBarChecks = 0;
            _machineLoopWaitCount = 0;
            _machineLoopAbilityCount = 0;

            if (!_navigator.IsOnBuilderBase())
            {
                Console.WriteLine("[BB-ATTACK] phase=attack status=skip reason=not_on_builder_base");
                return new(false, 0, 0, false);
            }

            if (!WaitForAttackReady(token, "attack_entry", 3))
            {
                Console.WriteLine("[BB-ATTACK] phase=attack status=fail reason=attack_not_ready_after_retry");
                CaptureDebugSnapshot("attack_not_ready_after_retry");
                return new(false, 0, 0, false);
            }

            if (!TapFirstVisible(OpenAttackTemplates, ButtonThreshold, HomeAttackButtonRoi, token, out string openTemplate))
            {
                Console.WriteLine("[BB-ATTACK] phase=open_attack status=fail reason=button_not_found");
                CaptureDebugSnapshot("open_attack_button_not_found");
                return new(false, 0, 0, false);
            }

            Console.WriteLine($"[BB-ATTACK] phase=open_attack status=success template=\"{openTemplate}\"");
            if (Sleep(1800, token)) return new(false, 0, 0, false);

            if (!HasVisibleTroopsOnPrepScreen())
            {
                Console.WriteLine("[BB-ATTACK] phase=army_ready status=skip reason=troops_not_detected_on_prep_screen");
                CloseAttackPrep(token);
                return new(false, 0, 0, false);
            }

            Console.WriteLine("[BB-ATTACK] phase=army_ready status=success");

            if (!TapFirstVisible(StartBattleTemplates, ButtonThreshold, BattleButtonRoi, token, out string startTemplate))
            {
                Console.WriteLine("[BB-ATTACK] phase=start_battle status=retry reason=button_not_found action=wait_and_recheck");
                if (!WaitForAttackReady(token, "start_battle", 2) || !TapFirstVisible(StartBattleTemplates, ButtonThreshold, BattleButtonRoi, token, out startTemplate))
                {
                    Console.WriteLine("[BB-ATTACK] phase=start_battle status=fail reason=button_not_found_after_retry");
                    CloseAttackPrep(token);
                    return new(false, 0, 0, false);
                }
            }

            Console.WriteLine($"[BB-ATTACK] phase=start_battle status=success template=\"{startTemplate}\"");
            if (!ClickFindNowIfRequired(token))
            {
                CloseAttackPrep(token);
                return new(false, 0, 0, false);
            }

            if (!WaitCloudsAndEnemyVillage(token))
            {
                Console.WriteLine("[BB-ATTACK] phase=cloud status=fail reason=enemy_village_not_detected action=abort_attack");
                CloseAttackPrep(token);
                return new(false, 0, 0, false);
            }

            if (DetectObstructedLayout())
            {
                Console.WriteLine("[BB-ATTACK] phase=obstructed status=warning action=continue_with_safe_drop_points");
            }

            ZoomOutBattleView(token, "initial_attack");

            DeployAllVisibleTroops(options, token, secondAttack: false);
            BuilderBaseBattleResult result = WaitBattleAndReturn(options, token);

            bool returned = _navigator.IsOnBuilderBase();
            if (!returned && result.ReturnedHome)
            {
                Console.WriteLine("[BB-ATTACK] phase=attack status=pending action=verify_return_home reason=result_handled_but_builder_base_not_detected");
                returned = ReturnHomeDropTrophyBB(token);
            }
            Console.WriteLine($"[BB-ATTACK] phase=attack status={(returned ? "success" : "warning")} return_handled={result.ReturnedHome} returned_builder_base={returned} damage={result.Damage} stars={result.Stars} stage2={result.Stage2Entered}");
            return result with { ReturnedHome = returned };
        }

        private void DeployAllVisibleTroops(BuilderBaseBattleOptions options, CancellationToken token, bool secondAttack)
        {
            string attackSide = _random.Next(2) == 0 ? "left" : "right";
            _currentDropPlanner = BuildDropPlanner();
            List<Point> previewDropPoints = _currentDropPlanner.ChooseDropPoints("default", attackSide, _random);
            if (previewDropPoints.Count == 0)
            {
                Console.WriteLine("[BB-ATTACK] phase=deploy status=fail reason=no_valid_drop_points");
                return;
            }
            Console.WriteLine($"[BB-ATTACK] phase=deploy status=start attack_side={attackSide} side_points={previewDropPoints.Count} source={_currentDropPlanner.Source} second_attack={secondAttack}");
            _activeBomberSlots.Clear();
            List<BuilderBaseTroopSlot> remaining = ReadAttackBarSlots(remaining: false, secondAttack: secondAttack);
            Console.WriteLine($"[BB-ATTACK] phase=deploy status=attack_bar_refresh slots={remaining.Count}");
            if (remaining.Count == 0)
            {
                if (Sleep(700, token)) return;
                remaining = ReadAttackBarSlots(remaining: false, secondAttack: secondAttack);
                Console.WriteLine($"[BB-ATTACK] phase=deploy status=attack_bar_retry slots={remaining.Count}");
                if (remaining.Count == 0)
                {
                    CaptureDebugSnapshot("attack_bar_empty_before_deploy");
                    Console.WriteLine("[BB-ATTACK] phase=deploy status=fail reason=attack_bar_empty_before_deploy");
                    return;
                }
            }

            for (int pass = 1; pass <= 4 && !token.IsCancellationRequested; pass++)
            {
                List<BuilderBaseTroopSlot> slots = pass == 1 ? remaining : ReadAttackBarSlots(remaining: true, secondAttack: secondAttack);
                if (slots.Count == 0) break;
                foreach (BuilderBaseTroopSlot slot in OrderSlots(slots, options))
                {
                    if (token.IsCancellationRequested) return;
                    DeploySlot(slot, options, attackSide, token);
                }
            }

            Console.WriteLine("[BB-ATTACK] phase=deploy status=done");
        }

        private void DeploySlot(BuilderBaseTroopSlot slot, BuilderBaseBattleOptions options, string attackSide, CancellationToken token)
        {
            List<Point> dropPoints = (_currentDropPlanner ?? BuildDropPlanner()).ChooseDropPoints(slot.Name, attackSide, _random);
            if (dropPoints.Count == 0)
            {
                Console.WriteLine($"[BB-ATTACK] phase=deploy status=skip troop={slot.Name} reason=no_drop_points_for_troop");
                return;
            }

            string displayName = slot.Name;
            if (slot.Name.Equals("BattleMachine", StringComparison.OrdinalIgnoreCase))
            {
                Point? machinePos = GetMachinePos(out string machineName);
                displayName = string.IsNullOrWhiteSpace(machineName) ? slot.Name : machineName;
                Console.WriteLine($"[BB-ATTACK] phase=machine status=found name=\"{displayName}\" pos={(machinePos == null ? "unknown" : $"({machinePos.Value.X},{machinePos.Value.Y})")}");
            }

            _adb.Tap(slot.Center.X, slot.Center.Y);
            if (Sleep(_adb.FramePacer.AdjustDelay(Math.Clamp(options.SameTroopDelayMs, 50, 5000)), token)) return;

            int amount = Math.Clamp(slot.Count, 1, 12);
            Console.WriteLine($"[BB-ATTACK] phase=deploy status=slot troop={displayName} count={amount} slot={slot.Index} center=({slot.Center.X},{slot.Center.Y})");
            for (int i = 0; i < amount && !token.IsCancellationRequested; i++)
            {
                Point drop = dropPoints[i % dropPoints.Count];
                if (i == 0) drop = AvoidPotionArea(drop);
                _adb.Tap(drop.X, drop.Y);
                if (slot.Name.Contains("Bomber", StringComparison.OrdinalIgnoreCase) && options.HandleBomber)
                {
                    if (!_activeBomberSlots.Any(s => s.Index == slot.Index)) _activeBomberSlots.Add(slot);
                    Sleep(Math.Max(350, options.SameTroopDelayMs), token);
                    TryActivateBomberAbility(slot);
                }

                if (slot.Name.Equals("BattleMachine", StringComparison.OrdinalIgnoreCase) || slot.Name.Equals("BattleCopter", StringComparison.OrdinalIgnoreCase))
                {
                    ConfirmMachineDeployAndAbility(displayName, token);
                }

                if (Sleep(_adb.FramePacer.AdjustDelay(Math.Clamp(options.SameTroopDelayMs, 50, 5000)), token)) return;
            }

            Sleep(_adb.FramePacer.AdjustDelay(Math.Clamp(options.NextTroopDelayMs, 0, 10000)), token);
        }

        private void ActivateHeroAbility(CancellationToken token)
        {
            for (int attempt = 1; attempt <= 3 && !token.IsCancellationRequested; attempt++)
            {
                if (Sleep(1800, token)) return;

                using Mat? screenshot = _adb.TakeScreenshot();
                if (screenshot == null || screenshot.Empty()) return;

                foreach (string template in ActiveHeroTemplates)
                {
                    Point? center = _vision.FindElement(screenshot, template, 0.55, HeroBarRoi, out double score);
                    if (center == null) continue;

                    Console.WriteLine($"[BB-ATTACK] phase=hero_ability status=success template=\"{template}\" score={score:F2} attempt={attempt}");
                    _adb.Tap(center.Value.X, center.Value.Y);
                    return;
                }
            }

            Console.WriteLine("[BB-ATTACK] phase=hero_ability status=skip reason=active_hero_not_found");
        }

        private bool TryActivateHeroAbilityOnce()
        {
            using Mat? screenshot = _adb.TakeScreenshot();
            if (screenshot == null || screenshot.Empty()) return false;

            foreach (string template in ActiveHeroTemplates)
            {
                Point? center = _vision.FindElement(screenshot, template, 0.55, HeroBarRoi, out double score);
                if (center == null) continue;

                Console.WriteLine($"[BB-ATTACK] phase=hero_ability status=success template=\"{template}\" score={score:F2}");
                _adb.Tap(center.Value.X, center.Value.Y);
                return true;
            }

            return false;
        }

        private void TryActivateBomberAbility(BuilderBaseTroopSlot slot)
        {
            using Mat? screenshot = _adb.TakeScreenshot();
            if (screenshot == null || screenshot.Empty()) return;

            Rect roi = Rect.FromLTRB(Math.Max(0, slot.Center.X - 45), Math.Max(0, slot.Center.Y - 60), Math.Min(ScreenWidth, slot.Center.X + 95), Math.Min(ScreenHeight, slot.Center.Y + 50));
            Point? ability = null;
            double score = 0;
            foreach (string template in BomberAbilityTemplates)
            {
                ability = _vision.FindElement(screenshot, template, 0.45, roi, out score);
                if (ability != null) break;
            }
            if (ability == null)
            {
                ability = FindMbrReadyAbilityPixel(screenshot, roi);
                if (ability == null) return;
            }

            Console.WriteLine($"[BB-ATTACK] phase=bomber_ability status=success score={score:F2} slot={slot.Index} reason={(score > 0 ? "template" : "mbr_ready_pixel")}");
            _adb.Tap(ability.Value.X, ability.Value.Y);
        }

        private BuilderBaseBattleResult WaitBattleAndReturn(BuilderBaseBattleOptions options, CancellationToken token)
        {
            Console.WriteLine("[BB-ATTACK] phase=wait_end status=start");
            DateTime timeout = DateTime.Now.AddSeconds(150);
            int lastDamage = 0;
            int sameDamageTicks = 0;
            bool stage2 = false;

            while (DateTime.Now < timeout && !token.IsCancellationRequested)
            {
                if (BBGoldEnd("EndBattleBB"))
                {
                    int stars = ReadStars();
                    int finalDamage = Math.Max(lastDamage, ReadResultDamage());
                    Console.WriteLine($"[BB-ATTACK] phase=end_battle status=early_detected damage={finalDamage} stars={stars}");
                    bool returnedHome = ReturnHomeDropTrophyBB(token);
                    return new(returnedHome, finalDamage, stars, stage2);
                }

                CheckMachineAbilityLoop();
                if (options.HandleBomber) CheckBomberAbilityLoop();

                if (TryHandleProblemAffect(token, "EndBattleBB"))
                {
                    return new(false, lastDamage, ReadStars(), stage2);
                }

                int damage = ReadDamage();
                if (damage > 0)
                {
                    sameDamageTicks = damage == lastDamage ? sameDamageTicks + 1 : 0;
                    lastDamage = damage;
                    Console.WriteLine($"[BB-ATTACK] phase=damage status=read value={damage} same_ticks={sameDamageTicks}");
                }

                if (!stage2 && damage >= 100)
                {
                    Console.WriteLine("[BB-ATTACK] phase=stage2 status=pending action=wait_transition reason=damage_reached_100");
                    if (!WaitForStage2BattleReady(token))
                    {
                        Console.WriteLine("[BB-ATTACK] phase=stage2 status=skip reason=stage2_not_confirmed_possible_result_screen");
                        continue;
                    }

                    stage2 = true;
                    lastDamage = 0;
                    sameDamageTicks = 0;
                    ZoomOutBattleView(token, "stage2");
                    Console.WriteLine("[BB-ATTACK] phase=stage2 status=detected action=redeploy_remaining reason=attack_bar_ready");
                    DeployAllVisibleTroops(options, token, secondAttack: true);
                    timeout = DateTime.Now.AddSeconds(150);
                    continue;
                }

                if (sameDamageTicks >= 25 && damage > 0)
                {
                    Console.WriteLine($"[BB-ATTACK] phase=wait_end status=stalled action=surrender reason=same_damage_ticks damage={damage} ticks={sameDamageTicks}");
                    bool surrendered = ReturnHomeDropTrophyBB(token);
                    return new(surrendered, lastDamage, ReadStars(), stage2);
                }

                if (TryDismissBattlePopup(token))
                {
                    Console.WriteLine("[BB-ATTACK] phase=wait_end status=pending action=dismiss_popup");
                    continue;
                }

                if (TapFirstVisible(ReturnHomeTemplates, 0.48, ResultRoi, token, out string matched))
                {
                    if (IsBonusOrChallengeTemplate(matched))
                    {
                        Console.WriteLine($"[BB-ATTACK] phase=bonus status=detected template=\"{matched}\" action=acknowledge");
                    }

                    Console.WriteLine($"[BB-ATTACK] phase=return_home status=pending template=\"{matched}\"");
                    int stars = ReadStars();
                    int finalDamage = Math.Max(lastDamage, ReadResultDamage());
                    Console.WriteLine($"[BB-ATTACK] phase=result status=read damage={finalDamage} stars={stars}");
                    for (int verify = 1; verify <= 3 && !token.IsCancellationRequested; verify++)
                    {
                        Sleep(1200, token);
                        if (_navigator.IsOnBuilderBase())
                        {
                            Console.WriteLine($"[BB-ATTACK] phase=return_home status=success verify={verify}");
                            return new(true, finalDamage, stars, stage2);
                        }
                    }
                    Console.WriteLine("[BB-ATTACK] phase=return_home status=pending reason=button_tapped_but_base_not_detected");
                    continue;
                }

                if (BBGoldEnd("EndBattleBB"))
                {
                    int stars = ReadStars();
                    int finalDamage = Math.Max(lastDamage, ReadResultDamage());
                    Console.WriteLine($"[BB-ATTACK] phase=end_battle status=success reason=result_sentinel damage={finalDamage} stars={stars}");
                    return new(true, finalDamage, stars, stage2);
                }

                if (_navigator.IsOnBuilderBase())
                {
                    return new(true, lastDamage, ReadStars(), stage2);
                }

                if (IsBBAttackPage())
                {
                    Console.WriteLine("[BB-ATTACK] phase=wait_end status=pending reason=attack_page_active");
                }

                if (TryHandleProblemAffect(token, "EndBattleBB"))
                {
                    return new(false, lastDamage, ReadStars(), stage2);
                }

                Sleep(3000, token);
            }

            if (token.IsCancellationRequested) return new(false, lastDamage, 0, stage2);

            Console.WriteLine("[BB-ATTACK] phase=wait_end status=timeout_or_stalled action=surrender_fallback");
            bool returned = ReturnHomeDropTrophyBB(token);
            return new(returned, lastDamage, ReadStars(), stage2);
        }

        private bool ReturnHomeDropTrophyBB(CancellationToken token)
        {
            Console.WriteLine("[BB-ATTACK] phase=return_home status=start");

            for (int attempt = 1; attempt <= 15 && !token.IsCancellationRequested; attempt++)
            {
                if (_navigator.IsOnBuilderBase())
                {
                    Console.WriteLine("[BB-ATTACK] phase=return_home status=success reason=already_on_builder_base");
                    return true;
                }

                if (IsBBAttackPage())
                {
                    using Mat? screenshot = _adb.TakeScreenshot();
                    if (screenshot == null || screenshot.Empty())
                    {
                        Console.WriteLine($"[BB-ATTACK] phase=return_home status=pending action=surrender attempt={attempt} reason=screenshot_unavailable");
                        if (Sleep(1000, token)) return false;
                        continue;
                    }

                    Point surrenderPoint = new(
                        (int)Math.Round(65 * screenshot.Width / 860.0),
                        (int)Math.Round(540 * screenshot.Height / 732.0));

                    _adb.Tap(surrenderPoint.X, surrenderPoint.Y);
                    Console.WriteLine($"[BB-ATTACK] phase=return_home status=pending action=surrender attempt={attempt} point=({surrenderPoint.X},{surrenderPoint.Y})");
                    if (Sleep(1000, token)) return false;
                    continue;
                }

                if (TapFirstVisible(SurrenderTemplates, 0.52, null, token, out string surrenderTemplate))
                {
                    Console.WriteLine($"[BB-ATTACK] phase=return_home status=pending template=\"{surrenderTemplate}\" attempt={attempt}");
                    if (Sleep(1000, token)) return false;
                }

                if (TapFirstVisible(ReturnHomeTemplates, 0.45, ResultRoi, token, out string returnTemplate))
                {
                    if (IsBonusOrChallengeTemplate(returnTemplate))
                    {
                        Console.WriteLine($"[BB-ATTACK] phase=bonus status=detected template=\"{returnTemplate}\" action=acknowledge");
                    }

                    Console.WriteLine($"[BB-ATTACK] phase=return_home status=pending template=\"{returnTemplate}\" attempt={attempt}");
                    if (Sleep(1800, token)) return false;
                    if (_navigator.IsOnBuilderBase())
                    {
                        Console.WriteLine("[BB-ATTACK] phase=return_home status=success reason=builder_base_detected");
                        return true;
                    }
                }

                if (TapFirstVisible(SurrenderConfirmTemplates, 0.52, ResultRoi, token, out string confirmTemplate))
                {
                    Console.WriteLine($"[BB-ATTACK] phase=surrender_confirm status=pending template=\"{confirmTemplate}\" attempt={attempt}");
                    if (Sleep(1800, token)) return false;
                }
            }

            Console.WriteLine("[BB-ATTACK] phase=return_home status=fail reason=not_returned_after_attempts");
            return _navigator.IsOnBuilderBase();
        }

        private Point? GetMachinePos(out string machineName)
        {
            machineName = string.Empty;
            using Mat? screenshot = _adb.TakeScreenshot();
            if (screenshot == null || screenshot.Empty()) return null;
            return GetMachinePos(screenshot, out machineName);
        }

        private Point? GetMachinePos(Mat screenshot, out string machineName)
        {
            machineName = string.Empty;
            foreach ((string Template, string Name) candidate in new[]
            {
                (@"heroes\battle_copter", "Battle Copter"),
                (@"heroes\battle_copter_a", "Battle Copter"),
                (@"heroes\battle_machine", "Battle Machine"),
                (@"heroes\battle_machine2", "Battle Machine"),
                (@"heroes\battle_machine_a", "Battle Machine")
            })
            {
                Point? center = _vision.FindElement(screenshot, candidate.Template, 0.48, HeroBarRoi, out double score);
                if (center == null) continue;
                machineName = candidate.Name;
                Console.WriteLine($"[BB-ATTACK] phase=machine status=detect template=\"{candidate.Template}\" score={score:F2} center=({center.Value.X},{center.Value.Y})");
                return center;
            }

            return null;
        }

        private void ConfirmMachineDeployAndAbility(string machineName, CancellationToken token)
        {
            for (int attempt = 1; attempt <= 16 && !token.IsCancellationRequested; attempt++)
            {
                if (Sleep(250, token)) return;
                if (TryActivateHeroAbilityOnce())
                {
                    Console.WriteLine($"[BB-ATTACK] phase=machine status=deployed action=ability name=\"{machineName}\" attempt={attempt}");
                    return;
                }
            }

            Console.WriteLine($"[BB-ATTACK] phase=machine status=deployed action=ability_not_ready name=\"{machineName}\"");
        }

        private const string DefaultDropOrderSequence = "BattleMachine|BattleCopter|BoxerGiant|DropShip|HogGlider|Bomber|SuperPekka|PowerPekka|BabyDragon|CannonCart|ElectrofireWizard|NightWitch|RagedBarbarian|BetaMinion|SneakyArcher";

        internal static IEnumerable<BuilderBaseTroopSlot> OrderSlots(List<BuilderBaseTroopSlot> slots, BuilderBaseBattleOptions options)
        {
            var ordered = new List<BuilderBaseTroopSlot>();

            // Always drop Hero (Battle Machine / Battle Copter) FIRST
            ordered.AddRange(slots.Where(s => IsHeroSlot(s.Name) && !ordered.Contains(s)));

            string sequence = options.UseCustomDropOrder && !string.IsNullOrWhiteSpace(options.DropOrder)
                ? options.DropOrder
                : DefaultDropOrderSequence;
            foreach (string raw in sequence.Split(new[] { '|', ',', ';' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                ordered.AddRange(slots.Where(s => NamesMatch(s.Name, raw) && !ordered.Contains(s)));
            }

            ordered.AddRange(slots.Where(s => !ordered.Contains(s)));
            return ordered;
        }

        private static bool IsHeroSlot(string name)
        {
            return NamesMatch(name, "BattleMachine") || NamesMatch(name, "BattleCopter") || NamesMatch(name, "Hero") || NamesMatch(name, "Machine") || NamesMatch(name, "Copter");
        }

        private static bool NamesMatch(string actual, string requested)
        {
            static string Normalize(string s) => s.Replace("_", "", StringComparison.OrdinalIgnoreCase).Replace(" ", "", StringComparison.OrdinalIgnoreCase).Replace("-", "", StringComparison.OrdinalIgnoreCase).ToLowerInvariant();
            string act = Normalize(actual);
            string req = Normalize(requested);
            return act == req || act.Contains(req) || req.Contains(act);
        }

        private static BuilderBaseBattleOptions DefaultOptions() => new(
            DropOrder: DefaultDropOrderSequence,
            UseCustomDropOrder: false,
            NextTroopDelayMs: 600,
            SameTroopDelayMs: 180,
            HandleBomber: true);

        private static bool IsNearExisting(IEnumerable<Point> points, Point candidate)
        {
            foreach (Point point in points)
            {
                int dx = point.X - candidate.X;
                int dy = point.Y - candidate.Y;
                if (dx * dx + dy * dy <= 55 * 55) return true;
            }

            return false;
        }

        private void CaptureDebugSnapshot(string reason)
        {
            try
            {
                using Mat? screenshot = _adb.TakeScreenshot();
                if (screenshot == null || screenshot.Empty()) return;
                string dir = Path.Combine(AppContext.BaseDirectory, "debug", "bb");
                Directory.CreateDirectory(dir);
                string safeReason = string.Concat(reason.Select(ch => char.IsLetterOrDigit(ch) || ch == '_' || ch == '-' ? ch : '_'));
                string file = Path.Combine(dir, $"{DateTime.Now:yyyyMMdd_HHmmss_fff}_{safeReason}.png");
                Cv2.ImWrite(file, screenshot);
                Console.WriteLine($"[BB-ATTACK] phase=debug_snapshot status=saved reason={safeReason} file=\"{file}\"");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[BB-ATTACK] phase=debug_snapshot status=fail reason=exception message=\"{ex.Message}\"");
            }
        }

        private static bool Sleep(int milliseconds, CancellationToken token)
        {
            return token.WaitHandle.WaitOne(milliseconds);
        }
    }
}
