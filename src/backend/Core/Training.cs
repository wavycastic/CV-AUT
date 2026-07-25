using System;
using System.IO;
using System.Text.Json;
using System.Threading;
using OpenCvSharp;

namespace CvAut;

/// <summary>
/// Backward-compatible facade over focused army training services.
/// </summary>
internal sealed class Training
{
    private readonly QuickTrainService _quickTrain;
    private readonly ArmyTrainingCoordinator _coordinator;
    private readonly DonationService _donations;

    public Training(IADBHelper adb, string templatesPath, IVisionEngine vision)
        : this(adb, templatesPath, vision, null)
    {
    }

    internal Training(IADBHelper adb, string templatesPath, IVisionEngine vision, object? unused = null)
    {
        ArgumentNullException.ThrowIfNull(adb);
        ArgumentNullException.ThrowIfNull(vision);
        string templateRoot = Path.Combine(templatesPath, "Smart_Auto_train");
        var trainingVision = new TrainingVision(vision, templateRoot);
        var heroReadiness = new HeroReadinessService(trainingVision);
        var detector = new ArmyStateDetector(adb, trainingVision, heroReadiness);
        var troopQueue = new TroopQueueManager(adb, trainingVision);
        var spellQueue = new SpellQueueManager(adb, trainingVision);
        _quickTrain = new QuickTrainService(adb, trainingVision);
        _coordinator = new ArmyTrainingCoordinator(
            adb,
            trainingVision,
            detector,
            new TrainingReadinessPolicy(),
            troopQueue,
            spellQueue);
        _donations = new DonationService(adb);
    }

    public bool QuickTrain(int quickSlot = 1, CancellationToken token = default)
        => _quickTrain.Execute(Math.Clamp(quickSlot, 1, 2), token);

    public bool SmartTrain(
        JsonElement config,
        string? attackStrategy = null,
        CancellationToken token = default)
        => _coordinator.Execute(config, attackStrategy, token);

    public bool RequestClanTroops(CancellationToken token)
        => _donations.RequestClanTroops(token);

    public bool ExecuteQuickTrain(int slotIndex, CancellationToken token)
        => QuickTrain(slotIndex, token);

    public static void DiagnoseSavedArmyWindow(string imagePath, string templatesPath)
    {
        if (!File.Exists(imagePath))
        {
            Console.WriteLine($"[TRAIN] phase=diagnose status=fail reason=image_not_found details=\"{imagePath}\"");
            return;
        }
        using Mat screenshot = Cv2.ImRead(imagePath, ImreadModes.Color);
        Console.WriteLine(screenshot.Empty()
            ? "[TRAIN] phase=diagnose status=fail reason=image_empty"
            : $"[TRAIN] phase=diagnose status=success width={screenshot.Width} height={screenshot.Height} templates=\"{templatesPath}\"");
    }
}
