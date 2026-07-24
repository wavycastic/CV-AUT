using System;
using System.Threading;
using OpenCvSharp;

namespace CvAut;

internal sealed class PopupHandlerService : IPopupHandlerService
{
    private readonly ADBHelper _adb;
    private readonly VisionEngine _vision;
    private readonly string _templatesPath;
    private readonly Handlers.ConnectionPopupHandler _connectionHandler;
    private readonly Handlers.StarBonusPopupHandler _starBonusHandler;
    private readonly Handlers.TreasureHuntHandler _treasureHuntHandler;

    private bool _handlingConnectionPopup;

    public PopupHandlerService(ADBHelper adb, VisionEngine vision, string templatesPath)
    {
        _adb = adb;
        _vision = vision;
        _templatesPath = templatesPath;
        _connectionHandler = new Handlers.ConnectionPopupHandler(adb, vision, templatesPath);
        _starBonusHandler = new Handlers.StarBonusPopupHandler(adb, vision, templatesPath);
        _treasureHuntHandler = new Handlers.TreasureHuntHandler(adb, vision, templatesPath);
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
            string details = warningMessage.Replace("[WARN] ", "").Replace(" → ", "_").ToLower();
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

    // Reuses CVAutomationFramework's dialog shape detection logic
    private static bool TryDetectReloadDialogShape(Mat screenshot, out Rect dialogRect)
    {
        dialogRect = default;
        if (screenshot.Empty()) return false;

        Rect roi = GetCenteredConnectionPopupRoi(screenshot.Width, screenshot.Height);
        using Mat crop = new Mat(screenshot, roi);
        using Mat hsv = new Mat();
        Cv2.CvtColor(crop, hsv, ColorConversionCodes.BGR2HSV);

        using Mat mask = new Mat();
        Cv2.InRange(hsv, new Scalar(0, 0, 45), new Scalar(179, 45, 105), mask);

        using Mat kernel = Cv2.GetStructuringElement(MorphShapes.Rect, new Size(15, 15));
        Cv2.MorphologyEx(mask, mask, MorphTypes.Close, kernel);
        Cv2.FindContours(mask, out Point[][] contours, out _, RetrievalModes.External, ContourApproximationModes.ApproxSimple);

        foreach (Point[] contour in contours)
        {
            Rect localRect = Cv2.BoundingRect(contour);
            double area = Cv2.ContourArea(contour);
            double fillRatio = area / Math.Max(1, localRect.Width * localRect.Height);
            double widthRatio = localRect.Width / (double)screenshot.Width;
            double heightRatio = localRect.Height / (double)screenshot.Height;

            if (widthRatio < 0.38 || widthRatio > 0.90 || heightRatio < 0.14 || heightRatio > 0.48 || fillRatio < 0.55)
                continue;

            int centerX = roi.X + localRect.X + localRect.Width / 2;
            int centerY = roi.Y + localRect.Y + localRect.Height / 2;
            bool centered = centerX >= screenshot.Width * 0.20 && centerX <= screenshot.Width * 0.80
                && centerY >= screenshot.Height * 0.25 && centerY <= screenshot.Height * 0.75;
            if (!centered) continue;

            dialogRect = new Rect(roi.X + localRect.X, roi.Y + localRect.Y, localRect.Width, localRect.Height);
            return true;
        }
        return false;
    }

    private static Rect GetCenteredConnectionPopupRoi(int width, int height)
    {
        int x = (int)Math.Round(width * 0.08);
        int y = (int)Math.Round(height * 0.18);
        int roiWidth = (int)Math.Round(width * 0.84);
        int roiHeight = (int)Math.Round(height * 0.64);
        return ImageUtils.ClampRect(new Rect(x, y, roiWidth, roiHeight), width, height);
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
        return _treasureHuntHandler.HandleIfPresent(verboseNotFound);
    }

    public bool DismissStarBonusIfPresent()
    {
        return _starBonusHandler.HandleIfPresent();
    }
}
