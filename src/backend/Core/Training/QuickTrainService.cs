using System;
using System.Threading;
using OpenCvSharp;

namespace CvAut;

internal sealed class QuickTrainService
{
    private static readonly Point OpenArmyWindow = new(62, 658);
    private static readonly Point CloseArmyWindow = new(1545, 81);
    private static readonly Point RecipePane = new(777, 90);
    private static readonly Point ConfirmRecipe = new(972, 584);
    private static readonly Rect ArmyWindowRoi = new(76, 57, 489, 99);
    private static readonly Rect Slot1Roi = Rect.FromLTRB(1364, 189, 1574, 425);
    private static readonly Rect Slot2Roi = Rect.FromLTRB(1368, 486, 1572, 735);

    private readonly IADBHelper _adb;
    private readonly TrainingVision _vision;

    public QuickTrainService(IADBHelper adb, TrainingVision vision)
    {
        _adb = adb;
        _vision = vision;
    }

    public bool Execute(int slot, CancellationToken token)
    {
        if (!OpenAndValidate(token)) return false;
        _adb.Tap(RecipePane.X, RecipePane.Y);
        if (token.WaitHandle.WaitOne(200)) return false;
        using Mat? screenshot = _adb.TakeScreenshot();
        if (screenshot == null || screenshot.Empty()) return CloseAndFail();

        Rect slotRoi = slot == 2 ? Slot2Roi : Slot1Roi;
        if (!_vision.TryMatchRoot("use_button.png", screenshot, 0.90, out Point button, slotRoi))
            return CloseAndFail();
        _adb.Tap(button.X, button.Y);
        if (token.WaitHandle.WaitOne(250)) return false;

        using Mat? confirm = _adb.TakeScreenshot();
        if (confirm != null && !confirm.Empty()
            && _vision.TryMatchRoot("use_army_recipe_window.png", confirm, 0.90, out _))
        {
            _adb.Tap(ConfirmRecipe.X, ConfirmRecipe.Y);
        }
        token.WaitHandle.WaitOne(150);
        _adb.Tap(CloseArmyWindow.X, CloseArmyWindow.Y);
        return !token.IsCancellationRequested;
    }

    private bool OpenAndValidate(CancellationToken token)
    {
        _adb.Tap(OpenArmyWindow.X, OpenArmyWindow.Y);
        if (token.WaitHandle.WaitOne(1000)) return false;
        using Mat? screenshot = _adb.TakeScreenshot();
        return screenshot != null && !screenshot.Empty()
            && _vision.TryMatchRoot("army_window.png", screenshot, 0.60, out _, ArmyWindowRoi);
    }

    private bool CloseAndFail()
    {
        _adb.Tap(CloseArmyWindow.X, CloseArmyWindow.Y);
        return false;
    }
}
