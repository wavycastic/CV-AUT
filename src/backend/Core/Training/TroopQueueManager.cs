using System;
using System.Collections.Generic;
using System.Threading;
using OpenCvSharp;

namespace CvAut;

internal sealed class TroopQueueManager
{
    private static readonly Point OpenArmyTab = new(1063, 305);
    private static readonly Point CloseArmyTab = new(47, 85);
    private static readonly Point OpenSiegeTab = new(1398, 533);
    private static readonly Point CloseSiegeTab = new(27, 85);
    private static readonly Rect TrashArmyRoi = Rect.FromLTRB(1519, 184, 1570, 231);
    private static readonly Rect TrashSiegeRoi = Rect.FromLTRB(1511, 406, 1577, 458);
    private static readonly Point ClearArmy = new(1546, 209);
    private static readonly Point ConfirmArmy = new(969, 579);
    private static readonly Point ClearSiege = new(1545, 427);
    private static readonly Point ConfirmSiege = new(966, 581);
    private static readonly Dictionary<string, int> SpaceCost = new(StringComparer.OrdinalIgnoreCase)
    {
        ["dragon"] = 20,
        ["electro_dragon"] = 30,
        ["balloon"] = 5
    };

    private readonly IADBHelper _adb;
    private readonly TrainingVision _vision;

    public TroopQueueManager(IADBHelper adb, TrainingVision vision)
    {
        _adb = adb;
        _vision = vision;
    }

    public bool Rebuild(ArmySpec spec, CancellationToken token)
    {
        if (token.IsCancellationRequested) return false;
        ClearQueue(TrashArmyRoi, ClearArmy, ConfirmArmy, token);
        _adb.Tap(OpenArmyTab.X, OpenArmyTab.Y);
        if (token.WaitHandle.WaitOne(1000)) return false;
        using Mat? screenshot = _adb.TakeScreenshot();
        int capacity = screenshot == null || screenshot.Empty()
            ? 260
            : _vision.ReadNumber(screenshot, ArmyStateDetector.ArmySpaceRoi, 120) ?? 260;
        int mainCost = SpaceCost[spec.Main];
        int mainSpace = ((capacity * 80 / 100) / mainCost) * mainCost;
        Queue(spec.Main, mainSpace / mainCost, token);
        Queue("balloon", Math.Max(0, (capacity - mainSpace) / SpaceCost["balloon"]), token);
        _adb.Tap(CloseArmyTab.X, CloseArmyTab.Y);
        return !token.WaitHandle.WaitOne(1000);
    }

    public bool EnsureSiege(ArmySpec spec, CancellationToken token)
    {
        _adb.Tap(OpenSiegeTab.X, OpenSiegeTab.Y);
        if (token.WaitHandle.WaitOne(1000)) return false;
        using Mat? screenshot = _adb.TakeScreenshot();
        if (screenshot == null || screenshot.Empty()
            || !_vision.TryMatch("to_train", spec.Siege, screenshot, 0.70, out _))
        {
            _adb.Tap(CloseSiegeTab.X, CloseSiegeTab.Y);
            return true;
        }
        ClearQueue(TrashSiegeRoi, ClearSiege, ConfirmSiege, token);
        Queue(spec.Siege, 3, token);
        _adb.Tap(CloseSiegeTab.X, CloseSiegeTab.Y);
        return !token.WaitHandle.WaitOne(1000);
    }

    private void Queue(string name, int count, CancellationToken token)
    {
        using Mat? screenshot = _adb.TakeScreenshot();
        if (screenshot == null || screenshot.Empty()
            || !_vision.TryMatch("to_train", name, screenshot, 0.70, out Point center)) return;
        for (int index = 0; index < count && !token.IsCancellationRequested; index++)
            _adb.Tap(center.X, center.Y);
    }

    private void ClearQueue(Rect roi, Point clear, Point confirm, CancellationToken token)
    {
        using Mat? screenshot = _adb.TakeScreenshot();
        if (screenshot == null || screenshot.Empty()
            || !_vision.TryMatch("to_train", "trash_icon", screenshot, 0.80, out _, roi)) return;
        _adb.Tap(clear.X, clear.Y);
        if (token.WaitHandle.WaitOne(1000)) return;
        _adb.Tap(confirm.X, confirm.Y);
        token.WaitHandle.WaitOne(1000);
    }
}
