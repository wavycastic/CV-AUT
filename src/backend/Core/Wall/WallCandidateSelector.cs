using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Point = OpenCvSharp.Point;

namespace CvAut
{
    /// <summary>
    /// Result of one candidate selection pass. SkipReason is null when Coord holds a candidate whose
    /// upgrade panel was validated.
    /// </summary>
    internal sealed record WallCandidateSelection(Point? Coord, int CandidateMatchCount, string? SkipReason);

    /// <summary>
    /// Picks a wall candidate and confirms that tapping it actually opens the upgrade panel.
    /// Owns the saved wall offset, which remembers which candidate worked last time so later cycles
    /// can skip straight to it.
    /// </summary>
    internal sealed class WallCandidateSelector
    {
        private readonly IADBHelper _adb;
        private readonly WallCandidateScanner _scanner;
        private readonly WallPanelInspector _inspector;
        private readonly WallMenuNavigator _navigator;
        private readonly WallDebugRecorder _debug;

        private int? _savedWallOffset;

        public WallCandidateSelector(IADBHelper adb, WallCandidateScanner scanner, WallPanelInspector inspector, WallMenuNavigator navigator, WallDebugRecorder debug)
        {
            _adb = adb;
            _scanner = scanner;
            _inspector = inspector;
            _navigator = navigator;
            _debug = debug;
        }

        public void ResetSavedOffset()
        {
            _savedWallOffset = null;
        }

        public WallCandidateSelection SelectValidatedCandidate(CancellationToken token) => SelectValidatedCandidate(token, "unknown", null, 0);

        public WallCandidateSelection SelectValidatedCandidate(CancellationToken token, string trigger = "unknown", string? runId = null, int cycle = 0)
        {
            int candidateMatchCount = 0;
            var triedCoords = new List<Point>();
            Point? validCoord = null;

            for (int attempt = 0; attempt < WallUiLayout.MaxCandidateAttempts; attempt++)
            {
                if (token.IsCancellationRequested)
                {
                    _navigator.BestEffortDismiss();
                    WallLogger.LogInfo("select_candidate", "cancelled", reason: "cancelled", cycle: cycle, trigger: trigger, runId: runId);
                    return new WallCandidateSelection(null, candidateMatchCount, "cancelled");
                }

                List<WallCandidate> candidates = _scanner.FindAllWallCandidates(token, trigger, runId, cycle)
                    .Where(candidate => !triedCoords.Any(tried => Math.Abs(candidate.Point.Y - tried.Y) <= 20))
                    .ToList();

                candidateMatchCount = Math.Max(candidateMatchCount, candidates.Count);
                if (candidates.Count == 0)
                {
                    Console.WriteLine($"[WALL RESULT] phase=attempt_upgrade cycle={_debug.Cycle} candidate_match_count={candidateMatchCount} verified_count=0 status=skip reason=no_candidates");
                    WallLogger.LogInfo("select_candidate", "skip", reason: "no_candidates", cycle: cycle, trigger: trigger, runId: runId, extra: $"attempt={attempt + 1} candidate_match_count={candidateMatchCount}");
                    _navigator.BestEffortDismiss();
                    return new WallCandidateSelection(null, candidateMatchCount, "no_candidates");
                }

                WallCandidate candidate;
                if (_savedWallOffset.HasValue && _savedWallOffset.Value >= -candidates.Count && _savedWallOffset.Value < candidates.Count)
                {
                    candidate = candidates[IndexFromEnd(candidates, _savedWallOffset.Value)];
                    WallLogger.LogInfo("candidate_offset_used", "ok", cycle: cycle, trigger: trigger, runId: runId, extra: $"saved_offset={_savedWallOffset.Value} candidate_x={candidate.Point.X} candidate_y={candidate.Point.Y}");
                }
                else
                {
                    candidate = candidates[candidates.Count - 1];
                }
                triedCoords.Add(candidate.Point);

                Console.WriteLine($"[WALL] phase=select_candidate cycle={_debug.Cycle} candidate_match_count={candidates.Count} attempt={attempt + 1} x={candidate.Point.X} y={candidate.Point.Y} conf={candidate.Confidence:F3} template=\"{candidate.TemplateName}\" status=start");
                WallLogger.LogInfo("select_candidate", "start", cycle: cycle, trigger: trigger, runId: runId, extra: $"attempt={attempt + 1} candidate_match_count={candidates.Count} selected_candidate_x={candidate.Point.X} selected_candidate_y={candidate.Point.Y} confidence={candidate.Confidence:F3} template_name=\"{candidate.TemplateName}\" saved_offset={_savedWallOffset}");

                WallLogger.LogInfo("tap_candidate", "ok", cycle: cycle, trigger: trigger, runId: runId, extra: $"tap_x={candidate.Point.X} tap_y={candidate.Point.Y}");
                _adb.Tap(candidate.Point.X, candidate.Point.Y);
                if (ThreadingUtil.InterruptibleSleep(1000, token)) return new WallCandidateSelection(null, candidateMatchCount, "cancelled");
                _debug.Capture("candidate_selected");

                // Close the builder panel so the upgrade panel underneath becomes visible
                WallLogger.LogInfo("tap_close_builder_menu", "ok", cycle: cycle, trigger: trigger, runId: runId, extra: $"tap_x={WallUiLayout.BuilderMenuPoint.X} tap_y={WallUiLayout.BuilderMenuPoint.Y}");
                _adb.Tap(WallUiLayout.BuilderMenuPoint.X, WallUiLayout.BuilderMenuPoint.Y);
                if (ThreadingUtil.InterruptibleSleep(500, token)) return new WallCandidateSelection(null, candidateMatchCount, "cancelled");

                bool panelOpen = _inspector.ValidateWallPanelOpen(out bool goldAvail, out bool elixirAvail, out bool whitePanel, trigger, runId, cycle);
                WallLogger.LogInfo("validate_panel", panelOpen ? "ok" : "fail", reason: panelOpen ? "panel_open" : "panel_not_open", cycle: cycle, trigger: trigger, runId: runId, extra: $"attempt={attempt + 1} panel_open={panelOpen} white_panel={whitePanel} gold_avail={goldAvail} elixir_avail={elixirAvail}");

                if (panelOpen)
                {
                    validCoord = candidate.Point;
                    _savedWallOffset ??= -1 - attempt;
                    WallLogger.LogInfo("select_candidate", "ok", reason: "panel_validated", cycle: cycle, trigger: trigger, runId: runId, extra: $"selected_candidate_x={candidate.Point.X} selected_candidate_y={candidate.Point.Y} saved_offset={_savedWallOffset}");
                    break;
                }

                WallLogger.LogInfo("tap_dismiss_panel", "ok", cycle: cycle, trigger: trigger, runId: runId, extra: $"tap_x={WallUiLayout.DismissPoint.X} tap_y={WallUiLayout.DismissPoint.Y}");
                _adb.Tap(WallUiLayout.DismissPoint.X, WallUiLayout.DismissPoint.Y);
                if (ThreadingUtil.InterruptibleSleep(500, token)) return new WallCandidateSelection(null, candidateMatchCount, "cancelled");
                _savedWallOffset = null;
            }

            if (!validCoord.HasValue)
            {
                Console.WriteLine($"[WALL RESULT] phase=attempt_upgrade cycle={_debug.Cycle} candidate_match_count={candidateMatchCount} verified_count=0 status=skip reason=unvalidated");
                WallLogger.LogInfo("select_candidate", "skip", reason: "unvalidated", cycle: cycle, trigger: trigger, runId: runId, extra: $"candidate_match_count={candidateMatchCount}");
                return new WallCandidateSelection(null, candidateMatchCount, "unvalidated");
            }

            return new WallCandidateSelection(validCoord, candidateMatchCount, null);
        }

        private static int IndexFromEnd<T>(IReadOnlyList<T> list, int negativeIndex)
        {
            return negativeIndex < 0 ? list.Count + negativeIndex : negativeIndex;
        }
    }
}
