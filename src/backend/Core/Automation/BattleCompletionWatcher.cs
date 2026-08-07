using System;
using System.Threading;
using OpenCvSharp;

namespace CvAut.Automation;

internal delegate bool DetectHomeBaseDelegate(out string reason);
internal delegate bool EnsureHomeBaseDelegate(int maxWaitSeconds);
internal delegate bool ShouldSmartSurrenderDelegate(DateTime battleStart, SmartSurrenderConfig surrenderConfig, out string reason);

internal sealed class BattleCompletionWatcher
{
    private readonly IADBHelper _adb;
    private readonly IVisionEngine _vision;
    private readonly PopupHandlerService _popups;

    public BattleCompletionWatcher(IADBHelper adb, IVisionEngine vision, PopupHandlerService popups)
    {
        _adb = adb;
        _vision = vision;
        _popups = popups;
    }

    public bool WaitBattleEnd(
        Func<CancellationToken, bool> checkStopFunc,
        Action waitIfPausedFunc,
        Action bootRecoveryFunc,
        ShouldSmartSurrenderDelegate shouldSmartSurrenderFunc,
        Action<string, CancellationToken> executeSurrenderAction,
        CancellationToken token,
        SmartSurrenderConfig? surrenderConfig = null)
    {
        Console.WriteLine("[FSM-CS] phase=battle_wait status=start");

        DateTime start = DateTime.Now;
        int stableResultMatches = 0;
        bool waitingLogged = false;
        bool resultDetectedLogged = false;
        bool smartSurrenderExecuted = false;
        while (!checkStopFunc(token))
        {
            waitIfPausedFunc();
            if (checkStopFunc(token)) return false;

            if (BattleEnded(out _))
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

            if (_popups.ConnectionPopupVisible(out string matchInfo, allowDialogShapeFallback: false))
            {
                Console.WriteLine($"[FSM-CS WARNING] phase=battle_wait status=fail reason=connection_lost details=\"{matchInfo}\"");
                bootRecoveryFunc();
                return false;
            }

            if (surrenderConfig?.Enabled == true && !smartSurrenderExecuted && !resultDetectedLogged && shouldSmartSurrenderFunc(start, surrenderConfig, out string surrenderReason))
            {
                Console.WriteLine($"[ATTACK-CS] phase=surrender status=start reason={surrenderReason}");
                smartSurrenderExecuted = true;
                executeSurrenderAction("smart_" + surrenderReason, token);
                continue;
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

    public bool BattleEnded(out string matchInfo)
    {
        matchInfo = "continue score=0.00, result-marker score=0.00";

        using Mat? screenshot = _adb.TakeScreenshot();
        if (screenshot == null || screenshot.Empty())
        {
            return false;
        }

        if (IsActiveBattlePresent(screenshot, out double endBattleScore))
        {
            matchInfo = $"active_battle end_battle_score={endBattleScore:F2}";
            return false;
        }

        bool hasContinue = TryFindContinueButton(screenshot, out Point center, out double continueScore, out string matchedTemplate);
        bool hasResultMarker = _vision.FindElement(screenshot, @"ui\resources_gained.png", AutomationThresholds.ResultYouGotThreshold, AutomationRoiConstants.ResultYouGotRoi, out double markerScore) != null;

        matchInfo = $"continue score={continueScore:F2} template={matchedTemplate} center=({center.X},{center.Y}), result-marker score={markerScore:F2}";

        return hasContinue || hasResultMarker;
    }

    public bool IsActiveBattlePresent(Mat screenshot, out double endBattleScore)
    {
        return BattleScreenDetector.IsActiveBattlePresent(_vision, screenshot, out endBattleScore);
    }

    public bool TryFindContinueButton(Mat screenshot, out Point center, out double score, out string matchedTemplate)
    {
        return BattleScreenDetector.TryFindContinueButton(_vision, screenshot, out center, out score, out matchedTemplate);
    }

    public bool TryFindContinueButton(Mat screenshot, out Point center, out double score)
    {
        return BattleScreenDetector.TryFindContinueButton(_vision, screenshot, out center, out score, out _);
    }

    public bool ReturnHome(DetectHomeBaseDelegate detectHomeBaseFunc, EnsureHomeBaseDelegate ensureHomeBaseFunc)
    {
        Console.WriteLine("[FSM-CS] phase=return_home status=start");

        const int maxReturnAttempts = 3;
        for (int attempt = 1; attempt <= maxReturnAttempts; attempt++)
        {
            Console.WriteLine($"[FSM-CS] phase=return_home status=pending attempt={attempt}");

            using Mat? screenshot = _adb.TakeScreenshot();
            if (screenshot != null && !screenshot.Empty() && TryFindContinueButton(screenshot, out Point continueCenter, out double score, out string matchedTemplate))
            {
                Console.WriteLine($"[FSM-CS] phase=return_home status=pending action=continue score={score:F2} template={matchedTemplate} center=({continueCenter.X},{continueCenter.Y}) attempt={attempt}");
                if (!string.IsNullOrEmpty(matchedTemplate) && matchedTemplate.Contains("claim_reward"))
                {
                    _popups.HandleClaimRewardFlow(continueCenter);
                }
                else
                {
                    _adb.Tap(continueCenter.X, continueCenter.Y);
                }
            }
            else
            {
                Console.WriteLine($"[FSM-CS WARNING] phase=return_home status=pending action=fallback_tap reason=continue_unavailable attempt={attempt}");
                _adb.Tap(788, 768);
            }

            Thread.Sleep(2500);
            _popups.DismissStarBonusIfPresent();

            if (detectHomeBaseFunc(out string homeReason))
            {
                Console.WriteLine($"[FSM-CS] phase=return_home status=success reason=home_detected details=\"{homeReason}\" attempt={attempt}");
                Console.WriteLine("[FSM-CS] phase=return_home action=ensure_home status=success");
                return true;
            }

            if (_popups.HandleTreasureHuntIfPresent(verboseNotFound: false))
            {
                Console.WriteLine($"[FSM-CS] phase=return_home status=pending action=clear_treasure_hunt attempt={attempt}");
                Thread.Sleep(1500);
            }
            else
            {
                _popups.HandleTreasureHuntIfPresent(verboseNotFound: false);
            }

            if (detectHomeBaseFunc(out homeReason))
            {
                Console.WriteLine($"[FSM-CS] phase=return_home status=success reason=home_detected details=\"{homeReason}\" attempt={attempt}");
                Console.WriteLine("[FSM-CS] phase=return_home action=ensure_home status=success");
                return true;
            }

            Console.WriteLine($"[FSM-CS] phase=clear_overlay action=android_back reason=home_blocked attempt={attempt}");
            _adb.ExecuteShell("input keyevent KEYCODE_BACK");
            Thread.Sleep(1500);
        }

        bool homeConfirmed = ensureHomeBaseFunc(20);
        Console.WriteLine($"[FSM-CS] phase=return_home action=ensure_home status={(homeConfirmed ? "success" : "fail")}");
        return homeConfirmed;
    }

    public bool HandleClaimRewardFlow(Point claimMatchCenter, int continueTimeoutSeconds = 8)
    {
        return _popups.HandleClaimRewardFlow(claimMatchCenter, continueTimeoutSeconds);
    }
}
