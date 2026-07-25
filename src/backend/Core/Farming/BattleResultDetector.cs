using System;
using System.Threading;
using OpenCvSharp;
using Point = OpenCvSharp.Point;

namespace CvAut
{
    /// <summary>
    /// Single responsibility: detect that a battle has ended and wait for the result screen.
    /// </summary>
    internal sealed class BattleResultDetector
    {
        private readonly ADBHelper _adb;
        private readonly VisionEngine _vision;
        private readonly Func<bool> _checkStop;

        public BattleResultDetector(ADBHelper adb, VisionEngine vision, Func<bool> checkStop)
        {
            _adb = adb;
            _vision = vision;
            _checkStop = checkStop;
        }

        /// <summary>
        /// Polls until the result screen is stably visible, or gives up once the timeout elapses.
        /// </summary>
        public bool WaitBattleEnd(CancellationToken token, SmartSurrenderConfig? surrenderConfig = null)
        {
            Console.WriteLine("[FSM-CS] phase=battle_wait status=start");
            DateTime start = DateTime.Now;
            int stableResultMatches = 0;
            bool waitingLogged = false;
            bool resultDetectedLogged = false;
            while (!_checkStop())
            {
                if (_checkStop()) return false;

                if (BattleEnded(out string resultMatchInfo))
                {
                    stableResultMatches++;
                    if (!resultDetectedLogged)
                    {
                        Console.WriteLine("[FSM-CS] phase=battle_wait status=pending details=\"result_screen_detected\"");
                        resultDetectedLogged = true;
                    }
                    if (stableResultMatches >= AutomationThresholds.ResultScreenStableMatches)
                    {
                        Console.WriteLine("[FSM-CS] phase=battle_wait status=success");
                        Thread.Sleep(1000);
                        return true;
                    }
                }
                else
                {
                    stableResultMatches = 0;
                    if (!waitingLogged)
                    {
                        Console.WriteLine("[FSM-CS] phase=battle_wait status=pending details=\"waiting\"");
                        waitingLogged = true;
                    }
                }

                if ((DateTime.Now - start).TotalSeconds >= AutomationThresholds.MaxWaitBattleSeconds)
                {
                    Console.WriteLine("[FSM-CS WARNING] phase=battle_wait status=fail reason=timeout");
                    return false;
                }

                Thread.Sleep(1000);
            }
            return false;
        }

        /// <summary>
        /// Inspects a single frame to decide whether the battle is over (continue button or result marker).
        /// </summary>
        public bool BattleEnded(out string matchInfo)
        {
            matchInfo = "continue score=0.00, result-marker score=0.00";
            using Mat? screenshot = _adb.TakeScreenshot();
            if (screenshot == null || screenshot.Empty()) return false;

            if (IsActiveBattlePresent(screenshot, out double endBattleScore))
            {
                matchInfo = $"active_battle end_battle_score={endBattleScore:F2}";
                return false;
            }

            bool hasContinue = TryFindContinueButton(screenshot, out Point center, out double continueScore);
            bool hasResultMarker = _vision.FindElement(screenshot, @"ui\resources_gained.png", AutomationThresholds.ResultYouGotThreshold, AutomationRoiConstants.ResultYouGotRoi, out double markerScore) != null;

            matchInfo = $"continue score={continueScore:F2} center=({center.X},{center.Y}), result-marker score={markerScore:F2}";
            return hasContinue || hasResultMarker;
        }

        public bool IsActiveBattlePresent(Mat screenshot, out double endBattleScore)
        {
            return BattleScreenDetector.IsActiveBattlePresent(_vision, screenshot, out endBattleScore);
        }

        public bool TryFindContinueButton(Mat screenshot, out Point center, out double score)
        {
            Point? found = _vision.FindElement(screenshot, @"ui\return_home.png", AutomationThresholds.ResultContinueThreshold, AutomationRoiConstants.ResultContinueRoi, out score);
            if (found.HasValue) { center = found.Value; return true; }
            found = _vision.FindElement(screenshot, @"ui\return_home_n.png", AutomationThresholds.ResultContinueThreshold, AutomationRoiConstants.ResultContinueRoi, out score);
            if (found.HasValue) { center = found.Value; return true; }
            center = default;
            return false;
        }
    }
}
