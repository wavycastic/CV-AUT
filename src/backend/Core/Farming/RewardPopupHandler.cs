using System;
using System.Threading;
using OpenCvSharp;
using Point = OpenCvSharp.Point;

namespace CvAut
{
    /// <summary>
    /// Chịu trách nhiệm duy nhất: xử lý các popup phần thưởng (star bonus, rương treasure hunt).
    /// </summary>
    internal sealed class RewardPopupHandler
    {
        private static readonly Rect StarBonusPopupRoi = Rect.FromLTRB(430, 55, 1170, 145);
        private static readonly Point TreasureHuntOpenedChestTapPoint = new(800, 455);
        private static readonly Point TreasureHuntRewardContinueTapPoint = new(800, 750);
        private static readonly Point StarBonusOkayTapPoint = new(808, 766);
        private const double StarBonusPopupThreshold = 0.70;

        private readonly ADBHelper _adb;
        private readonly VisionEngine _vision;
        private readonly string _templatesPath;
        private readonly BattleResultDetector _battleResult;

        public RewardPopupHandler(ADBHelper adb, VisionEngine vision, string templatesPath, BattleResultDetector battleResult)
        {
            _adb = adb;
            _vision = vision;
            _templatesPath = templatesPath;
            _battleResult = battleResult;
        }

        public bool DismissStarBonusIfPresent()
        {
            using Mat? screenshot = _adb.TakeScreenshot();
            if (screenshot == null || screenshot.Empty()) return false;
            if (!TryFindStarBonusPopup(screenshot, out _, out double score)) return false;

            Console.WriteLine($"[FSM-CS] phase=reward_check status=success action=dismiss_popup score={score:F2}");
            _adb.Tap(StarBonusOkayTapPoint.X, StarBonusOkayTapPoint.Y);
            Thread.Sleep(1500);
            return true;
        }

        public bool TryFindStarBonusPopup(Mat screenshot, out Point center, out double score)
        {
            center = default; score = 0;
            bool hasUiTemplate = TemplateAssetLoader.Exists(_templatesPath, @"ui\star_bonus_received.png");
            bool hasRootTemplate = TemplateAssetLoader.Exists(_templatesPath, "star_bonus_received.png");
            if (!hasUiTemplate && !hasRootTemplate) return false;

            if (hasUiTemplate)
            {
                Point? found = _vision.FindElement(screenshot, @"ui\star_bonus_received.png", StarBonusPopupThreshold, StarBonusPopupRoi, out score);
                if (found.HasValue) { center = found.Value; return true; }
            }
            if (hasRootTemplate)
            {
                Point? found = _vision.FindElement(screenshot, "star_bonus_received.png", StarBonusPopupThreshold, StarBonusPopupRoi, out score);
                if (found.HasValue) { center = found.Value; return true; }
            }
            return false;
        }

        public bool HandleOpenedTreasureChest()
        {
            Console.WriteLine("[FSM-CS] phase=treasure_hunt status=pending action=handle_opened_chest");
            for (int i = 1; i <= 5; i++)
            {
                _adb.Tap(TreasureHuntOpenedChestTapPoint.X, TreasureHuntOpenedChestTapPoint.Y);
                Thread.Sleep(350);
            }
            Thread.Sleep(2000);
            if (!TapTreasureRewardContinue())
            {
                Console.WriteLine("[FSM-CS WARNING] phase=treasure_hunt status=pending action=continue reason=action_unavailable details=\"using_fallback\"");
                _adb.Tap(TreasureHuntRewardContinueTapPoint.X, TreasureHuntRewardContinueTapPoint.Y);
                Thread.Sleep(1500);
            }
            return true;
        }

        public bool TapTreasureRewardContinue()
        {
            DateTime start = DateTime.Now;
            while ((DateTime.Now - start).TotalSeconds < 10)
            {
                using Mat? screenshot = _adb.TakeScreenshot();
                if (screenshot != null && !screenshot.Empty() && _battleResult.TryFindContinueButton(screenshot, out Point continueCenter, out double score))
                {
                    Console.WriteLine("[FSM-CS] phase=treasure_hunt status=pending action=continue details=\"action_detected\"");
                    _adb.Tap(continueCenter.X, continueCenter.Y);
                    Thread.Sleep(1500);
                    return true;
                }
                Thread.Sleep(500);
            }
            return false;
        }
    }
}
