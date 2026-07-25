using System;
using System.IO;
using System.Threading;
using OpenCvSharp;

namespace CvAut.Handlers
{
    /// <summary>
    /// Xử lý sự kiện rương báu (Treasure Hunt Event) tự động mở rương và đóng popup rương báu.
    /// </summary>
    internal class TreasureHuntHandler
    {
        private readonly IADBHelper _adb;
        private readonly IVisionEngine _vision;
        private readonly string _templatesPath;

        public TreasureHuntHandler(IADBHelper adb, IVisionEngine vision, string templatesPath)
        {
            _adb = adb;
            _vision = vision;
            _templatesPath = templatesPath;
        }

        public bool HandleIfPresent(bool verboseNotFound = true)
        {
            using Mat? screenshot = _adb.TakeScreenshot();
            if (screenshot == null) return false;
            return HandleIfPresent(screenshot, verboseNotFound);
        }

        public bool HandleIfPresent(Mat screenshot, bool verboseNotFound = true)
        {
            if (!TryFindTreasureHuntPopup(screenshot, out Point center, out double score))
            {
                if (verboseNotFound)
                {
                    Console.WriteLine("[TREASURE HUNT] phase=check status=not_found");
                }
                return false;
            }

            Console.WriteLine($"[TREASURE HUNT] phase=detected status=success score={score:F3} center=({center.X},{center.Y})");

            _adb.Tap(center.X, center.Y);
            Thread.Sleep(1200);

            _adb.Tap(AutomationRoiConstants.TreasureHuntOpenedChestTapPoint.X, AutomationRoiConstants.TreasureHuntOpenedChestTapPoint.Y);
            Thread.Sleep(1000);

            _adb.Tap(AutomationRoiConstants.TreasureHuntRewardContinueTapPoint.X, AutomationRoiConstants.TreasureHuntRewardContinueTapPoint.Y);
            Thread.Sleep(1000);

            _adb.Tap(200, 200);
            Thread.Sleep(500);

            Console.WriteLine("[TREASURE HUNT] phase=handled status=success");
            return true;
        }

        public bool TryFindTreasureHuntPopup(Mat screenshot, out Point center, out double score)
        {
            if (TryMatch(screenshot, @"ui\treasure_hunt.png", AutomationRoiConstants.TreasureHuntRoi, AutomationThresholds.TreasureHuntThreshold, out center, out score)
                || TryMatch(screenshot, @"event\treasure_hunt.png", AutomationRoiConstants.TreasureHuntRoi, AutomationThresholds.TreasureHuntThreshold, out center, out score))
            {
                return true;
            }

            if (TryMatch(screenshot, @"ui\treasure_chest.png", AutomationRoiConstants.TreasureHuntRoi, AutomationThresholds.TreasureHuntThreshold, out center, out score)
                && TryMatch(screenshot, @"ui\treasure_hunt_text.png", AutomationRoiConstants.TreasureHuntRoi, AutomationThresholds.TreasureHuntThreshold, out _, out _))
            {
                return true;
            }

            return false;
        }

        private bool TryMatch(Mat screenshot, string relativePath, Rect? roi, double threshold, out Point matchCenter, out double score)
        {
            matchCenter = default;
            score = 0;
            string fullPath = Path.Combine(_templatesPath, relativePath);
            if (!File.Exists(fullPath)) return false;

            return _vision.TryFindTemplate(screenshot, fullPath, roi, threshold, out matchCenter, out score);
        }
    }
}
