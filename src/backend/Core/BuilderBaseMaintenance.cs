using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using OpenCvSharp;
using Point = OpenCvSharp.Point;

namespace CvAut
{
    internal sealed record BuilderBaseMaintenanceOptions(
        bool SuggestedUpgrades,
        bool StarLaboratory,
        bool UpgradeBattleMachine,
        bool UpgradeBattleCopter,
        bool PlaceNewBuildings,
        bool IgnoreGoldUpgrades,
        bool IgnoreElixirUpgrades,
        bool IgnoreHallUpgrades,
        bool IgnoreWallUpgrades,
        string StarLaboratoryTroop,
        int VillageIdx,
        bool StarLaboratoryDebugScreenshots);

    /// <summary>
    /// Port an toàn các maintenance task Builder Base từ MBR.
    /// MBR dùng XML image pack riêng; bản C# chỉ thao tác khi template PNG/DAT tương ứng tồn tại
    /// và match được trên màn hình, tránh click mù khi asset chưa được port.
    /// </summary>
    internal sealed partial class BuilderBaseMaintenance
    {
        private readonly IADBHelper _adb;
        private readonly IVisionEngine _vision;
        private readonly BuilderBaseNavigator _navigator;
        private readonly string _templatesPath;
        private DateTime? _starLabUpgradeFinishUtc;

        private const double ObjectThreshold = 0.62;
        private const double ButtonThreshold = 0.62;
        private const double RowThreshold = 0.58;

        private static readonly Rect MapRoi = Rect.FromLTRB(160, 75, 1440, 800);
        private static readonly Rect ActionButtonRoi = Rect.FromLTRB(360, 470, 1240, 850);
        private static readonly Rect BuilderMenuRoi = Rect.FromLTRB(430, 60, 1120, 650);
        private static readonly Rect SuggestedRowsRoi = Rect.FromLTRB(850, 90, 1190, 430);
        private static readonly Rect LaboratoryRoi = Rect.FromLTRB(120, 80, 1460, 790);
        private static readonly Rect ResearchRowsRoi = Rect.FromLTRB(220, 130, 1380, 760);
        private static readonly Rect ResearchTimerRoi = Rect.FromLTRB(610, 95, 990, 190);
        private static readonly Rect ResearchCostRoi = Rect.FromLTRB(720, 560, 1040, 650);
        private static readonly Rect ResearchConfirmTimeRoi = Rect.FromLTRB(820, 565, 1120, 650);
        private static readonly Rect BuildingInfoLevelRoi = Rect.FromLTRB(300, 430, 620, 535);
        private static readonly Rect HeroMapRoi = Rect.FromLTRB(120, 80, 1460, 790);

        
        private static readonly string[] BuilderHeadTemplates = { @"ui\master_builder_head", @"ui\builder_available", @"ui\builder_head" };
        private static readonly string[] UpgradeActionTemplates = { @"ui\open_upgrade", @"ui\open_upgrade2", @"ui\icon_up", @"icons\upgrade_more" };
        private static readonly string[] UpgradeConfirmGold = { @"builder_base\upgrade\gold", @"ui\upgrade_gold", @"resources\gold" };
        private static readonly string[] UpgradeConfirmElixir = { @"builder_base\upgrade\elixir", @"ui\upgrade_elixir", @"resources\elixir" };
        private static readonly string[] NoResourceTemplates = { @"builder_base\suggested\no_resources", @"ui\no_resources" };
        private static readonly string[] NewBuildingTemplates = { @"builder_base\suggested\new", @"ui\new_building" };
        private static readonly string[] StarLabTemplates = { @"builder_base\star_laboratory", @"ui\star_laboratory", @"ui\laboratory" };
        private static readonly string[] ResearchButtons = { @"ui\research", @"builder_base\research", @"buttons\research" };
        private static readonly string[] ResearchBusyTemplates = { @"ui\researching", @"builder_base\researching", @"builder_base\star_laboratory_busy" };
        private static readonly string[] ResearchMaxTemplates = { @"ui\max_level", @"builder_base\max_level", @"builder_base\research_max" };
        private static readonly string[] BattleMachineTemplates = { @"heroes\battle_machine", @"heroes\battle_machine2", @"builder_base\battle_machine" };
        private static readonly string[] BattleCopterTemplates = { @"heroes\battle_copter", @"builder_base\battle_copter" };
        private static readonly string[] BuilderHallTemplates = { @"builder_base\builder_hall", @"buildings\builder_hall" };
        
        private static readonly StarLabTroopInfo[] StarLabTroops =
        {
            new(1, "raged_barbarian", "Raged Barbarian", new Point(114, 341), new[] { "raged_barbarian", "barbarian", "ragedbarbarian" }),
            new(2, "sneaky_archer", "Sneaky Archer", new Point(114, 449), new[] { "sneaky_archer", "sneakyarcher", "archer" }),
            new(3, "boxer_giant", "Boxer Giant", new Point(213, 341), new[] { "boxer_giant", "boxer_giants", "boxergiant", "giant" }),
            new(4, "beta_minion", "Beta Minion", new Point(213, 449), new[] { "beta_minion", "betaminion", "minion" }),
            new(5, "bomber", "Bomber", new Point(314, 341), new[] { "bomber" }),
            new(6, "baby_dragon", "Baby Dragon", new Point(314, 449), new[] { "baby_dragon", "baby_dragon_builder", "babydragon" }),
            new(7, "cannon_cart", "Cannon Cart", new Point(416, 341), new[] { "cannon_cart", "cannoncart" }),
            new(8, "night_witch", "Night Witch", new Point(416, 449), new[] { "night_witch", "nightwitch" }),
            new(9, "drop_ship", "Drop Ship", new Point(516, 341), new[] { "drop_ship", "dropship" }),
            new(10, "super_pekka", "Super Pekka", new Point(516, 449), new[] { "super_pekka", "power_pekka", "superpekka", "pekka" }),
            new(11, "hog_glider", "Hog Glider", new Point(622, 341), new[] { "hog_glider", "hogglider" }),
            new(12, "electrofire_wizard", "Electrofire Wizard", new Point(622, 449), new[] { "electrofire_wizard", "electro_fire_wizard", "efwizard", "wizard" })
        };

        public BuilderBaseMaintenance(IADBHelper adb, IVisionEngine vision, BuilderBaseNavigator navigator, string templatesPath)
        {
            _adb = adb;
            _vision = vision;
            _navigator = navigator;
            _templatesPath = templatesPath;
        }

        public BuilderBaseMaintenanceResult Run(BuilderBaseMaintenanceOptions options, BuilderBaseReportSnapshot report, CancellationToken token)
        {
            if (!_navigator.IsOnBuilderBase())
            {
                Console.WriteLine("[BB-MAINT] phase=maintenance status=skip reason=not_on_builder_base");
                return new BuilderBaseMaintenanceResult(0, 0, 0);
            }

            int upgrades = 0, research = 0, hero = 0;
            if (options.SuggestedUpgrades) upgrades = SuggestedUpgrades(options, report, token);
            if (options.StarLaboratory) research = TryStartStarLaboratoryResearch(options, report, token) ? 1 : 0;
            if (options.UpgradeBattleMachine) hero += TryUpgradeHero("battle_machine", BattleMachineTemplates, report, token) ? 1 : 0;
            if (options.UpgradeBattleCopter) hero += TryUpgradeHero("battle_copter", BattleCopterTemplates, report, token) ? 1 : 0;
            return new BuilderBaseMaintenanceResult(upgrades, research, hero);
        }

        

        private int SuggestedUpgrades(BuilderBaseMaintenanceOptions options, BuilderBaseReportSnapshot report, CancellationToken token)
        {
            if (report.FreeBuilders == 0)
            {
                Console.WriteLine("[BB-MAINT] phase=suggested_upgrades status=skip reason=no_free_builder");
                return 0;
            }
            if (!OpenBuilderMenu(token)) return 0;
            Console.WriteLine($"[BB-MAINT] phase=suggested_upgrades status=start ignore_gold={options.IgnoreGoldUpgrades} ignore_elixir={options.IgnoreElixirUpgrades} ignore_hall={options.IgnoreHallUpgrades} ignore_wall={options.IgnoreWallUpgrades} place_new={options.PlaceNewBuildings}");

            int upgraded = 0;
            for (int i = 0; i < Math.Max(1, report.FreeBuilders) && !token.IsCancellationRequested; i++)
            {
                using Mat? screenshot = _adb.TakeScreenshot();
                if (screenshot == null || screenshot.Empty()) break;
                if (TemplateSearch.IsAnyVisible(screenshot, FindElementWithExistenceCheck, NoResourceTemplates, RowThreshold, SuggestedRowsRoi)) break;

                SuggestedUpgradeCandidate? candidate = FindSuggestedUpgradeCandidate(screenshot, options, report);
                if (candidate == null) break;

                Console.WriteLine($"[BB-MAINT] phase=suggested_upgrades status=pending action=open_row resource={candidate.Resource} template=\"{candidate.Template}\" score={candidate.Score:F2} cost={candidate.Cost}");
                Point row = candidate.Center;
                _adb.Tap(row.X, row.Y);
                if (Sleep(1200, token)) break;
                if (!TapFirstExisting(UpgradeActionTemplates, ButtonThreshold, ActionButtonRoi, token, "suggested_upgrade_action")) { SafeDismiss(token); break; }
                Sleep(900, token);
                string[] confirmTemplates = candidate.Resource == "gold" ? UpgradeConfirmGold : candidate.Resource == "elixir" ? UpgradeConfirmElixir : UpgradeConfirmGold.Concat(UpgradeConfirmElixir).ToArray();
                if (TapFirstExisting(confirmTemplates, ButtonThreshold, ActionButtonRoi, token, "suggested_upgrade_confirm")) upgraded++;
                Sleep(900, token);
                SafeDismiss(token);
                OpenBuilderMenu(token);
            }
            SafeDismiss(token);
            Console.WriteLine($"[BB-MAINT] phase=suggested_upgrades status=done upgraded={upgraded}");
            return upgraded;
        }

        private SuggestedUpgradeCandidate? FindSuggestedUpgradeCandidate(Mat screenshot, BuilderBaseMaintenanceOptions options, BuilderBaseReportSnapshot report)
        {
            bool canGold = !options.IgnoreGoldUpgrades && report.Gold > 0;
            bool canElixir = !options.IgnoreElixirUpgrades && report.Elixir > 0;
            var rowGroups = new List<(string Resource, string[] Templates)>
            {
                ("gold", canGold ? UpgradeConfirmGold : Array.Empty<string>()),
                ("elixir", canElixir ? UpgradeConfirmElixir : Array.Empty<string>())
            };

            if (options.PlaceNewBuildings)
            {
                rowGroups.Add(("new_building", NewBuildingTemplates));
            }

            if (options.IgnoreHallUpgrades)
            {
                if (TemplateSearch.IsAnyVisible(screenshot, FindElementWithExistenceCheck, BuilderHallTemplates, RowThreshold, SuggestedRowsRoi))
                {
                    Console.WriteLine("[BB-MAINT] phase=suggested_upgrades status=skip reason=builder_hall_ignored");
                    rowGroups.RemoveAll(g => g.Resource == "new_building");
                }
            }

            var candidates = new List<SuggestedUpgradeCandidate>();
            foreach ((string resource, string[] templates) in rowGroups)
            {
                if (templates.Length == 0) continue;
                Point? center = TemplateSearch.FindFirst(screenshot, FindElementWithExistenceCheck, templates, RowThreshold, SuggestedRowsRoi, out string template, out double score);
                if (center == null) continue;
                int cost = ReadSuggestedUpgradeCost(screenshot, center.Value, resource);
                candidates.Add(new SuggestedUpgradeCandidate(resource, template, center.Value, score, cost));
                Console.WriteLine($"[BB-MAINT] phase=suggested_upgrades status=found resource={resource} template=\"{template}\" score={score:F2} cost={cost} center=({center.Value.X},{center.Value.Y})");
            }

            if (candidates.Count == 0)
            {
                Console.WriteLine("[BB-MAINT] phase=suggested_upgrades status=skip reason=no_candidate_visible");
                return null;
            }

            return candidates.Where(c => IsSuggestedCandidateAllowed(c, report, canGold, canElixir))
                .OrderBy(c => SuggestedResourcePriority(c.Resource))
                .ThenBy(c => c.Cost <= 0 ? int.MaxValue : c.Cost)
                .ThenByDescending(c => c.Score)
                .FirstOrDefault();
        }

        private static bool IsSuggestedCandidateAllowed(SuggestedUpgradeCandidate candidate, BuilderBaseReportSnapshot report, bool canGold, bool canElixir)
        {
            if (candidate.Resource == "gold")
            {
                if (!canGold) return false;
                return candidate.Cost <= 0 || candidate.Cost <= report.Gold;
            }

            if (candidate.Resource == "elixir")
            {
                if (!canElixir) return false;
                return candidate.Cost <= 0 || candidate.Cost <= report.Elixir;
            }

            return true;
        }

        private static int SuggestedResourcePriority(string resource)
        {
            return resource switch
            {
                "gold" => 0,
                "elixir" => 1,
                "new_building" => 2,
                _ => 9
            };
        }

        private int ReadSuggestedUpgradeCost(Mat screenshot, Point center, string resource)
        {
            Rect roi = resource == "gold"
                ? Rect.FromLTRB(center.X + 12, center.Y + 70, center.X + 190, center.Y + 118)
                : Rect.FromLTRB(center.X + 12, center.Y + 72, center.X + 190, center.Y + 122);
            int cost = ReadNumberFromCurrentScreen(roi, 100_000_000);
            Console.WriteLine($"[BB-MAINT] phase=suggested_upgrades_ocr status={(cost > 0 ? "success" : "fail")} resource={resource} value={cost} center=({center.X},{center.Y})");
            return cost;
        }

        private bool TryStartStarLaboratoryResearch(BuilderBaseMaintenanceOptions options, BuilderBaseReportSnapshot report, CancellationToken token)
        {
            LoadStarLabRuntime(options.VillageIdx);
            Console.WriteLine($"[BB-MAINT] phase=star_laboratory status=start troop_mode=\"{options.StarLaboratoryTroop}\" village={options.VillageIdx} elixir={report.Elixir}");
            if (_starLabUpgradeFinishUtc is DateTime finishUtc && finishUtc > DateTime.UtcNow)
            {
                Console.WriteLine($"[BB-MAINT] phase=star_laboratory status=skip reason=known_research_busy finish_utc=\"{finishUtc:O}\" remaining_minutes={(int)Math.Ceiling((finishUtc - DateTime.UtcNow).TotalMinutes)}");
                return false;
            }

            if (report.Elixir <= 0)
            {
                Console.WriteLine("[BB-MAINT] phase=star_laboratory status=skip reason=no_elixir");
                return false;
            }

            if (!LocateAndOpenStarLaboratory(options.VillageIdx, token))
            {
                Console.WriteLine("[BB-MAINT] phase=star_laboratory status=skip reason=laboratory_not_found");
                return false;
            }
            Sleep(1000, token);

            using Mat? screenshot = _adb.TakeScreenshot();
            if (screenshot == null || screenshot.Empty())
            {
                Console.WriteLine("[BB-MAINT] phase=star_laboratory status=skip reason=screenshot_failed");
                return false;
            }

            if (TemplateSearch.IsAnyVisible(screenshot, FindElementWithExistenceCheck, ResearchBusyTemplates, RowThreshold, ResearchTimerRoi))
            {
                SaveStarLabDebugScreenshot(options, "busy_timer");
                int timer = ReadStarLabTimeMinutes(ResearchTimerRoi, "busy_timer");
                if (timer > 0) RecordStarLabFinish(options.VillageIdx, DateTime.UtcNow.AddMinutes(timer), "busy_timer");
                Console.WriteLine($"[BB-MAINT] phase=star_laboratory status=skip reason=research_busy timer_ocr={timer}");
                SafeDismiss(token);
                return false;
            }

            if (TemplateSearch.IsAnyVisible(screenshot, FindElementWithExistenceCheck, ResearchMaxTemplates, RowThreshold, ResearchRowsRoi))
            {
                Console.WriteLine("[BB-MAINT] phase=star_laboratory status=skip reason=max_or_unavailable");
                SafeDismiss(token);
                return false;
            }

            StarLabCandidate[] candidates = FindStarLabCandidates(screenshot, options.StarLaboratoryTroop);
            StarLabCandidate? selectedCandidate = SelectStarLabCandidate(candidates, options.StarLaboratoryTroop, report.Elixir);
            if (selectedCandidate == null)
            {
                Console.WriteLine($"[BB-MAINT] phase=star_laboratory status=skip reason=no_affordable_candidate candidates={candidates.Length} elixir={report.Elixir}");
                SafeDismiss(token);
                return false;
            }

            Console.WriteLine($"[BB-MAINT] phase=star_laboratory status=pending action=select_candidate troop=\"{selectedCandidate.DisplayName}\" cost={selectedCandidate.Cost} state={selectedCandidate.State} score={selectedCandidate.Score:F2}");
            _adb.Tap(selectedCandidate.Center.X, selectedCandidate.Center.Y);
            if (Sleep(700, token)) return false;

            if (IsStarLabTroopBlocked(selectedCandidate, out string blockedReason))
            {
                Console.WriteLine($"[BB-MAINT] phase=star_laboratory status=skip troop=\"{selectedCandidate.DisplayName}\" reason={blockedReason}");
                SafeDismiss(token);
                return false;
            }

            int cost = selectedCandidate.Cost > 0 ? selectedCandidate.Cost : ReadNumberFromCurrentScreen(ResearchCostRoi, 100_000_000);
            if (cost > 0 && cost > report.Elixir)
            {
                Console.WriteLine($"[BB-MAINT] phase=star_laboratory status=skip reason=not_enough_elixir cost={cost} elixir={report.Elixir}");
                SafeDismiss(token);
                return false;
            }

            bool started = TapFirstExisting(ResearchButtons, ButtonThreshold, ActionButtonRoi, token, "star_laboratory_research");
            if (started)
            {
                Sleep(700, token);
                SaveStarLabDebugScreenshot(options, "confirm_time");
                int minutes = ReadStarLabTimeMinutes(ResearchConfirmTimeRoi, "confirm_time");
                if (minutes > 0)
                {
                    RecordStarLabFinish(options.VillageIdx, DateTime.UtcNow.AddMinutes(minutes), "confirm_time");
                    Console.WriteLine($"[BB-MAINT] phase=star_laboratory status=pending action=record_finish minutes={minutes} finish_utc=\"{_starLabUpgradeFinishUtc:O}\"");
                }
            }
            Sleep(900, token);
            SafeDismiss(token);
            Console.WriteLine($"[BB-MAINT] phase=star_laboratory status={(started ? "success" : "skip")} troop=\"{selectedCandidate.DisplayName}\" cost={cost} troop_mode=\"{options.StarLaboratoryTroop}\"");
            return started;
        }

        private StarLabCandidate[] FindStarLabCandidates(Mat screenshot, string configuredTroop)
        {
            var candidates = new List<StarLabCandidate>();
            foreach (StarLabTroopInfo troop in SortStarLabTroops(configuredTroop))
            {
                Point? center = FindStarLabTroopCenter(screenshot, troop, out string source, out double score);
                if (center == null || IsNearExisting(candidates.Select(c => c.Center), center.Value)) continue;
                StarLabTroopState state = ReadStarLabTroopState(screenshot, center.Value);
                int cost = ReadStarLabResourceCost(center.Value, troop.DisplayName);
                candidates.Add(new StarLabCandidate(troop.Key, troop.DisplayName, source, center.Value, cost, score, troop.Index, state));
                Console.WriteLine($"[BB-MAINT] phase=star_laboratory status=found troop=\"{troop.DisplayName}\" key={troop.Key} source=\"{source}\" cost={cost} state={state} score={score:F2} center=({center.Value.X},{center.Value.Y})");
            }

            return candidates.ToArray();
        }

        private Point? FindStarLabTroopCenter(Mat screenshot, StarLabTroopInfo troop, out string source, out double score)
        {
            source = "grid";
            score = 0;
            foreach (string template in BuildStarLaboratoryTroopTemplates(troop))
            {
                Point? center = _vision.FindElement(screenshot, template, RowThreshold, ResearchRowsRoi, out score);
                if (center != null)
                {
                    source = template;
                    return center;
                }
            }

            Point grid = troop.DefaultCenter;
            if (grid.X >= ResearchRowsRoi.Left && grid.X <= ResearchRowsRoi.Right && grid.Y >= ResearchRowsRoi.Top && grid.Y <= ResearchRowsRoi.Bottom)
            {
                StarLabTroopState state = ReadStarLabTroopState(screenshot, grid);
                if (state != StarLabTroopState.NotPresent)
                {
                    score = 0.5;
                    return grid;
                }
            }

            return null;
        }

        private static StarLabCandidate? SelectStarLabCandidate(StarLabCandidate[] candidates, string configuredTroop, int availableElixir)
        {
            if (candidates.Length == 0) return null;
            bool auto = string.IsNullOrWhiteSpace(configuredTroop) || configuredTroop.Equals("auto", StringComparison.OrdinalIgnoreCase);
            IEnumerable<StarLabCandidate> affordable = candidates.Where(c => c.Cost <= 0 || c.Cost <= availableElixir);
            if (!auto)
            {
                StarLabCandidate? exact = affordable.FirstOrDefault(c => c.Key.Equals(NormalizeStarLabTroopKey(configuredTroop), StringComparison.OrdinalIgnoreCase)
                    || c.DisplayName.Equals(configuredTroop, StringComparison.OrdinalIgnoreCase));
                if (exact != null) return exact;
            }

            return affordable.OrderBy(c => c.Cost <= 0 ? int.MaxValue : c.Cost).FirstOrDefault();
        }

        private bool IsStarLabTroopBlocked(StarLabCandidate candidate, out string reason)
        {
            using Mat? screenshot = _adb.TakeScreenshot();
            if (screenshot == null || screenshot.Empty()) { reason = "screenshot_failed"; return true; }

            StarLabTroopState state = ReadStarLabTroopState(screenshot, candidate.Center);
            if (state != StarLabTroopState.Upgradeable && state != StarLabTroopState.Unknown)
            {
                reason = state.ToString().ToLowerInvariant();
                return true;
            }

            reason = string.Empty;
            return false;
        }

        private int ReadNumberNear(Point center, int maxPlausible)
        {
            Rect roi = Rect.FromLTRB(center.X - 10, center.Y + 45, center.X + 180, center.Y + 105);
            return ReadNumberFromCurrentScreen(roi, maxPlausible);
        }

        private int ReadStarLabResourceCost(Point troopCenter, string troopName)
        {
            Rect redRoi = Rect.FromLTRB(troopCenter.X + 2, troopCenter.Y + 76, troopCenter.X + 172, troopCenter.Y + 112);
            int red = ReadNumberFromCurrentScreen(redRoi, 100_000_000);
            if (red >= 3000)
            {
                Console.WriteLine($"[BB-MAINT] phase=star_laboratory_ocr status=success mode=resource_red troop=\"{troopName}\" value={red}");
                return red;
            }

            Rect whiteRoi = Rect.FromLTRB(troopCenter.X + 2, troopCenter.Y + 86, troopCenter.X + 180, troopCenter.Y + 124);
            int white = ReadNumberFromCurrentScreen(whiteRoi, 100_000_000);
            if (white >= 3000)
            {
                Console.WriteLine($"[BB-MAINT] phase=star_laboratory_ocr status=success mode=resource_white troop=\"{troopName}\" value={white}");
                return white;
            }

            int fallback = ReadNumberNear(troopCenter, 100_000_000);
            Console.WriteLine($"[BB-MAINT] phase=star_laboratory_ocr status={(fallback >= 3000 ? "success" : "fail")} mode=resource_fallback troop=\"{troopName}\" value={fallback}");
            return fallback >= 3000 ? fallback : 0;
        }

        private int ReadStarLabTimeMinutes(Rect roi, string phase)
        {
            int value = ReadNumberFromCurrentScreen(roi, 999999);
            Console.WriteLine($"[BB-MAINT] phase=star_laboratory_ocr status={(value > 0 ? "success" : "fail")} mode=time phase_detail={phase} minutes={value}");
            return value;
        }

        private void SaveStarLabDebugScreenshot(BuilderBaseMaintenanceOptions options, string phase)
        {
            if (!options.StarLaboratoryDebugScreenshots) return;
            using Mat? screenshot = _adb.TakeScreenshot();
            if (screenshot == null || screenshot.Empty()) return;
            SaveStarLabDebugScreenshot(screenshot, phase);
        }

        private static void SaveStarLabDebugScreenshot(Mat screenshot, string phase)
        {
            try
            {
                string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SimpliMixi", "logs", "StarLabUpgrade");
                Directory.CreateDirectory(dir);
                string safePhase = string.Concat(phase.Select(ch => char.IsLetterOrDigit(ch) || ch == '_' || ch == '-' ? ch : '_'));
                string path = Path.Combine(dir, $"{safePhase}_{DateTime.UtcNow:yyyyMMdd_HHmmss_fff}.png");
                Cv2.ImWrite(path, screenshot);
                Console.WriteLine($"[BB-MAINT] phase=star_laboratory_debug status=saved path=\"{path}\"");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[BB-MAINT] phase=star_laboratory_debug status=fail reason=\"{ex.Message}\"");
            }
        }

        private static StarLabTroopState ReadStarLabTroopState(Mat screenshot, Point center)
        {
            if (PixelNear(screenshot, center.X + 47, center.Y + 1, 0xD3D3CB, 20)) return StarLabTroopState.NotUnlocked;
            if (PixelNear(screenshot, center.X + 22, center.Y + 60, 0xFFC360, 20)) return StarLabTroopState.MaxLevel;
            if (PixelNear(screenshot, center.X + 76, center.Y + 76, 0xFFFFFF, 20) || PixelNear(screenshot, center.X + 76, center.Y + 80, 0xFFFFFF, 20)) return StarLabTroopState.MaxLevel;
            if (PixelNear(screenshot, center.X + 3, center.Y + 19, 0xB7B7B7, 20) || PixelNear(screenshot, center.X + 93, center.Y + 20, 0x757575, 24)) return StarLabTroopState.LabUpgradeRequiredOrBusy;
            if (PixelNear(screenshot, center.X + 67, center.Y + 79, 0xFF7B72, 24) || PixelNear(screenshot, center.X + 67, center.Y + 82, 0xFF7B72, 24)) return StarLabTroopState.NotEnoughLoot;
            if (PixelNear(screenshot, center.X + 47, center.Y + 40, 0xD3D3CB, 28)) return StarLabTroopState.Unknown;
            return StarLabTroopState.Upgradeable;
        }

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

        private static bool PixelNear(Mat screenshot, int x, int y, int rgb, int tolerance)
        {
            if (x < 0 || y < 0 || x >= screenshot.Width || y >= screenshot.Height) return false;
            Vec3b pixel = screenshot.At<Vec3b>(y, x);
            int r = (rgb >> 16) & 0xFF;
            int g = (rgb >> 8) & 0xFF;
            int b = rgb & 0xFF;
            return Math.Abs(pixel.Item2 - r) <= tolerance
                && Math.Abs(pixel.Item1 - g) <= tolerance
                && Math.Abs(pixel.Item0 - b) <= tolerance;
        }

        private sealed record StarLabCandidate(string Key, string DisplayName, string Source, Point Center, int Cost, double Score, int Index, StarLabTroopState State);
        private sealed record StarLabState(int X, int Y, int Level, DateTime? UpgradeFinishUtc, DateTime? LastCheckedUtc);
        private sealed record StarLabTroopInfo(int Index, string Key, string DisplayName, Point DefaultCenter, string[] Aliases);
        private sealed record SuggestedUpgradeCandidate(string Resource, string Template, Point Center, double Score, int Cost);

        private enum StarLabTroopState
        {
            NotPresent,
            Unknown,
            Upgradeable,
            NotUnlocked,
            NotEnoughLoot,
            MaxLevel,
            LabUpgradeRequiredOrBusy
        }

        private bool TryUpgradeHero(string name, string[] templates, BuilderBaseReportSnapshot report, CancellationToken token)
        {
            if (report.FreeBuilders == 0 || report.Elixir <= 0) return false;

            bool isBattleCopter = name.Equals("battle_copter", StringComparison.OrdinalIgnoreCase);
            if (isBattleCopter)
            {
                if (!_navigator.SwitchToOttoVillage(token)) return false;
            }

            bool found = TapFirstExisting(templates, ButtonThreshold, HeroMapRoi, token, $"hero_{name}_open");
            bool upgraded = false;

            if (found)
            {
                Sleep(900, token);
                upgraded = TapFirstExisting(UpgradeActionTemplates, ButtonThreshold, ActionButtonRoi, token, $"hero_{name}_upgrade")
                    && TapFirstExisting(UpgradeConfirmElixir, ButtonThreshold, ActionButtonRoi, token, $"hero_{name}_confirm");
                SafeDismiss(token);
            }

            if (isBattleCopter)
            {
                _navigator.SwitchToBuilderBaseStage1(token);
            }

            Console.WriteLine($"[BB-MAINT] phase=hero_upgrade hero={name} status={(upgraded ? "success" : "skip")}");
            return upgraded;
        }

        

        private bool LocateAndOpenStarLaboratory(int villageIdx, CancellationToken token)
        {
            StarLabState state = LoadStarLabRuntime(villageIdx);
            Console.WriteLine($"[BB-MAINT] phase=star_laboratory_locate status=start village={villageIdx} cached=({state.X},{state.Y}) level={state.Level}");
            if (state.X > 0 && state.Y > 0)
            {
                _adb.Tap(state.X, state.Y);
                Sleep(650, token);
                if (ValidateStarLaboratoryPanel(villageIdx, state.X, state.Y, "stored")) return true;
                SaveStarLabRuntime(villageIdx, state with { X = -1, Y = -1, Level = 0 });
                SafeDismiss(token);
            }

            using Mat? slSs = _adb.TakeScreenshot();
            if (slSs == null || slSs.Empty()) return false;
            if (!TemplateSearch.TryFindFirst(slSs, FindElementWithExistenceCheck, StarLabTemplates, ButtonThreshold, LaboratoryRoi, out string matched, out double score, out Point center))
            {
                return false;
            }

            _adb.Tap(center.X, center.Y);
            Sleep(650, token);
            if (!ValidateStarLaboratoryPanel(villageIdx, center.X, center.Y, "detected"))
            {
                SafeDismiss(token);
                return false;
            }

            Console.WriteLine($"[BB-MAINT] phase=star_laboratory_locate status=success source=template template=\"{matched}\" score={score:F2} x={center.X} y={center.Y}");
            return true;
        }

        private bool ValidateStarLaboratoryPanel(int villageIdx, int x, int y, string source)
        {
            if (!TapFirstExisting(ResearchButtons, ButtonThreshold, ActionButtonRoi, CancellationToken.None, "star_laboratory_research_button"))
            {
                Console.WriteLine($"[BB-MAINT] phase=star_laboratory_locate status=fail source={source} reason=research_button_missing x={x} y={y}");
                return false;
            }

            int level = ReadNumberFromCurrentScreen(BuildingInfoLevelRoi, 20);
            StarLabState state = LoadStarLabRuntime(villageIdx) with { X = x, Y = y, Level = level, LastCheckedUtc = DateTime.UtcNow };
            SaveStarLabRuntime(villageIdx, state);
            Console.WriteLine($"[BB-MAINT] phase=star_laboratory_locate status=validated source={source} level_ocr={level} x={x} y={y}");
            return true;
        }

        private StarLabState LoadStarLabRuntime(int villageIdx)
        {
            try
            {
                string path = GetVillageProfilePath(villageIdx);
                if (!File.Exists(path)) return new StarLabState(-1, -1, 0, null, null);
                JsonNode? root = JsonNode.Parse(File.ReadAllText(path));
                JsonObject? star = root?["builder_base"]?["star_laboratory_state"] as JsonObject;
                if (star == null) return new StarLabState(-1, -1, 0, null, null);
                DateTime? finish = DateTime.TryParse((string?)star["upgrade_finish_utc"], out DateTime parsedFinish) ? parsedFinish.ToUniversalTime() : null;
                DateTime? checkedUtc = DateTime.TryParse((string?)star["last_checked_utc"], out DateTime parsedChecked) ? parsedChecked.ToUniversalTime() : null;
                _starLabUpgradeFinishUtc = finish;
                return new StarLabState((int?)star["x"] ?? -1, (int?)star["y"] ?? -1, (int?)star["level"] ?? 0, finish, checkedUtc);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[BB-MAINT] phase=star_laboratory_state status=fail action=load reason=\"{ex.Message}\"");
                return new StarLabState(-1, -1, 0, null, null);
            }
        }

        private void RecordStarLabFinish(int villageIdx, DateTime finishUtc, string reason)
        {
            _starLabUpgradeFinishUtc = finishUtc.ToUniversalTime();
            StarLabState state = LoadStarLabRuntime(villageIdx) with { UpgradeFinishUtc = _starLabUpgradeFinishUtc, LastCheckedUtc = DateTime.UtcNow };
            SaveStarLabRuntime(villageIdx, state);
            Console.WriteLine($"[BB-MAINT] phase=star_laboratory_state status=saved reason={reason} finish_utc=\"{_starLabUpgradeFinishUtc:O}\"");
        }

        private static string GetVillageProfilePath(int villageIdx)
        {
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SimpliMixi", "profiles", $"Village_{Math.Clamp(villageIdx, 1, 5)}.json");
        }

        private static void SaveStarLabRuntime(int villageIdx, StarLabState state)
        {
            string path = GetVillageProfilePath(villageIdx);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            JsonObject root = File.Exists(path) ? JsonNode.Parse(File.ReadAllText(path)) as JsonObject ?? new JsonObject() : new JsonObject();
            JsonObject builderBase = root["builder_base"] as JsonObject ?? new JsonObject();
            root["builder_base"] = builderBase;
            builderBase["star_laboratory_state"] = new JsonObject
            {
                ["x"] = state.X,
                ["y"] = state.Y,
                ["level"] = state.Level,
                ["upgrade_finish_utc"] = state.UpgradeFinishUtc?.ToUniversalTime().ToString("O") ?? string.Empty,
                ["last_checked_utc"] = state.LastCheckedUtc?.ToUniversalTime().ToString("O") ?? string.Empty
            };
            File.WriteAllText(path, root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
        }

        private static IEnumerable<StarLabTroopInfo> SortStarLabTroops(string configuredTroop)
        {
            string normalized = NormalizeStarLabTroopKey(configuredTroop);
            if (string.IsNullOrEmpty(normalized) || normalized == "auto" || normalized == "any") return StarLabTroops;
            return StarLabTroops.OrderBy(t => t.Key == normalized || t.Aliases.Contains(normalized, StringComparer.OrdinalIgnoreCase) ? 0 : 1).ThenBy(t => t.Index);
        }

        private static string[] BuildStarLaboratoryTroopTemplates(StarLabTroopInfo troop)
        {
            return troop.Aliases
                .Append(troop.Key)
                .SelectMany(alias => new[] { $@"troops\builder_base\{alias}_click", $@"builder_base\starlab\{alias}", $@"builder_base\star_laboratory\{alias}" })
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }

        private static string NormalizeStarLabTroopKey(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return "auto";
            return value.Trim().ToLowerInvariant().Replace(" ", "_").Replace("-", "_");
        }

        private string[] GetConfirmTemplates(BuilderBaseUpgradeTarget target, BuilderBaseReportSnapshot report)
        {
            bool canGold = target.AllowGold && report.Gold > 0;
            bool canElixir = target.AllowElixir && report.Elixir > 0;
            if (canGold && canElixir) return UpgradeConfirmGold.Concat(UpgradeConfirmElixir).ToArray();
            if (canGold) return UpgradeConfirmGold;
            if (canElixir) return UpgradeConfirmElixir;
            return Array.Empty<string>();
        }

        private int ReadNumberFromCurrentScreen(Rect roi, int maxPlausible)
        {
            using Mat? screenshot = _adb.TakeScreenshot();
            if (screenshot == null || screenshot.Empty()) return 0;
            Rect safe = ImageUtils.ClampRect(roi, screenshot.Width, screenshot.Height);
            if (safe.Width <= 0 || safe.Height <= 0) return 0;
            if (_vision.TryExtractNumericalMetrics(screenshot, safe, out int value, out _, useRgbThresh: true)
                || _vision.TryExtractNumericalMetrics(screenshot, safe, out value, out _))
            {
                return value >= 0 && value <= maxPlausible ? value : 0;
            }

            return 0;
        }

        private bool OpenBuilderMenu(CancellationToken token)
        {
            if (TapFirstExisting(BuilderHeadTemplates, ButtonThreshold, Rect.FromLTRB(600, 0, 900, 110), token, "open_builder_menu")) return !Sleep(900, token);
            _adb.Tap(738, 36);
            Sleep(900, token);
            return true;
        }

        private IEnumerable<string> EnumerateTemplateNames(params string[] subdirs)
        {
            foreach (string subdir in subdirs)
                foreach (string name in TemplateAssetLoader.EnumerateNames(_templatesPath, subdir))
                    yield return Path.Combine(subdir, name);
        }

        private Point? FindElementWithExistenceCheck(Mat screenshot, string template, double threshold, Rect? roi, out double score)
        {
            score = 0;
            if (!TemplateAssetLoader.Exists(_templatesPath, template)) return null;
            return _vision.FindElement(screenshot, template, threshold, roi, out score);
        }

        private bool TapFirstExisting(string[] templates, double threshold, Rect? roi, CancellationToken token, string phase)
        {
            using Mat? screenshot = _adb.TakeScreenshot();
            if (screenshot == null || screenshot.Empty()) return false;

            if (!TemplateSearch.TryFindFirst(screenshot, FindElementWithExistenceCheck, templates, threshold, roi, out string matched, out double score, out Point center))
                return false;

            Console.WriteLine($"[BB-MAINT] phase={phase} status=found template=\"{matched}\" score={score:F2} x={center.X} y={center.Y}");
            _adb.Tap(center.X, center.Y);
            return true;
        }
        private static bool IsGoldTemplate(string template) => template.IndexOf("gold", StringComparison.OrdinalIgnoreCase) >= 0;
        private static bool IsElixirTemplate(string template) => template.IndexOf("elixir", StringComparison.OrdinalIgnoreCase) >= 0;
        private void SafeDismiss(CancellationToken token) { if (!token.IsCancellationRequested) { _adb.Tap(140, 606); Sleep(350, token); } }
        private static bool Sleep(int milliseconds, CancellationToken token) => token.WaitHandle.WaitOne(milliseconds);
    }

    internal sealed record BuilderBaseMaintenanceResult(int SuggestedUpgrades, int ResearchStarted, int HeroUpgrades);

    internal sealed record BuilderBaseUpgradeTarget(string Name, string[] Templates, bool AllowGold, bool AllowElixir, bool IsHall = false, int RequiredLevel = 0, int[]? CostThousandsByLevel = null);

    
}
