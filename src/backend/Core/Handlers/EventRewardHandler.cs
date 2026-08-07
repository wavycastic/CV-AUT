using System;
using System.IO;
using System.Threading;
using OpenCvSharp;

namespace CvAut.Handlers
{
    /// <summary>
    /// Xử lý chung cho các sự kiện nhận thưởng (Treasure Hunt, Clash of Cards, ...):
    /// phát hiện template sự kiện trên màn hình rồi tap để nhận thưởng.
    /// </summary>
    internal class EventRewardHandler
    {
        private readonly IADBHelper _adb;
        private readonly IVisionEngine _vision;
        private readonly string _templatesPath;

        public EventRewardHandler(IADBHelper adb, IVisionEngine vision, string templatesPath)
        {
            _adb = adb;
            _vision = vision;
            _templatesPath = templatesPath;
        }

        // ==================== Treasure Hunt Event ====================

        public bool HandleTreasureHuntIfPresent(bool verboseNotFound = true)
        {
            using Mat? screenshot = _adb.TakeScreenshot();
            if (screenshot == null) return false;
            return HandleTreasureHuntIfPresent(screenshot, verboseNotFound);
        }

        public bool HandleTreasureHuntIfPresent(Mat screenshot, bool verboseNotFound = true)
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

        // ==================== Claim Reward Event (Clash of Cards, ...) ====================

        public bool HandleClaimRewardFlow(Point claimMatchCenter, int continueTimeoutSeconds = 8)
        {
            Console.WriteLine($"[REWARD-CS] phase=claim_reward status=start center=({claimMatchCenter.X},{claimMatchCenter.Y})");

            _adb.Tap(claimMatchCenter.X, claimMatchCenter.Y);
            Console.WriteLine($"[REWARD-CS] phase=claim_reward status=pending action=tap_claim_match center=({claimMatchCenter.X},{claimMatchCenter.Y})");

            Thread.Sleep(1500);

            Point safePoint = AutomationRoiConstants.ClaimRewardSafeTapPoint;
            for (int i = 1; i <= 3; i++)
            {
                _adb.Tap(safePoint.X, safePoint.Y);
                Console.WriteLine($"[REWARD-CS] phase=claim_reward status=pending action=tap_open_reward tap_index={i} point=({safePoint.X},{safePoint.Y})");
                Thread.Sleep(400);
            }

            Thread.Sleep(1200);
            _adb.Tap(safePoint.X, safePoint.Y);
            Console.WriteLine($"[REWARD-CS] phase=claim_reward status=pending action=tap_safe_roi point=({safePoint.X},{safePoint.Y})");
            Thread.Sleep(1500);

            DateTime startWait = DateTime.Now;
            bool continueFound = false;

            while ((DateTime.Now - startWait).TotalSeconds < continueTimeoutSeconds)
            {
                using Mat? screenshot = _adb.TakeScreenshot();
                if (screenshot != null && !screenshot.Empty())
                {
                    Point? continueMatch = _vision.FindElement(
                        screenshot,
                        @"ui\continue_reward.png",
                        AutomationThresholds.ResultContinueThreshold,
                        AutomationRoiConstants.ResultContinueRoi,
                        out double continueScore);

                    if (!continueMatch.HasValue)
                    {
                        continueMatch = _vision.FindElement(
                            screenshot,
                            @"ui\continue_reward.png",
                            AutomationThresholds.ResultContinueThreshold,
                            null,
                            out continueScore);
                    }

                    if (continueMatch.HasValue)
                    {
                        Console.WriteLine($"[REWARD-CS] phase=claim_reward status=success action=continue_reward score={continueScore:F2} center=({continueMatch.Value.X},{continueMatch.Value.Y})");
                        _adb.Tap(continueMatch.Value.X, continueMatch.Value.Y);
                        Thread.Sleep(1500);
                        continueFound = true;
                        break;
                    }
                }
                Thread.Sleep(500);
            }

            if (!continueFound)
            {
                Console.WriteLine("[REWARD-CS WARNING] phase=claim_reward status=pending action=fail_safe reason=continue_reward_timeout");
                _adb.Tap(safePoint.X, safePoint.Y);
                Thread.Sleep(1500);
            }

            return true;
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
