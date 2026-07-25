using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using OpenCvSharp;
using Point = OpenCvSharp.Point;

namespace CvAut
{
    internal sealed class StarLaboratoryService
    {
        private readonly IADBHelper _adb;
        private readonly IVisionEngine _vision;
        private readonly BuilderBaseMaintenanceUi _ui;
        private readonly StarLaboratoryStateStore _stateStore;

        public StarLaboratoryService(IADBHelper adb, IVisionEngine vision, BuilderBaseMaintenanceUi ui, StarLaboratoryStateStore stateStore)
        {
            _adb = adb;
            _vision = vision;
            _ui = ui;
            _stateStore = stateStore;
        }

        public DateTime? StarLabUpgradeFinishUtc => _stateStore.StarLabUpgradeFinishUtc;

        public bool TryStartStarLaboratoryResearch(BuilderBaseMaintenanceOptions options, BuilderBaseReportSnapshot report, CancellationToken token)
        {
            _stateStore.LoadStarLabRuntime(options.VillageIdx);
            Console.WriteLine($"[BB-MAINT] phase=star_laboratory status=start troop_mode=\"{options.StarLaboratoryTroop}\" village={options.VillageIdx} elixir={report.Elixir}");
            if (_stateStore.StarLabUpgradeFinishUtc is DateTime finishUtc && finishUtc > DateTime.UtcNow)
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
            BuilderBaseMaintenanceUi.Sleep(1000, token);

            using Mat? screenshot = _adb.TakeScreenshot();
            if (screenshot == null || screenshot.Empty())
            {
                Console.WriteLine("[BB-MAINT] phase=star_laboratory status=skip reason=screenshot_failed");
                return false;
            }

            if (TemplateSearch.IsAnyVisible(screenshot, _ui.FindElementWithExistenceCheck, BuilderBaseMaintenanceLayout.ResearchBusyTemplates, BuilderBaseMaintenanceLayout.RowThreshold, BuilderBaseMaintenanceLayout.ResearchTimerRoi))
            {
                StarLaboratoryTroopStateReader.SaveStarLabDebugScreenshot(options, _adb, "busy_timer");
                int timer = StarLaboratoryTroopStateReader.ReadStarLabTimeMinutes(BuilderBaseMaintenanceLayout.ResearchTimerRoi, "busy_timer", _ui);
                if (timer > 0) _stateStore.RecordStarLabFinish(options.VillageIdx, DateTime.UtcNow.AddMinutes(timer), "busy_timer");
                Console.WriteLine($"[BB-MAINT] phase=star_laboratory status=skip reason=research_busy timer_ocr={timer}");
                _ui.SafeDismiss(token);
                return false;
            }

            if (TemplateSearch.IsAnyVisible(screenshot, _ui.FindElementWithExistenceCheck, BuilderBaseMaintenanceLayout.ResearchMaxTemplates, BuilderBaseMaintenanceLayout.RowThreshold, BuilderBaseMaintenanceLayout.ResearchRowsRoi))
            {
                Console.WriteLine("[BB-MAINT] phase=star_laboratory status=skip reason=max_or_unavailable");
                _ui.SafeDismiss(token);
                return false;
            }

            StarLabCandidate[] candidates = FindStarLabCandidates(screenshot, options.StarLaboratoryTroop);
            StarLabCandidate? selectedCandidate = SelectStarLabCandidate(candidates, options.StarLaboratoryTroop, report.Elixir);
            if (selectedCandidate == null)
            {
                Console.WriteLine($"[BB-MAINT] phase=star_laboratory status=skip reason=no_affordable_candidate candidates={candidates.Length} elixir={report.Elixir}");
                _ui.SafeDismiss(token);
                return false;
            }

            Console.WriteLine($"[BB-MAINT] phase=star_laboratory status=pending action=select_candidate troop=\"{selectedCandidate.DisplayName}\" cost={selectedCandidate.Cost} state={selectedCandidate.State} score={selectedCandidate.Score:F2}");
            _adb.Tap(selectedCandidate.Center.X, selectedCandidate.Center.Y);
            if (BuilderBaseMaintenanceUi.Sleep(700, token)) return false;

            if (IsStarLabTroopBlocked(selectedCandidate, out string blockedReason))
            {
                Console.WriteLine($"[BB-MAINT] phase=star_laboratory status=skip troop=\"{selectedCandidate.DisplayName}\" reason={blockedReason}");
                _ui.SafeDismiss(token);
                return false;
            }

            int cost = selectedCandidate.Cost > 0 ? selectedCandidate.Cost : _ui.ReadNumberFromCurrentScreen(BuilderBaseMaintenanceLayout.ResearchCostRoi, 100_000_000);
            if (cost > 0 && cost > report.Elixir)
            {
                Console.WriteLine($"[BB-MAINT] phase=star_laboratory status=skip reason=not_enough_elixir cost={cost} elixir={report.Elixir}");
                _ui.SafeDismiss(token);
                return false;
            }

            bool started = _ui.TapFirstExisting(BuilderBaseMaintenanceLayout.ResearchButtons, BuilderBaseMaintenanceLayout.ButtonThreshold, BuilderBaseMaintenanceLayout.ActionButtonRoi, token, "star_laboratory_research");
            if (started)
            {
                BuilderBaseMaintenanceUi.Sleep(700, token);
                StarLaboratoryTroopStateReader.SaveStarLabDebugScreenshot(options, _adb, "confirm_time");
                int minutes = StarLaboratoryTroopStateReader.ReadStarLabTimeMinutes(BuilderBaseMaintenanceLayout.ResearchConfirmTimeRoi, "confirm_time", _ui);
                if (minutes > 0)
                {
                    _stateStore.RecordStarLabFinish(options.VillageIdx, DateTime.UtcNow.AddMinutes(minutes), "confirm_time");
                    Console.WriteLine($"[BB-MAINT] phase=star_laboratory status=pending action=record_finish minutes={minutes} finish_utc=\"{_stateStore.StarLabUpgradeFinishUtc:O}\"");
                }
            }
            BuilderBaseMaintenanceUi.Sleep(900, token);
            _ui.SafeDismiss(token);
            Console.WriteLine($"[BB-MAINT] phase=star_laboratory status={(started ? "success" : "skip")} troop=\"{selectedCandidate.DisplayName}\" cost={cost} troop_mode=\"{options.StarLaboratoryTroop}\"");
            return started;
        }

        private StarLabCandidate[] FindStarLabCandidates(Mat screenshot, string configuredTroop)
        {
            var candidates = new List<StarLabCandidate>();
            foreach (StarLabTroopInfo troop in StarLaboratoryTroopCatalog.SortStarLabTroops(configuredTroop))
            {
                Point? center = FindStarLabTroopCenter(screenshot, troop, out string source, out double score);
                if (center == null || BuilderBaseMaintenanceUi.IsNearExisting(candidates.Select(c => c.Center), center.Value)) continue;
                StarLabTroopState state = StarLaboratoryTroopStateReader.ReadStarLabTroopState(screenshot, center.Value);
                int cost = StarLaboratoryTroopStateReader.ReadStarLabResourceCost(center.Value, troop.DisplayName, _ui);
                candidates.Add(new StarLabCandidate(troop.Key, troop.DisplayName, source, center.Value, cost, score, troop.Index, state));
                Console.WriteLine($"[BB-MAINT] phase=star_laboratory status=found troop=\"{troop.DisplayName}\" key={troop.Key} source=\"{source}\" cost={cost} state={state} score={score:F2} center=({center.Value.X},{center.Value.Y})");
            }

            return candidates.ToArray();
        }

        private Point? FindStarLabTroopCenter(Mat screenshot, StarLabTroopInfo troop, out string source, out double score)
        {
            source = "grid";
            score = 0;
            foreach (string template in StarLaboratoryTroopCatalog.BuildStarLaboratoryTroopTemplates(troop))
            {
                Point? center = _vision.FindElement(screenshot, template, BuilderBaseMaintenanceLayout.RowThreshold, BuilderBaseMaintenanceLayout.ResearchRowsRoi, out score);
                if (center != null)
                {
                    source = template;
                    return center;
                }
            }

            Point grid = troop.DefaultCenter;
            if (grid.X >= BuilderBaseMaintenanceLayout.ResearchRowsRoi.Left && grid.X <= BuilderBaseMaintenanceLayout.ResearchRowsRoi.Right && grid.Y >= BuilderBaseMaintenanceLayout.ResearchRowsRoi.Top && grid.Y <= BuilderBaseMaintenanceLayout.ResearchRowsRoi.Bottom)
            {
                StarLabTroopState state = StarLaboratoryTroopStateReader.ReadStarLabTroopState(screenshot, grid);
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
                StarLabCandidate? exact = affordable.FirstOrDefault(c => c.Key.Equals(StarLaboratoryTroopCatalog.NormalizeStarLabTroopKey(configuredTroop), StringComparison.OrdinalIgnoreCase)
                    || c.DisplayName.Equals(configuredTroop, StringComparison.OrdinalIgnoreCase));
                if (exact != null) return exact;
            }

            return affordable.OrderBy(c => c.Cost <= 0 ? int.MaxValue : c.Cost).FirstOrDefault();
        }

        private bool IsStarLabTroopBlocked(StarLabCandidate candidate, out string reason)
        {
            using Mat? screenshot = _adb.TakeScreenshot();
            if (screenshot == null || screenshot.Empty()) { reason = "screenshot_failed"; return true; }

            StarLabTroopState state = StarLaboratoryTroopStateReader.ReadStarLabTroopState(screenshot, candidate.Center);
            if (state != StarLabTroopState.Upgradeable && state != StarLabTroopState.Unknown)
            {
                reason = state.ToString().ToLowerInvariant();
                return true;
            }

            reason = string.Empty;
            return false;
        }

        private bool LocateAndOpenStarLaboratory(int villageIdx, CancellationToken token)
        {
            StarLabState state = _stateStore.LoadStarLabRuntime(villageIdx);
            Console.WriteLine($"[BB-MAINT] phase=star_laboratory_locate status=start village={villageIdx} cached=({state.X},{state.Y}) level={state.Level}");
            if (state.X > 0 && state.Y > 0)
            {
                _adb.Tap(state.X, state.Y);
                BuilderBaseMaintenanceUi.Sleep(650, token);
                if (ValidateStarLaboratoryPanel(villageIdx, state.X, state.Y, "stored")) return true;
                StarLaboratoryStateStore.SaveStarLabRuntime(villageIdx, state with { X = -1, Y = -1, Level = 0 });
                _ui.SafeDismiss(token);
            }

            using Mat? slSs = _adb.TakeScreenshot();
            if (slSs == null || slSs.Empty()) return false;
            if (!TemplateSearch.TryFindFirst(slSs, _ui.FindElementWithExistenceCheck, BuilderBaseMaintenanceLayout.StarLabTemplates, BuilderBaseMaintenanceLayout.ButtonThreshold, BuilderBaseMaintenanceLayout.LaboratoryRoi, out string matched, out double score, out Point center))
            {
                return false;
            }

            _adb.Tap(center.X, center.Y);
            BuilderBaseMaintenanceUi.Sleep(650, token);
            if (!ValidateStarLaboratoryPanel(villageIdx, center.X, center.Y, "detected"))
            {
                _ui.SafeDismiss(token);
                return false;
            }

            Console.WriteLine($"[BB-MAINT] phase=star_laboratory_locate status=success source=template template=\"{matched}\" score={score:F2} x={center.X} y={center.Y}");
            return true;
        }

        private bool ValidateStarLaboratoryPanel(int villageIdx, int x, int y, string source)
        {
            if (!_ui.TapFirstExisting(BuilderBaseMaintenanceLayout.ResearchButtons, BuilderBaseMaintenanceLayout.ButtonThreshold, BuilderBaseMaintenanceLayout.ActionButtonRoi, CancellationToken.None, "star_laboratory_research_button"))
            {
                Console.WriteLine($"[BB-MAINT] phase=star_laboratory_locate status=fail source={source} reason=research_button_missing x={x} y={y}");
                return false;
            }

            int level = _ui.ReadNumberFromCurrentScreen(BuilderBaseMaintenanceLayout.BuildingInfoLevelRoi, 20);
            StarLabState state = _stateStore.LoadStarLabRuntime(villageIdx) with { X = x, Y = y, Level = level, LastCheckedUtc = DateTime.UtcNow };
            StarLaboratoryStateStore.SaveStarLabRuntime(villageIdx, state);
            Console.WriteLine($"[BB-MAINT] phase=star_laboratory_locate status=validated source={source} level_ocr={level} x={x} y={y}");
            return true;
        }

        private sealed record StarLabCandidate(string Key, string DisplayName, string Source, Point Center, int Cost, double Score, int Index, StarLabTroopState State);
    }
}
