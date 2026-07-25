using System;
using System.Threading;
using OpenCvSharp;

namespace CvAut.Automation;

internal sealed class HomeBaseDetector
{
    private readonly IADBHelper _adb;
    private readonly IVisionEngine _vision;
    private readonly PopupHandlerService _popups;

    public HomeBaseDetector(IADBHelper adb, IVisionEngine vision, PopupHandlerService popups)
    {
        _adb = adb;
        _vision = vision;
        _popups = popups;
    }

    public bool EnsureHomeBase(
        Func<int, CancellationToken, bool> sleepFunc,
        Action bootRecovery,
        CancellationToken token,
        int maxWaitSeconds = 50,
        bool allowBootRecovery = true)
    {
        Console.WriteLine("[FSM-CS] phase=home_check status=start");

        DateTime start = DateTime.Now;
        while ((DateTime.Now - start).TotalSeconds < maxWaitSeconds)
        {
            if (DetectHomeBase(out string reason))
            {
                Console.WriteLine("[FSM-CS] phase=home_check status=success");
                return true;
            }

            if (_popups.HandleBlockingConnectionPopup("[WARN] Connection popup while waiting home → reload"))
            {
                start = DateTime.Now;
                Console.WriteLine("[FSM-CS] phase=home_check status=pending action=restart_wait_after_reload");
                continue;
            }

            Console.WriteLine("[FSM-CS] phase=home_check status=pending details=\"waiting\"");
            if (sleepFunc(5000, token)) return false;
        }

        if (!allowBootRecovery)
        {
            Console.WriteLine("[FSM-CS ERROR] phase=home_check status=fail reason=recovery_retry_failed");
            return false;
        }

        Console.WriteLine("[FSM-CS ERROR] phase=home_check status=fail action=reboot reason=detection_failed");
        bootRecovery();
        return EnsureHomeBase(sleepFunc, bootRecovery, token, maxWaitSeconds: 20, allowBootRecovery: false);
    }

    public bool DetectHomeBase(out string reason)
    {
        reason = "not found";
        using Mat? screenshot = _adb.TakeScreenshot();
        if (screenshot == null || screenshot.Empty())
        {
            Console.WriteLine("[FSM-CS WARNING] phase=home_check status=fail reason=screenshot_failed");
            return false;
        }

        if (_vision.FindElement(screenshot, @"ui\game_setting.png", AutomationThresholds.HomeTemplateThreshold, AutomationRoiConstants.GameSettingHomeRoi, out double settingScore) is { } gs)
        {
            reason = $"game_setting score={settingScore:F3}";
            return true;
        }

        if (_vision.FindElement(screenshot, @"ui\shop.png", AutomationThresholds.HomeTemplateThreshold, null, out double shopScore) is { } shop)
        {
            reason = $"shop template at ({shop.X},{shop.Y}) score={shopScore:F3}";
            return true;
        }

        return false;
    }
}
