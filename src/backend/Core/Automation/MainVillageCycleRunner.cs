using System;
using System.Threading;
using CvAut.Configuration;

namespace CvAut.Automation;

internal sealed class MainVillageCycleRunner
{
    private readonly IADBHelper _adb;
    private readonly IVisionEngine _vision;
    private readonly IConfigService _configService;
    private readonly ZoomService _zoom;
    private readonly PopupHandlerService _popups;
    private readonly Training _training;
    private readonly Func<Attacks> _attacksProvider;
    private readonly StatsRepository _stats;
    private readonly HomeBaseDetector _homeDetector;
    private readonly ScoutingFlow _scouting;
    private readonly BattleCompletionWatcher _battleWatcher;
    private readonly HomeResourceCollector _collector;
    private readonly HomeWallUpgradeRunner _wallRunner;

    public MainVillageCycleRunner(
        IADBHelper adb,
        IVisionEngine vision,
        IConfigService configService,
        ZoomService zoom,
        PopupHandlerService popups,
        Training training,
        Func<Attacks> attacksProvider,
        StatsRepository stats,
        HomeBaseDetector homeDetector,
        ScoutingFlow scouting,
        BattleCompletionWatcher battleWatcher,
        HomeResourceCollector collector,
        HomeWallUpgradeRunner wallRunner)
    {
        _adb = adb;
        _vision = vision;
        _configService = configService;
        _zoom = zoom;
        _popups = popups;
        _training = training;
        _attacksProvider = attacksProvider;
        _stats = stats;
        _homeDetector = homeDetector;
        _scouting = scouting;
        _battleWatcher = battleWatcher;
        _collector = collector;
        _wallRunner = wallRunner;
    }

    public void RunCycle(
        int currentVillageIdx,
        ref int cycleCount,
        Func<bool> getFastAttackQueuedFunc,
        Action<bool> setFastAttackQueuedAction,
        ref int sessionBattlesCompleted,
        Func<CancellationToken, bool> checkStopFunc,
        Action waitIfPausedFunc,
        Func<int, CancellationToken, bool> interruptibleSleepFunc,
        Action bootRecoveryFunc,
        Func<int, bool> isNightVillageModeFunc,
        Action<CancellationToken> oneBuilderBaseCycleFunc,
        Action<MainVillageConfig, CancellationToken> runDonateOnlyCycleFunc,
        Action<MainVillageConfig, CancellationToken> tryUseCakeFunc,
        Action<MainVillageConfig, CancellationToken> tryRequestTroopsFunc,
        ShouldSmartSurrenderDelegate shouldSmartSurrenderFunc,
        Action<string, CancellationToken> executeSurrenderFunc,
        CancellationToken token)
    {
        waitIfPausedFunc();
        if (checkStopFunc(token)) return;

        Console.WriteLine($"[FSM-CS] phase=cycle status=start village={currentVillageIdx}");
        bool fastAttackOnly = getFastAttackQueuedFunc();
        setFastAttackQueuedAction(false);
        if (fastAttackOnly)
        {
            Console.WriteLine("[FSM-CS] phase=cycle status=pending mode=fast_attack");
        }

        waitIfPausedFunc();
        if (checkStopFunc(token)) return;

        bool nightVillageMode = isNightVillageModeFunc(currentVillageIdx);
        if (!nightVillageMode)
        {
            Console.WriteLine("[FSM-CS] phase=home_check status=start");
            bool isLoaded = _homeDetector.EnsureHomeBase(interruptibleSleepFunc, bootRecoveryFunc, token, fastAttackOnly ? 8 : 50);
            if (!isLoaded)
            {
                Console.WriteLine("[FSM-CS ERROR] phase=cycle status=skip reason=home_not_detected");
                return;
            }
        }

        if (nightVillageMode)
        {
            oneBuilderBaseCycleFunc(token);
            return;
        }

        MainVillageConfig mainConfig = _configService.GetMainVillageConfig(currentVillageIdx);
        int remainingWallBatch = _configService.GetWallUpgradeConfig(currentVillageIdx).BatchLimit;

        waitIfPausedFunc();
        if (checkStopFunc(token)) return;

        Console.WriteLine("[FSM-CS] phase=cycle status=pending step=1 details=\"initial_zoomout\"");
        _zoom.ZoomOut();

        if (mainConfig.AttackMode == AttackMode.DonateOnly)
        {
            runDonateOnlyCycleFunc(mainConfig, token);
            cycleCount++;
            Console.WriteLine($"[FSM-CS] phase=cycle status=success village={currentVillageIdx} mode=donate_only");
            return;
        }

        if (!fastAttackOnly)
        {
            _adb.Tap(140, 606);
            if (interruptibleSleepFunc(1000, token)) return;

            if (_popups.HandleBlockingConnectionPopup("[WARN] Connection lost → recovering"))
            {
                return;
            }

            waitIfPausedFunc();
            if (checkStopFunc(token)) return;

            if (_popups.HandleBlockingConnectionPopup("[WARN] Connection lost before training → recovering"))
            {
                return;
            }

            TrainingConfig trainConfig = _configService.GetTrainingConfig(currentVillageIdx);

            if (trainConfig.Mode.Equals("quick", StringComparison.OrdinalIgnoreCase) && cycleCount % 5 == 0)
            {
                Console.WriteLine($"[TRAIN] phase=quick_train slot={trainConfig.QuickSlot} status=start");
                _training.QuickTrain(trainConfig.QuickSlot);
            }
            else if (!trainConfig.Mode.Equals("quick", StringComparison.OrdinalIgnoreCase) && cycleCount % 3 == 0)
            {
                Console.WriteLine($"[TRAIN] phase=smart_train strategy={trainConfig.AttackStrategy} status=start");
                if (!_training.SmartTrain(default, trainConfig.AttackStrategy))
                {
                    Console.WriteLine("[TRAIN] phase=smart_train status=skip reason=incomplete");
                    return;
                }
            }

            if (_popups.HandleBlockingConnectionPopup("[WARN] Connection lost → recovering"))
            {
                return;
            }

            waitIfPausedFunc();
            if (checkStopFunc(token)) return;
            Console.WriteLine("[FSM-CS] phase=cycle status=pending step=5 details=\"collecting_resources\"");
            if (_collector.CollectResources(interruptibleSleepFunc, token))
            {
                return;
            }

            tryUseCakeFunc(mainConfig, token);
            tryRequestTroopsFunc(mainConfig, token);

            var collectTimer = WallLogger.StartTimer();
            string collectRunId = WallLogger.GenerateRunId();
            WallLogger.LogInfo("cycle_runner", "start", village: currentVillageIdx, cycle: cycleCount, trigger: "after_collect", batchBudget: remainingWallBatch, runId: collectRunId);
            int wallsUpgraded = 0;
            try
            {
                wallsUpgraded = _wallRunner.TryUpgradeWallsFromHome(currentVillageIdx, cycleCount, seconds => _homeDetector.EnsureHomeBase(interruptibleSleepFunc, bootRecoveryFunc, token, seconds), token, "after_collect", remainingWallBatch, collectRunId);
                remainingWallBatch -= wallsUpgraded;
                WallLogger.LogInfo("cycle_runner", "success", village: currentVillageIdx, cycle: cycleCount, trigger: "after_collect", runId: collectRunId, elapsedMs: collectTimer.ElapsedMilliseconds, extra: $"upgraded={wallsUpgraded}");
            }
            catch (Exception ex)
            {
                WallLogger.LogInfo("cycle_runner", "fail", reason: "exception", village: currentVillageIdx, cycle: cycleCount, trigger: "after_collect", runId: collectRunId, elapsedMs: collectTimer.ElapsedMilliseconds, extra: $"ex_type=\"{ex.GetType().Name}\" ex_msg=\"{ex.Message}\"");
                throw;
            }
        }

        waitIfPausedFunc();
        if (checkStopFunc(token)) return;

        FarmingTargetConfig targetConfig = mainConfig.Target;

        Console.WriteLine($"[CONFIG-CS] phase=startup active_village={currentVillageIdx} gold_req={targetConfig.GoldThreshold} elixir_req={targetConfig.ElixirThreshold} dark_elixir_req={targetConfig.DarkElixirThreshold} total_req={targetConfig.TotalResourceThreshold} target_logic={targetConfig.Logic}");
        Console.WriteLine($"[SCOUT-CS] phase=scout status=start village={currentVillageIdx} gold_req={targetConfig.GoldThreshold} elixir_req={targetConfig.ElixirThreshold} dark_elixir_req={targetConfig.DarkElixirThreshold} total_req={targetConfig.TotalResourceThreshold} target_logic={targetConfig.Logic}");

        _scouting.SearchAttack(interruptibleSleepFunc, checkStopFunc, token);

        int searchCount = 1;
        int maxSearches = 50;
        bool battleExecuted = false;

        while (searchCount <= maxSearches && !checkStopFunc(token))
        {
            waitIfPausedFunc();
            if (checkStopFunc(token)) break;

            Console.WriteLine($"[SCOUT-CS] phase=scout status=pending index={searchCount} max={maxSearches}");

            if (_popups.HandleBlockingConnectionPopup("[WARN] Connection lost during evaluation → recovering"))
            {
                return;
            }

            if (!_scouting.WaitForScoutScreen())
            {
                Console.WriteLine("[SCOUT-CS WARNING] phase=scout status=pending action=recover reason=scouting_ui_not_detected");
                bootRecoveryFunc();
                return;
            }

            bool nextButtonFound = false;
            for (int attempt = 1; attempt <= 2; attempt++)
            {
                waitIfPausedFunc();
                if (checkStopFunc(token)) break;

                if (_scouting.IsNextButtonPresent())
                {
                    nextButtonFound = true;
                    break;
                }

                Console.WriteLine($"[SCOUT-CS WARNING] phase=scout status=retry action=next attempt={attempt} max=2 reason=next_button_unavailable");
                if (interruptibleSleepFunc(500, token)) break;
            }

            if (!nextButtonFound)
            {
                Console.WriteLine("[SCOUT-CS WARNING] phase=scout status=pending action=recover reason=next_button_unavailable");
                bootRecoveryFunc();
                Console.WriteLine("[FSM-CS] phase=recovery status=success");
                return;
            }

            var resources = IsTarget.ExtractResources(_adb, _vision);

            bool targetAccepted = TargetAcceptancePolicy.ShouldAcceptTarget(resources, targetConfig, out string targetReason);
            if (targetAccepted)
            {
                Console.WriteLine($"[SCOUT-CS] phase=scout status=success gold={resources.Gold} elixir={resources.Elixir} dark_elixir={resources.DarkElixir} total={resources.Gold + resources.Elixir} target_logic={targetConfig.Logic} reason={targetReason} details=\"target_accepted\"");
                Console.WriteLine("[SCOUT-CS] phase=scout status=pending action=prepare_attack");
                if (interruptibleSleepFunc(1500, token)) break;

                string attackStrategy = _configService.GetAttackStrategy(currentVillageIdx);
                Console.WriteLine($"[ATTACK-CS] phase=select_strategy status=success village={currentVillageIdx} strategy={attackStrategy}");
                _attacksProvider().Run(attackStrategy, token, mainConfig.UseEventTroops);
                battleExecuted = true;

                waitIfPausedFunc();
                if (checkStopFunc(token)) break;

                bool battleWaitOk = _battleWatcher.WaitBattleEnd(checkStopFunc, waitIfPausedFunc, bootRecoveryFunc, shouldSmartSurrenderFunc, executeSurrenderFunc, token, mainConfig.SmartSurrender);
                if (!battleWaitOk)
                {
                    return;
                }

                bool returnedHome = false;
                int starsGot = _stats.GetStarsFromScreen();
                var gained = _stats.GainResources(starsGot);
                Console.WriteLine($"[FSM-CS] phase=battle_stats stars={starsGot} gold={gained.Gold} elixir={gained.Elixir} dark_elixir={gained.DarkElixir} status=success");

                if (_configService.Current.EnableStats)
                {
                    _stats.UpdateStats(currentVillageIdx, starsGot, gained);
                }
                else
                {
                    Console.WriteLine("[FSM-CS] phase=battle_stats status=skip reason=stats_disabled");
                }
                sessionBattlesCompleted++;

                returnedHome = _battleWatcher.ReturnHome(_homeDetector.DetectHomeBase, seconds => _homeDetector.EnsureHomeBase(interruptibleSleepFunc, bootRecoveryFunc, token, seconds));
                setFastAttackQueuedAction(returnedHome);

                waitIfPausedFunc();
                if (checkStopFunc(token)) break;

                if (returnedHome)
                {
                    var postBattleTimer = WallLogger.StartTimer();
                    string postBattleRunId = WallLogger.GenerateRunId();
                    WallLogger.LogInfo("cycle_runner", "start", village: currentVillageIdx, cycle: cycleCount, trigger: "post_battle", batchBudget: Math.Max(0, remainingWallBatch), runId: postBattleRunId);
                    try
                    {
                        _wallRunner.TryUpgradeWallsFromHome(currentVillageIdx, cycleCount, seconds => _homeDetector.EnsureHomeBase(interruptibleSleepFunc, bootRecoveryFunc, token, seconds), token, "post_battle", Math.Max(0, remainingWallBatch), postBattleRunId);
                        WallLogger.LogInfo("cycle_runner", "success", village: currentVillageIdx, cycle: cycleCount, trigger: "post_battle", runId: postBattleRunId, elapsedMs: postBattleTimer.ElapsedMilliseconds);
                    }
                    catch (Exception ex)
                    {
                        WallLogger.LogInfo("cycle_runner", "fail", reason: "exception", village: currentVillageIdx, cycle: cycleCount, trigger: "post_battle", runId: postBattleRunId, elapsedMs: postBattleTimer.ElapsedMilliseconds, extra: $"ex_type=\"{ex.GetType().Name}\" ex_msg=\"{ex.Message}\"");
                        throw;
                    }
                }

                checkStopFunc(token);
                break;
            }
            else
            {
                Console.WriteLine($"[SCOUT-CS] phase=scout status=skip gold={resources.Gold} elixir={resources.Elixir} dark_elixir={resources.DarkElixir} total={resources.Gold + resources.Elixir} target_logic={targetConfig.Logic} reason={targetReason} details=\"target_skipped\"");
                _scouting.SearchNext();
                searchCount++;
            }
        }

        if (!battleExecuted && !checkStopFunc(token))
        {
            Console.WriteLine("[SCOUT-CS WARNING] phase=scout status=fail reason=search_limit_reached action=return_home");
            _adb.Tap(80, 780);
            if (interruptibleSleepFunc(1000, token)) return;
            _adb.Tap(960, 560);
            if (interruptibleSleepFunc(2000, token)) return;
            _adb.Tap(800, 780);
            interruptibleSleepFunc(5000, token);
        }

        cycleCount++;
        Console.WriteLine($"[FSM-CS] phase=cycle status=success village={currentVillageIdx}");
    }
}
