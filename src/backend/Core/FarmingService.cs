using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using OpenCvSharp;
using Point = OpenCvSharp.Point;

namespace CvAut;

internal sealed class FarmingService
{
    private readonly ADBHelper _adb;
    private readonly VisionEngine _vision;
    private readonly string _templatesPath;
    private readonly Func<int, CancellationToken, bool> _interruptibleSleep;
    private readonly Func<bool> _checkStop;
    private readonly Func<bool> _bootRecovery;
    private readonly Func<CancellationToken, bool> _ensureHomeBase;
    private readonly Func<string, bool> _handleConnectionPopup;
    private readonly Func<bool> _handleTreasureHuntIfPresent;

    private static readonly Rect BattleEndedRoi = Rect.FromLTRB(632, 222, 989, 841);
    private static readonly Rect StarBonusPopupRoi = Rect.FromLTRB(430, 55, 1170, 145);
    private static readonly Point TreasureHuntOpenedChestTapPoint = new(800, 455);
    private static readonly Point TreasureHuntRewardContinueTapPoint = new(800, 750);
    private static readonly Point StarBonusOkayTapPoint = new(808, 766);
    private const double TreasureHuntMarkerThreshold = 0.82;
    private const double StarBonusPopupThreshold = 0.70;

    public FarmingService(
        ADBHelper adb,
        VisionEngine vision,
        string templatesPath,
        Func<int, CancellationToken, bool> interruptibleSleep,
        Func<bool> checkStop,
        Func<bool> bootRecovery,
        Func<CancellationToken, bool> ensureHomeBase,
        Func<string, bool> handleConnectionPopup,
        Func<bool> handleTreasureHuntIfPresent)
    {
        _adb = adb;
        _vision = vision;
        _templatesPath = templatesPath;
        _interruptibleSleep = interruptibleSleep;
        _checkStop = checkStop;
        _bootRecovery = bootRecovery;
        _ensureHomeBase = ensureHomeBase;
        _handleConnectionPopup = handleConnectionPopup;
        _handleTreasureHuntIfPresent = handleTreasureHuntIfPresent;
    }

    public void SearchAttack(CancellationToken token)
    {
        _adb.Tap(113, 797);
        if (_interruptibleSleep(700, token)) return;
        _handleTreasureHuntIfPresent();
        if (_checkStop()) return;
        _adb.Tap(272, 659);
        if (_interruptibleSleep(700, token)) return;
        _adb.Tap(1445, 804);
    }

    public bool WaitBattleEnd(CancellationToken token, SmartSurrenderConfig? surrenderConfig = null)
    {
        Console.WriteLine("[FSM-CS] phase=battle_wait status=start");
        DateTime start = DateTime.Now;
        int stableResultMatches = 0;
        bool waitingLogged = false;
        bool resultDetectedLogged = false;
        bool smartSurrenderExecuted = false;
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
        endBattleScore = 0;
        if (screenshot == null || screenshot.Empty()) return false;

        Point? endBtn = _vision.FindElement(screenshot, @"ui\end_battle.png", AutomationThresholds.ScoutUiThreshold, AutomationRoiConstants.ScoutUiRoi, out endBattleScore);
        if (endBtn.HasValue) return true;

        Rect endBtnRoi = ImageUtils.ClampRect(new Rect(20, 670, 180, 70), screenshot.Width, screenshot.Height);
        if (endBtnRoi.Width > 0 && endBtnRoi.Height > 0)
        {
            using Mat roiMat = new Mat(screenshot, endBtnRoi);
            int redPixels = 0;
            int totalPixels = roiMat.Rows * roiMat.Cols;
            for (int y = 0; y < roiMat.Rows; y++)
            {
                for (int x = 0; x < roiMat.Cols; x++)
                {
                    Vec3b px = roiMat.At<Vec3b>(y, x);
                    if (px.Item2 > 160 && px.Item1 < 90 && px.Item0 < 90 && (px.Item2 - px.Item1) > 60 && (px.Item2 - px.Item0) > 60)
                    {
                        redPixels++;
                    }
                }
            }
            double redRatio = redPixels / (double)totalPixels;
            if (redPixels >= 120 || redRatio >= 0.08)
            {
                endBattleScore = 0.99;
                return true;
            }
        }

        return false;
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

    public int GetStarsFromScreen()
    {
        using Mat? screenshot = _adb.TakeScreenshot();
        if (screenshot == null || screenshot.Empty())
        {
            Console.WriteLine("[FSM-CS WARNING] phase=battle_stats status=fail reason=screenshot_failed");
            return 3;
        }

        using Mat gray = new Mat();
        Cv2.CvtColor(screenshot, gray, ColorConversionCodes.BGR2GRAY);
        using Mat thresh = new Mat();
        Cv2.Threshold(gray, thresh, 200, 255, ThresholdTypes.Binary);

        using Mat template3 = Cv2.ImRead(Path.Combine(_templatesPath, @"stars\3_stars.png"), ImreadModes.Grayscale);
        using Mat template2 = Cv2.ImRead(Path.Combine(_templatesPath, @"stars\2_stars.png"), ImreadModes.Grayscale);
        using Mat template1 = Cv2.ImRead(Path.Combine(_templatesPath, @"stars\1_star.png"), ImreadModes.Grayscale);

        if (template3 != null && !template3.Empty() && MatchStarTemplate(thresh, template3, 0.45)) return 3;
        if (template2 != null && !template2.Empty() && MatchStarTemplate(thresh, template2, 0.55)) return 2;
        if (template1 != null && !template1.Empty() && MatchStarTemplate(thresh, template1, 0.55)) return 1;

        Console.WriteLine("[FSM-CS WARNING] phase=battle_stats status=fallback reason=star_template_not_found");
        return 3;
    }

    private static bool MatchStarTemplate(Mat grayScreen, Mat starTemplate, double threshold)
    {
        using Mat res = new Mat();
        Cv2.MatchTemplate(grayScreen, starTemplate, res, TemplateMatchModes.CCoeffNormed);
        Cv2.MinMaxLoc(res, out _, out double maxVal, out _, out _);
        return maxVal >= threshold;
    }

    public (int Gold, int Elixir, int DarkElixir) GainResources(int stars)
    {
        using Mat? screenshot = _adb.TakeScreenshot();
        if (screenshot == null || screenshot.Empty())
        {
            Console.WriteLine("[FSM-CS] phase=gain_resources status=fail reason=screenshot_failed");
            return (0, 0, 0);
        }

        int gold = OcrResourceSum(screenshot, new Rect(315, 374, 220, 30), "gold", 100);
        int elixir = OcrResourceSum(screenshot, new Rect(710, 374, 220, 30), "elixir", 100);
        int darkElixir = OcrResourceSum(screenshot, new Rect(1085, 374, 220, 30), "dark_elixir", 10);
        Console.WriteLine($"[FSM-CS] phase=gain_resources status=success gold={gold} elixir={elixir} dark_elixir={darkElixir} stars={stars}");
        return (gold, elixir, darkElixir);
    }

    public int OcrResourceSum(Mat screenshot, Rect roi, string label, int minValidValue)
    {
        if (_vision.TryExtractNumericalMetrics(screenshot, roi, out int value, out double confidence) && IsPlausibleResourceValue(value, confidence, minValidValue, label))
            return value;
        return 0;
    }

    public static bool IsPlausibleResourceValue(int value, double confidence, int minValidValue, string label)
    {
        if (value < 0) { Console.WriteLine($"[OCR-WARN] phase=validation status=reject label={label} value={value} confidence={confidence:F2} reason=negative_value"); return false; }
        if (value < minValidValue) { Console.WriteLine($"[OCR-WARN] phase=validation status=reject label={label} value={value} min={minValidValue} reason=below_minimum"); return false; }
        if (confidence < 0.25) { Console.WriteLine($"[OCR-WARN] phase=validation status=reject label={label} value={value} confidence={confidence:F2} reason=low_confidence"); return false; }
        return true;
    }

    public void ZoomOut()
    {
        Console.WriteLine("[FSM-CS] phase=camera_zoom status=start");
        IntPtr memuParent = FindMainWindowByProcessName("MEmu");
        if (memuParent == IntPtr.Zero) memuParent = FindWindow(null, "MEmu");

        IntPtr bsParent = FindMainWindowByProcessName("HD-Player", "BlueStacks");
        if (bsParent == IntPtr.Zero) bsParent = FindWindow(null, "BlueStacks App Player");

        if (memuParent != IntPtr.Zero)
        {
            Console.WriteLine("[FSM-CS] phase=camera_zoom status=pending details=\"memu_detected\"");
            SendKeyToWindow(memuParent, (IntPtr)0x72, repetitions: 4, gapMs: 1000);
            Console.WriteLine("[FSM-CS] phase=camera_zoom status=success details=\"memu\"");
        }
        else if (bsParent != IntPtr.Zero)
        {
            Console.WriteLine("[FSM-CS] phase=camera_zoom status=pending details=\"bluestacks_detected\"");
            bool ok = _adb.PinchInZoomOut(count: 3, durationMs: 450, intervalMs: 500);
            Console.WriteLine(ok
                ? "[FSM-CS] phase=camera_zoom status=success details=\"bluestacks_adb_pinch\""
                : "[FSM-CS WARNING] phase=camera_zoom status=fail reason=no_confirmation");
        }
        else
        {
            Console.WriteLine("[FSM-CS WARNING] phase=camera_zoom status=skip reason=emulator_window_not_found");
        }
    }

    public static bool ShouldAcceptTarget((int Gold, int Elixir, int DarkElixir) resources, FarmingTargetConfig config, out string reason)
    {
        int total = resources.Gold + resources.Elixir;
        bool goldOk = resources.Gold >= config.GoldThreshold;
        bool elixirOk = resources.Elixir >= config.ElixirThreshold;
        bool darkOk = config.DarkElixirThreshold <= 0 || resources.DarkElixir >= config.DarkElixirThreshold;
        bool totalOk = total >= config.TotalResourceThreshold;

        bool accepted = config.Logic switch
        {
            TargetSelectionLogic.And => goldOk && elixirOk && darkOk,
            TargetSelectionLogic.Or => goldOk || elixirOk || darkOk,
            _ => totalOk && darkOk
        };

        reason = config.Logic switch
        {
            TargetSelectionLogic.And => $"and gold_ok={goldOk} elixir_ok={elixirOk} dark_ok={darkOk}",
            TargetSelectionLogic.Or => $"or gold_ok={goldOk} elixir_ok={elixirOk} dark_ok={darkOk}",
            _ => $"total total_ok={totalOk} dark_ok={darkOk}"
        };
        return accepted;
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
            if (screenshot != null && !screenshot.Empty() && TryFindContinueButton(screenshot, out Point continueCenter, out double score))
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

    // --- Win32 P/Invoke helpers ---
    [System.Runtime.InteropServices.DllImport("user32.dll", CharSet = System.Runtime.InteropServices.CharSet.Auto)]
    private static extern bool PostMessage(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);

    [System.Runtime.InteropServices.DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr FindWindow(string? lpClassName, string? lpWindowName);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    [return: System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)]
    private static extern bool AttachThreadInput(uint idAttach, uint idAttachTo, bool fAttach);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

    [System.Runtime.InteropServices.DllImport("kernel32.dll")]
    private static extern uint GetCurrentThreadId();

    private static IntPtr FindMainWindowByProcessName(params string[] processNames)
    {
        foreach (string processName in processNames)
        {
            try
            {
                foreach (var process in System.Diagnostics.Process.GetProcessesByName(processName))
                    if (process.MainWindowHandle != IntPtr.Zero) return process.MainWindowHandle;
            }
            catch { }
        }
        return IntPtr.Zero;
    }

    private static void SendKeyToWindow(IntPtr hWnd, IntPtr virtualKey, int repetitions, int gapMs)
    {
        const uint WM_KEYDOWN = 0x0100;
        const uint WM_KEYUP = 0x0101;
        uint currentThreadId = GetCurrentThreadId();
        uint targetThreadId = GetWindowThreadProcessId(hWnd, out _);

        for (int i = 0; i < repetitions; i++)
        {
            bool attached = false;
            if (targetThreadId != 0 && targetThreadId != currentThreadId)
                attached = AttachThreadInput(currentThreadId, targetThreadId, true);
            try
            {
                PostMessage(hWnd, WM_KEYDOWN, virtualKey, IntPtr.Zero);
                Thread.Sleep(20);
                PostMessage(hWnd, WM_KEYUP, virtualKey, IntPtr.Zero);
            }
            finally
            {
                if (attached) AttachThreadInput(currentThreadId, targetThreadId, false);
            }
            if (i < repetitions - 1) Thread.Sleep(gapMs);
        }
    }
}
