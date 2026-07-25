using System;
using System.Threading;
using OpenCvSharp;

namespace CvAut.Automation;

internal sealed class ScoutingFlow
{
    private readonly IADBHelper _adb;
    private readonly IVisionEngine _vision;
    private readonly PopupHandlerService _popups;

    public ScoutingFlow(IADBHelper adb, IVisionEngine vision, PopupHandlerService popups)
    {
        _adb = adb;
        _vision = vision;
        _popups = popups;
    }

    public void SearchAttack(
        Func<int, CancellationToken, bool> sleepFunc,
        Func<CancellationToken, bool> checkStopFunc,
        CancellationToken token)
    {
        _adb.Tap(113, 797);
        if (sleepFunc(700, token)) return;
        _popups.HandleTreasureHuntIfPresent();
        if (checkStopFunc(token)) return;
        _adb.Tap(272, 659);
        if (sleepFunc(700, token)) return;
        _adb.Tap(1445, 804);
    }

    public void SearchNext()
    {
        _adb.Tap(1432, 637);
    }

    public bool IsNextButtonPresent()
    {
        using Mat? screenshot = _adb.TakeScreenshot();
        if (screenshot == null || screenshot.Empty())
        {
            Console.WriteLine("[SCOUT-CS WARNING] phase=scout status=fail reason=screenshot_failed");
            return false;
        }

        bool found = _vision.FindElement(screenshot, @"ui\next_button.png", AutomationThresholds.NextButtonThreshold, AutomationRoiConstants.NextButtonRoi, out _) != null;
        return found;
    }

    public bool WaitForScoutScreen(int timeoutSeconds = 12, int intervalMs = 500)
    {
        Console.WriteLine("[SCOUT-CS] phase=scout_wait status=start details=\"loading\"");

        DateTime start = DateTime.Now;
        while ((DateTime.Now - start).TotalSeconds < timeoutSeconds)
        {
            Thread.Sleep(350);

            using Mat? screenshot = _adb.TakeScreenshot();
            if (screenshot == null || screenshot.Empty())
            {
                Console.WriteLine("[SCOUT-CS WARNING] phase=scout_wait status=fail reason=screenshot_failed");
                Thread.Sleep(intervalMs);
                continue;
            }

            if (_vision.FindElement(screenshot, @"ui\end_battle.png", AutomationThresholds.ScoutUiThreshold, AutomationRoiConstants.ScoutUiRoi, out _) != null)
            {
                Console.WriteLine("[SCOUT-CS] phase=scout_wait status=success details=\"ready\"");
                return true;
            }

            if (_popups.HandleTreasureHuntIfPresent(verboseNotFound: false))
            {
                continue;
            }

            Thread.Sleep(intervalMs);
        }

        Console.WriteLine("[SCOUT-CS WARNING] phase=scout_wait status=fail reason=timeout");
        return false;
    }
}
