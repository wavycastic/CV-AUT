using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using OpenCvSharp;
using Point = OpenCvSharp.Point;

namespace CvAut
{
    internal sealed class SuggestedUpgradePlanner
    {
        private readonly IADBHelper _adb;
        private readonly BuilderBaseMaintenanceUi _ui;

        public SuggestedUpgradePlanner(IADBHelper adb, BuilderBaseMaintenanceUi ui)
        {
            _adb = adb;
            _ui = ui;
        }

        public int SuggestedUpgrades(BuilderBaseMaintenanceOptions options, BuilderBaseReportSnapshot report, CancellationToken token)
        {
            if (report.FreeBuilders == 0)
            {
                Console.WriteLine("[BB-MAINT] phase=suggested_upgrades status=skip reason=no_free_builder");
                return 0;
            }
            if (!_ui.OpenBuilderMenu(token)) return 0;
            Console.WriteLine($"[BB-MAINT] phase=suggested_upgrades status=start ignore_gold={options.IgnoreGoldUpgrades} ignore_elixir={options.IgnoreElixirUpgrades} ignore_hall={options.IgnoreHallUpgrades} ignore_wall={options.IgnoreWallUpgrades} place_new={options.PlaceNewBuildings}");

            int upgraded = 0;
            for (int i = 0; i < Math.Max(1, report.FreeBuilders) && !token.IsCancellationRequested; i++)
            {
                using Mat? screenshot = _adb.TakeScreenshot();
                if (screenshot == null || screenshot.Empty()) break;
                if (TemplateSearch.IsAnyVisible(screenshot, _ui.FindElementWithExistenceCheck, BuilderBaseMaintenanceLayout.NoResourceTemplates, BuilderBaseMaintenanceLayout.RowThreshold, BuilderBaseMaintenanceLayout.SuggestedRowsRoi)) break;

                SuggestedUpgradeCandidate? candidate = FindSuggestedUpgradeCandidate(screenshot, options, report);
                if (candidate == null) break;

                Console.WriteLine($"[BB-MAINT] phase=suggested_upgrades status=pending action=open_row resource={candidate.Resource} template=\"{candidate.Template}\" score={candidate.Score:F2} cost={candidate.Cost}");
                Point row = candidate.Center;
                _adb.Tap(row.X, row.Y);
                if (BuilderBaseMaintenanceUi.Sleep(1200, token)) break;
                if (!_ui.TapFirstExisting(BuilderBaseMaintenanceLayout.UpgradeActionTemplates, BuilderBaseMaintenanceLayout.ButtonThreshold, BuilderBaseMaintenanceLayout.ActionButtonRoi, token, "suggested_upgrade_action")) { _ui.SafeDismiss(token); break; }
                BuilderBaseMaintenanceUi.Sleep(900, token);
                string[] confirmTemplates = candidate.Resource == "gold" ? BuilderBaseMaintenanceLayout.UpgradeConfirmGold : candidate.Resource == "elixir" ? BuilderBaseMaintenanceLayout.UpgradeConfirmElixir : BuilderBaseMaintenanceLayout.UpgradeConfirmGold.Concat(BuilderBaseMaintenanceLayout.UpgradeConfirmElixir).ToArray();
                if (_ui.TapFirstExisting(confirmTemplates, BuilderBaseMaintenanceLayout.ButtonThreshold, BuilderBaseMaintenanceLayout.ActionButtonRoi, token, "suggested_upgrade_confirm")) upgraded++;
                BuilderBaseMaintenanceUi.Sleep(900, token);
                _ui.SafeDismiss(token);
                _ui.OpenBuilderMenu(token);
            }
            _ui.SafeDismiss(token);
            Console.WriteLine($"[BB-MAINT] phase=suggested_upgrades status=done upgraded={upgraded}");
            return upgraded;
        }

        private SuggestedUpgradeCandidate? FindSuggestedUpgradeCandidate(Mat screenshot, BuilderBaseMaintenanceOptions options, BuilderBaseReportSnapshot report)
        {
            bool canGold = !options.IgnoreGoldUpgrades && report.Gold > 0;
            bool canElixir = !options.IgnoreElixirUpgrades && report.Elixir > 0;
            var rowGroups = new List<(string Resource, string[] Templates)>
            {
                ("gold", canGold ? BuilderBaseMaintenanceLayout.UpgradeConfirmGold : Array.Empty<string>()),
                ("elixir", canElixir ? BuilderBaseMaintenanceLayout.UpgradeConfirmElixir : Array.Empty<string>())
            };

            if (options.PlaceNewBuildings)
            {
                rowGroups.Add(("new_building", BuilderBaseMaintenanceLayout.NewBuildingTemplates));
            }

            if (options.IgnoreHallUpgrades)
            {
                if (TemplateSearch.IsAnyVisible(screenshot, _ui.FindElementWithExistenceCheck, BuilderBaseMaintenanceLayout.BuilderHallTemplates, BuilderBaseMaintenanceLayout.RowThreshold, BuilderBaseMaintenanceLayout.SuggestedRowsRoi))
                {
                    Console.WriteLine("[BB-MAINT] phase=suggested_upgrades status=skip reason=builder_hall_ignored");
                    rowGroups.RemoveAll(g => g.Resource == "new_building");
                }
            }

            var candidates = new List<SuggestedUpgradeCandidate>();
            foreach ((string resource, string[] templates) in rowGroups)
            {
                if (templates.Length == 0) continue;
                Point? center = TemplateSearch.FindFirst(screenshot, _ui.FindElementWithExistenceCheck, templates, BuilderBaseMaintenanceLayout.RowThreshold, BuilderBaseMaintenanceLayout.SuggestedRowsRoi, out string template, out double score);
                if (center == null) continue;
                int cost = ReadSuggestedUpgradeCost(center.Value, resource);
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

        private int ReadSuggestedUpgradeCost(Point center, string resource)
        {
            Rect roi = resource == "gold"
                ? Rect.FromLTRB(center.X + 12, center.Y + 70, center.X + 190, center.Y + 118)
                : Rect.FromLTRB(center.X + 12, center.Y + 72, center.X + 190, center.Y + 122);
            int cost = _ui.ReadNumberFromCurrentScreen(roi, 100_000_000);
            Console.WriteLine($"[BB-MAINT] phase=suggested_upgrades_ocr status={(cost > 0 ? "success" : "fail")} resource={resource} value={cost} center=({center.X},{center.Y})");
            return cost;
        }

        public sealed record SuggestedUpgradeCandidate(string Resource, string Template, Point Center, double Score, int Cost);
    }
}
