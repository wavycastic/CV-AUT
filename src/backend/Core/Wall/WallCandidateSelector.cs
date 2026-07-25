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

        public WallCandidateSelection SelectValidatedCandidate(CancellationToken token)
        {
            int candidateMatchCount = 0;
            var triedCoords = new List<Point>();
            Point? validCoord = null;

            for (int attempt = 0; attempt < WallUiLayout.MaxCandidateAttempts; attempt++)
            {
                if (token.IsCancellationRequested)
                {
                    _navigator.BestEffortDismiss();
                    return new WallCandidateSelection(null, candidateMatchCount, "cancelled");
                }

                List<WallCandidate> candidates = _scanner.FindAllWallCandidates(token)
                    .Where(candidate => !triedCoords.Any(tried => Math.Abs(candidate.Point.Y - tried.Y) <= 20))
                    .ToList();

                candidateMatchCount = Math.Max(candidateMatchCount, candidates.Count);
                if (candidates.Count == 0)
                {
                    Console.WriteLine($"[WALL RESULT] phase=attempt_upgrade cycle={_debug.Cycle} candidate_match_count={candidateMatchCount} verified_count=0 status=skip reason=no_candidates");
                    _navigator.BestEffortDismiss();
                    return new WallCandidateSelection(null, candidateMatchCount, "no_candidates");
                }

                WallCandidate candidate;
                if (_savedWallOffset.HasValue && _savedWallOffset.Value >= -candidates.Count && _savedWallOffset.Value < candidates.Count)
                {
                    candidate = candidates[IndexFromEnd(candidates, _savedWallOffset.Value)];
                }
                else
                {
                    candidate = candidates[candidates.Count - 1];
                }
                triedCoords.Add(candidate.Point);

                Console.WriteLine($"[WALL] phase=select_candidate cycle={_debug.Cycle} candidate_match_count={candidates.Count} attempt={attempt + 1} x={candidate.Point.X} y={candidate.Point.Y} conf={candidate.Confidence:F3} template=\"{candidate.TemplateName}\" status=start");
                _adb.Tap(candidate.Point.X, candidate.Point.Y);
                if (ThreadingUtil.InterruptibleSleep(1000, token)) return new WallCandidateSelection(null, candidateMatchCount, "cancelled");
                _debug.Capture("candidate_selected");

                // Close the builder panel so the upgrade panel underneath becomes visible
                _adb.Tap(WallUiLayout.BuilderMenuPoint.X, WallUiLayout.BuilderMenuPoint.Y);
                if (ThreadingUtil.InterruptibleSleep(500, token)) return new WallCandidateSelection(null, candidateMatchCount, "cancelled");

                if (_inspector.ValidateWallPanelOpen(out _, out _))
                {
                    validCoord = candidate.Point;
                    _savedWallOffset ??= -1 - attempt;
                    break;
                }

                _adb.Tap(WallUiLayout.DismissPoint.X, WallUiLayout.DismissPoint.Y);
                if (ThreadingUtil.InterruptibleSleep(500, token)) return new WallCandidateSelection(null, candidateMatchCount, "cancelled");
                _savedWallOffset = null;
            }

            if (!validCoord.HasValue)
            {
                Console.WriteLine($"[WALL RESULT] phase=attempt_upgrade cycle={_debug.Cycle} candidate_match_count={candidateMatchCount} verified_count=0 status=skip reason=unvalidated");
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
