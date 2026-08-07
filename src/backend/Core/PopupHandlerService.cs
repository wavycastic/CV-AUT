using System;
using System.Threading;
using OpenCvSharp;

namespace CvAut;

internal sealed class PopupHandlerService : IPopupHandlerService
{
    private readonly IADBHelper _adb;
    private readonly Handlers.ConnectionPopupHandler _connectionHandler;
    private readonly Handlers.StarBonusPopupHandler _starBonusHandler;
    private readonly Handlers.EventRewardHandler _eventRewardHandler;

    private bool _handlingConnectionPopup;

    public PopupHandlerService(IADBHelper adb, IVisionEngine vision, string templatesPath)
    {
        _adb = adb;
        _connectionHandler = new Handlers.ConnectionPopupHandler(adb, vision, templatesPath);
        _starBonusHandler = new Handlers.StarBonusPopupHandler(adb, vision, templatesPath);
        _eventRewardHandler = new Handlers.EventRewardHandler(adb, vision, templatesPath);
    }

    public bool IsHandlingConnectionPopup => _handlingConnectionPopup;

    public bool HandleBlockingConnectionPopup(string warningMessage, Func<bool>? reloadAction = null, bool disableDialogShapeFallback = false)
    {
        if (_handlingConnectionPopup) return false;

        if (!ConnectionPopupVisible(out string matchInfo, allowDialogShapeFallback: !disableDialogShapeFallback))
            return false;

        _handlingConnectionPopup = true;
        try
        {
            string details = warningMessage.Replace("[WARN] ", "").Replace(" \u2192 ", "_").ToLower();
            Console.WriteLine($"[FSM-CS WARNING] phase=connection_check status=fail action=recover reason=\"connection_lost\" details=\"{details} ({matchInfo})\"");

            if (reloadAction != null)
                return reloadAction();
            else
                BootRecovery();
            return true;
        }
        finally
        {
            _handlingConnectionPopup = false;
        }
    }

    public bool ConnectionPopupVisible(out string matchInfo, bool allowDialogShapeFallback = true)
    {
        matchInfo = "none";
        using Mat? screenshot = _adb.TakeScreenshot();
        if (screenshot == null || screenshot.Empty()) return false;

        if (_connectionHandler.ConnectionPopupVisible(screenshot, out matchInfo,
                allowDialogShapeFallback: allowDialogShapeFallback,
                disableDialogShapeFallback: !allowDialogShapeFallback))
            return true;

        return false;
    }

    private void BootRecovery()
    {
        Console.WriteLine("[FSM-CS] phase=recovery status=start action=restart_app package=\"com.supercell.clashofclans\"");
        _adb.ExecuteShell("am force-stop com.supercell.clashofclans");
        _adb.ExecuteShell("monkey -p com.supercell.clashofclans -c android.intent.category.LAUNCHER 1");
        Console.WriteLine("[FSM-CS] phase=recovery status=pending action=wait_app_load");
        Thread.Sleep(10000);
        Console.WriteLine("[FSM-CS] phase=recovery status=pending action=clear_popups");
        _adb.Tap(146, 487);
    }

    public bool HandleTreasureHuntIfPresent(bool verboseNotFound = true)
    {
        return _eventRewardHandler.HandleTreasureHuntIfPresent(verboseNotFound);
    }

    public bool HandleClaimRewardFlow(Point claimMatchCenter, int continueTimeoutSeconds = 8)
    {
        return _eventRewardHandler.HandleClaimRewardFlow(claimMatchCenter, continueTimeoutSeconds);
    }

    public bool DismissStarBonusIfPresent()
    {
        return _starBonusHandler.HandleIfPresent();
    }
}
