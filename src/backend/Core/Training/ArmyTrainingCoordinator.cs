using System;
using System.Text.Json;
using System.Threading;
using OpenCvSharp;

namespace CvAut;

internal sealed class ArmyTrainingCoordinator
{
    private static readonly Point OpenArmyWindow = new(62, 658);
    private static readonly Point CloseArmyWindow = new(1545, 81);
    private static readonly Rect ArmyWindowRoi = new(76, 57, 489, 99);

    private readonly IADBHelper _adb;
    private readonly TrainingVision _vision;
    private readonly ArmyStateDetector _detector;
    private readonly TrainingReadinessPolicy _policy;
    private readonly TroopQueueManager _troops;
    private readonly SpellQueueManager _spells;

    public ArmyTrainingCoordinator(
        IADBHelper adb,
        TrainingVision vision,
        ArmyStateDetector detector,
        TrainingReadinessPolicy policy,
        TroopQueueManager troops,
        SpellQueueManager spells)
    {
        _adb = adb;
        _vision = vision;
        _detector = detector;
        _policy = policy;
        _troops = troops;
        _spells = spells;
    }

    public bool Execute(JsonElement config, string? attackStrategy, CancellationToken token)
    {
        ArmySpec spec = TrainingPlanResolver.Resolve(config, attackStrategy);
        Console.WriteLine($"[TRAIN] phase=smart_train status=start main_troop={spec.Main}");
        if (!OpenAndValidate(token))
        {
            Console.WriteLine("[TRAIN] phase=smart_train status=skip reason=army_window_not_detected");
            return true;
        }

        ArmyState state = _detector.Detect(spec, token);
        TrainingReadiness readiness = _policy.Evaluate(state);
        if (readiness.IsReady)
        {
            Console.WriteLine("[TRAIN] phase=smart_train status=skip reason=army_ready");
            Close();
            return true;
        }

        bool success = true;
        if (readiness.RebuildArmy) success &= _troops.Rebuild(spec, token);
        if (readiness.RebuildSpells) success &= _spells.Rebuild(token);
        if (readiness.RebuildSiege) success &= _troops.EnsureSiege(spec, token);
        Close();
        token.WaitHandle.WaitOne(1000);
        return success && !token.IsCancellationRequested;
    }

    private bool OpenAndValidate(CancellationToken token)
    {
        _adb.Tap(OpenArmyWindow.X, OpenArmyWindow.Y);
        if (token.WaitHandle.WaitOne(1000)) return false;
        using Mat? screenshot = _adb.TakeScreenshot();
        return screenshot != null && !screenshot.Empty()
            && _vision.TryMatchRoot("army_window.png", screenshot, 0.60, out _, ArmyWindowRoi);
    }

    private void Close() => _adb.Tap(CloseArmyWindow.X, CloseArmyWindow.Y);
}
