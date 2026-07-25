using System;
using System.Threading;
using OpenCvSharp;
using Point = OpenCvSharp.Point;

namespace CvAut;

/// <summary>
/// Thin facade over the farming flow. All logic lives in the collaborators under Core/Farming:
/// <see cref="BattleResultDetector"/>, <see cref="BattleRewardReader"/>,
/// <see cref="RewardPopupHandler"/>, <see cref="CameraZoomController"/> and
/// <see cref="TargetAcceptancePolicy"/>. The public surface is unchanged so consumers stay untouched.
/// </summary>
internal sealed class FarmingService
{
    private readonly ADBHelper _adb;
    private readonly Func<int, CancellationToken, bool> _interruptibleSleep;
    private readonly Func<bool> _checkStop;
    private readonly Func<bool> _bootRecovery;
    private readonly Func<CancellationToken, bool> _ensureHomeBase;
    private readonly Func<string, bool> _handleConnectionPopup;
    private readonly Func<bool> _handleTreasureHuntIfPresent;

    private readonly BattleResultDetector _battleResult;
    private readonly BattleRewardReader _rewardReader;
    private readonly RewardPopupHandler _popups;
    private readonly CameraZoomController _camera;

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
        _interruptibleSleep = interruptibleSleep;
        _checkStop = checkStop;
        _bootRecovery = bootRecovery;
        _ensureHomeBase = ensureHomeBase;
        _handleConnectionPopup = handleConnectionPopup;
        _handleTreasureHuntIfPresent = handleTreasureHuntIfPresent;

        _battleResult = new BattleResultDetector(adb, vision, checkStop);
        _rewardReader = new BattleRewardReader(adb, vision, templatesPath);
        _popups = new RewardPopupHandler(adb, vision, templatesPath, _battleResult);
        _camera = new CameraZoomController(adb);
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
        => _battleResult.WaitBattleEnd(token, surrenderConfig);

    public bool BattleEnded(out string matchInfo) => _battleResult.BattleEnded(out matchInfo);

    public bool IsActiveBattlePresent(Mat screenshot, out double endBattleScore)
        => _battleResult.IsActiveBattlePresent(screenshot, out endBattleScore);

    public bool TryFindContinueButton(Mat screenshot, out Point center, out double score)
        => _battleResult.TryFindContinueButton(screenshot, out center, out score);

    public bool DismissStarBonusIfPresent() => _popups.DismissStarBonusIfPresent();

    public bool TryFindStarBonusPopup(Mat screenshot, out Point center, out double score)
        => _popups.TryFindStarBonusPopup(screenshot, out center, out score);

    public int GetStarsFromScreen() => _rewardReader.GetStarsFromScreen();

    public (int Gold, int Elixir, int DarkElixir) GainResources(int stars) => _rewardReader.GainResources(stars);

    public int OcrResourceSum(Mat screenshot, Rect roi, string label, int minValidValue)
        => _rewardReader.OcrResourceSum(screenshot, roi, label, minValidValue);

    public static bool IsPlausibleResourceValue(int value, double confidence, int minValidValue, string label)
        => BattleRewardReader.IsPlausibleResourceValue(value, confidence, minValidValue, label);

    public void ZoomOut() => _camera.ZoomOut();

    public static bool ShouldAcceptTarget((int Gold, int Elixir, int DarkElixir) resources, FarmingTargetConfig config, out string reason)
        => TargetAcceptancePolicy.ShouldAcceptTarget(resources, config, out reason);

    public bool HandleOpenedTreasureChest() => _popups.HandleOpenedTreasureChest();

    public bool TapTreasureRewardContinue() => _popups.TapTreasureRewardContinue();
}
