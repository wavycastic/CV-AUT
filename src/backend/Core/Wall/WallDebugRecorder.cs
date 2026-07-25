using System;
using System.IO;
using System.Linq;
using OpenCvSharp;

namespace CvAut
{
    /// <summary>
    /// Saves debug screenshots and tracks per-session counters for the wall upgrade flow.
    /// </summary>
    internal sealed class WallDebugRecorder
    {
        private readonly IADBHelper _adb;
        private readonly string _debugDirectory;
        private bool _enabled;
        private int _cycle;
        private int _sessionWallAttempted;
        private int _sessionWallVerified;
        private int _sessionWallSkipped;
        private int _sessionWallUnknown;

        public WallDebugRecorder(IADBHelper adb)
        {
            _adb = adb;
            _debugDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SimpliMixi", "debug", "wall");
        }

        /// <summary>The current cycle, used in every WallUpdater log line.</summary>
        public int Cycle => _cycle;

        public void Configure(bool debugScreenshotsEnabled, int cycle)
        {
            _enabled = debugScreenshotsEnabled;
            _cycle = cycle;
        }

        public void RecordVerified(int verifiedCount)
        {
            _sessionWallVerified += verifiedCount;
            _sessionWallAttempted += verifiedCount;
        }

        public void RecordUnknown()
        {
            _sessionWallUnknown++;
            _sessionWallAttempted++;
        }

        public void RecordSkipped()
        {
            _sessionWallSkipped++;
            _sessionWallAttempted++;
        }

        public void Capture(string phase)
        {
            if (!_enabled) return;
            using Mat? screenshot = _adb.TakeScreenshot();
            if (screenshot == null || screenshot.Empty()) return;
            Capture(screenshot, phase);
        }

        public void Capture(Mat screenshot, string phase)
        {
            if (!_enabled || screenshot.Empty()) return;
            try
            {
                Directory.CreateDirectory(_debugDirectory);
                string safePhase = string.Concat(phase.Select(ch => char.IsLetterOrDigit(ch) || ch == '_' || ch == '-' ? ch : '_'));
                string fileName = $"wall_cycle_{_cycle:D6}_{DateTimeOffset.UtcNow:yyyyMMdd_HHmmssfff}_{safePhase}.png";
                Cv2.ImWrite(Path.Combine(_debugDirectory, fileName), screenshot);
                Console.WriteLine($"[WALL DEBUG] phase={safePhase} cycle={_cycle} status=saved file=\"{fileName}\"");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[WALL DEBUG] phase={phase} cycle={_cycle} status=fail reason=\"{ex.Message}\"");
            }
        }

        public void LogSessionCounters(string phase, string resource, int cost, int candidateMatchCount, int requestedCount, int verifiedCount, string reason)
        {
            Console.WriteLine($"[WALL SESSION] phase={phase} cycle={_cycle} resource={resource} cost={cost:N0} candidate_match_count={candidateMatchCount} requested_count={requestedCount} verified_count={verifiedCount} reason={reason} wall_attempted={_sessionWallAttempted} wall_verified={_sessionWallVerified} wall_skipped={_sessionWallSkipped} wall_unknown={_sessionWallUnknown}");
        }
    }
}
