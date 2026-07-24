using System;
using System.Threading;
using OpenCvSharp;

namespace CvAut;

internal sealed class SpellQueueManager
{
    private static readonly Point OpenSpellTab = new(1008, 531);
    private static readonly Point CloseSpellTab = new(59, 52);
    private static readonly Rect TrashRoi = Rect.FromLTRB(1197, 408, 1250, 455);
    private static readonly Point Clear = new(1225, 429);
    private static readonly Point Confirm = new(978, 583);

    private readonly IADBHelper _adb;
    private readonly TrainingVision _vision;

    public SpellQueueManager(IADBHelper adb, TrainingVision vision)
    {
        _adb = adb;
        _vision = vision;
    }

    public bool Rebuild(CancellationToken token)
    {
        ClearQueue(token);
        _adb.Tap(OpenSpellTab.X, OpenSpellTab.Y);
        if (token.WaitHandle.WaitOne(1000)) return false;
        using Mat? screenshot = _adb.TakeScreenshot();
        int capacity = screenshot == null || screenshot.Empty()
            ? 11
            : ReadCapacity(screenshot);
        int rageSpace = ((capacity * 80 / 100) / 2) * 2;
        Queue("rage", rageSpace / 2, token);
        Queue("freeze", Math.Max(0, capacity - rageSpace), token);
        _adb.Tap(CloseSpellTab.X, CloseSpellTab.Y);
        return !token.WaitHandle.WaitOne(1000);
    }

    private int ReadCapacity(Mat screenshot)
        => _vision.TryReadFraction(screenshot, ArmyStateDetector.SpellSpaceRoi, out _, out int capacity)
            ? capacity
            : 11;

    private void Queue(string name, int count, CancellationToken token)
    {
        using Mat? screenshot = _adb.TakeScreenshot();
        if (screenshot == null || screenshot.Empty()
            || !_vision.TryMatch("to_train", name, screenshot, 0.70, out Point center)) return;
        for (int index = 0; index < count && !token.IsCancellationRequested; index++)
            _adb.Tap(center.X, center.Y);
    }

    private void ClearQueue(CancellationToken token)
    {
        using Mat? screenshot = _adb.TakeScreenshot();
        if (screenshot == null || screenshot.Empty()
            || !_vision.TryMatch("to_train", "trash_icon", screenshot, 0.80, out _, TrashRoi)) return;
        _adb.Tap(Clear.X, Clear.Y);
        if (token.WaitHandle.WaitOne(1000)) return;
        _adb.Tap(Confirm.X, Confirm.Y);
        token.WaitHandle.WaitOne(1000);
    }
}
