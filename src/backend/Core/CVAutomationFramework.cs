using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using CvAut.Automation;
using CvAut.Configuration;
using OpenCvSharp;

namespace CvAut;

internal partial class CVAutomationFramework : IAutomationRunner
{
    private readonly IADBHelper _adb;
    private readonly IVisionEngine _vision;
    private readonly Training _training;
    private Attacks _attacks;
    private readonly WallUpdater _wallUpdater;
    private readonly BuilderBaseNavigator _builderBaseNavigator;
    private readonly BuilderBaseResources _builderBaseResources;
    private readonly BuilderBaseReport _builderBaseReport;
    private readonly BuilderBaseArmyManager _builderBaseArmyManager;
    private readonly BuilderBaseAttacks _builderBaseAttacks;
    private readonly BuilderBaseClockTower _builderBaseClockTower;
    private readonly BuilderBaseWallUpdater _builderBaseWallUpdater;
    private readonly BuilderBaseMaintenance _builderBaseMaintenance;
    private readonly string _templatesPath;
    private readonly string _configPath;

    private readonly IConfigService _configService;
    private readonly StatsRepository _stats;
    private readonly PopupHandlerService _popups;
    private readonly ZoomService _zoom;
    private readonly AccountSwitcher _accounts;

    private readonly HomeBaseDetector _homeDetector;
    private readonly ScoutingFlow _scouting;
    private readonly BattleCompletionWatcher _battleWatcher;
    private readonly HomeResourceCollector _collector;
    private readonly HomeWallUpgradeRunner _wallRunner;
    private readonly MainVillageCycleRunner _mainCycleRunner;
    private readonly BuilderBaseCycleRunner _builderBaseCycleRunner;
    private readonly AccountRotationLoop _accountLoop;

    private CancellationTokenSource? _cts;
    private Task? _workerTask;
    private readonly ManualResetEvent _pauseEvent = new(true);
    private volatile bool _isRunning;
    private int _cycleCount;
    private int _currentVillageIdx = 1;
    private volatile bool _fastAttackQueued;
    private bool _disposed;
    private DateTime _sessionStartedAt;
    private DateTime? _pauseStartedAt;
    private TimeSpan _pausedDuration = TimeSpan.Zero;
    private int _sessionBattlesCompleted;

    public CVAutomationFramework(string configPath = "Config/test_config.json")
        : this(CreateServices(configPath))
    {
    }

    private CVAutomationFramework((IConfigService Config, IADBHelper Adb, IVisionEngine Vision, string TemplatesPath) services)
        : this(services.Config, services.Adb, services.Vision, services.TemplatesPath)
    {
    }

    private CVAutomationFramework(IConfigService configService, IADBHelper adb, IVisionEngine vision, string templatesPath)
        : this("", configService, adb, vision, templatesPath,
              new StatsRepository(adb, vision, templatesPath),
              new PopupHandlerService(adb, vision, templatesPath),
              new ZoomService(adb),
              new AccountSwitcher(adb, vision, templatesPath, maxWait => true))
    {
    }

    internal CVAutomationFramework(string configPath, IConfigService configService,
        IADBHelper adb, IVisionEngine vision, string templatesPath)
        : this(configPath, configService, adb, vision, templatesPath,
              new StatsRepository(adb, vision, templatesPath),
              new PopupHandlerService(adb, vision, templatesPath),
              new ZoomService(adb),
              new AccountSwitcher(adb, vision, templatesPath, maxWait => true))
    {
    }

    private CVAutomationFramework(string configPath, IConfigService configService,
        IADBHelper adb, IVisionEngine vision, string templatesPath,
        StatsRepository stats, PopupHandlerService popups, ZoomService zoom, AccountSwitcher accounts)
    {
        _configPath = configPath;
        _configService = configService;
        _stats = stats;
        _popups = popups;
        _zoom = zoom;
        _accounts = accounts;

        _adb = adb;
        _adb.BeforeInputAction = null;
        _templatesPath = templatesPath;
        _vision = vision;
        _training = new Training(_adb, _templatesPath, _vision);
        _attacks = new Attacks(_adb, _vision, _templatesPath, CreateAttackDelayConfig(configService.Current.Advanced));
        _wallUpdater = new WallUpdater(_adb, _vision, _templatesPath);
        _builderBaseNavigator = new BuilderBaseNavigator(_adb, _vision);
        _builderBaseResources = new BuilderBaseResources(_adb, _vision, _builderBaseNavigator);
        _builderBaseReport = new BuilderBaseReport(_adb, _vision, _builderBaseNavigator);
        _builderBaseArmyManager = new BuilderBaseArmyManager(_adb, _vision, _builderBaseNavigator);
        _builderBaseAttacks = new BuilderBaseAttacks(_adb, _vision, _builderBaseNavigator);
        _builderBaseClockTower = new BuilderBaseClockTower(_adb, _vision, _builderBaseNavigator);
        _builderBaseWallUpdater = new BuilderBaseWallUpdater(_adb, _vision, _builderBaseNavigator);
        _builderBaseMaintenance = new BuilderBaseMaintenance(_adb, _vision, _builderBaseNavigator, _templatesPath);

        _homeDetector = new HomeBaseDetector(_adb, _vision, _popups);
        _scouting = new ScoutingFlow(_adb, _vision, _popups);
        _battleWatcher = new BattleCompletionWatcher(_adb, _vision, _popups);
        _collector = new HomeResourceCollector(_adb, _popups, _templatesPath);
        _wallRunner = new HomeWallUpgradeRunner(_wallUpdater, _configService, _stats);
        _mainCycleRunner = new MainVillageCycleRunner(_adb, _vision, _configService, _zoom, _popups, _training, _attacks, _stats, _homeDetector, _scouting, _battleWatcher, _collector, _wallRunner);
        _builderBaseCycleRunner = new BuilderBaseCycleRunner(_adb, _vision, _configService, _builderBaseNavigator, _builderBaseResources, _builderBaseReport, _builderBaseArmyManager, _builderBaseAttacks, _builderBaseClockTower, _builderBaseWallUpdater, _stats, _templatesPath);
        _accountLoop = new AccountRotationLoop(_configService, _accounts, _wallUpdater);

        Console.WriteLine("[FSM-CS] phase=init status=success details=\"automation_core_initialized\"");
    }

    private static (IConfigService, IADBHelper, IVisionEngine, string) CreateServices(string configPath)
    {
        var config = new ConfigService(configPath);
        DeviceConnectionConfig devConfig = config.Current.DeviceConnection;
        var adb = new ADBHelper(devConfig.Host, devConfig.Port, devConfig.Serial);
        string templatesPath = Path.Combine(AppContext.BaseDirectory, "assets", "Templates");
        var vision = new VisionEngine(templatesPath);
        return (config, adb, vision, templatesPath);
    }

    private void LoadConfig(string path) => _configService.Reload();

    public void Start()
    {
        if (_isRunning) return;

        _configService.Reload();
        _attacks = new Attacks(_adb, _vision, _templatesPath, CreateAttackDelayConfig(_configService.Current.Advanced));

        _isRunning = true;
        _fastAttackQueued = false;
        _sessionStartedAt = DateTime.Now;
        _pauseStartedAt = null;
        _pausedDuration = TimeSpan.Zero;
        _sessionBattlesCompleted = 0;
        _cts = new CancellationTokenSource();
        _pauseEvent.Set();

        _workerTask = Task.Run(() => StartWorker(_cts.Token));
        Console.WriteLine("[FSM-CS] phase=worker status=start details=\"automation_started\"");
    }

    private void StartWorker(CancellationToken token)
    {
        try
        {
            DeviceConnectionConfig devConfig = _configService.Current.DeviceConnection;
            if (!EmulatorBootstrapper.EnsureReady(_adb, devConfig.Host, devConfig.Port, devConfig.EmulatorType, devConfig.EmulatorPath, token, devConfig.EmulatorInstance))
            {
                _isRunning = false;
                return;
            }

            Console.WriteLine("[FSM-CS] phase=home_check status=start");
            BotLoop(token);
        }
        catch (OperationCanceledException)
        {
            Console.WriteLine("[FSM-CS] phase=worker status=cancelled");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[FSM-CS ERROR] phase=worker status=fail action=startup reason=\"{ex.Message}\"");
        }
        finally
        {
            _isRunning = false;
            _fastAttackQueued = false;
            Console.WriteLine("[FSM-CS] phase=worker status=stopped");
        }
    }

    public void Stop()
    {
        _isRunning = false;
        _fastAttackQueued = false;
        _cts?.Cancel();
        _pauseEvent.Set();
        Console.WriteLine("[FSM-CS] phase=worker status=stop_requested");
    }

    public Task Completion => _workerTask ?? Task.CompletedTask;

    public void Pause()
    {
        _pauseStartedAt = DateTime.Now;
        _pauseEvent.Reset();
        Console.WriteLine("[FSM-CS] phase=worker status=paused");
    }

    public void Resume()
    {
        if (_pauseStartedAt != null)
        {
            _pausedDuration += DateTime.Now - _pauseStartedAt.Value;
            _pauseStartedAt = null;
        }

        _pauseEvent.Set();
        Console.WriteLine("[FSM-CS] phase=worker status=resumed");
    }

    public void RunSingleCycleForTest(CancellationToken token) => RunCyclesForTest(1, token);

    public void RunCyclesForTest(int cycleLimit, CancellationToken token)
    {
        bool wasRunning = _isRunning;
        _isRunning = true;
        _pauseEvent.Set();
        _currentVillageIdx = 1;
        _cycleCount = 0;

        try
        {
            for (int i = 1; i <= cycleLimit && !CheckStop(token); i++)
            {
                Console.WriteLine($"[FSM-CS] phase=test_cycle status=pending cycle={i} max={cycleLimit}");
                OneCycle(token);
                if (i < cycleLimit && !CheckStop(token))
                {
                    InterruptibleSleep(_fastAttackQueued ? AutomationThresholds.FastAttackCycleDelayMs : AutomationThresholds.NormalCycleDelayMs, token);
                }
            }
        }
        finally
        {
            _isRunning = wasRunning;
        }
    }

    private bool CheckStop(CancellationToken token) => token.IsCancellationRequested || !_isRunning || CheckAutoStop();

    private bool CheckAutoStop()
    {
        if (!_isRunning) return true;

        RunSessionConfig session = _configService.Current.RunSession;
        if (session.StopAfterBattlesEnabled && session.StopAfterBattles > 0 && _sessionBattlesCompleted >= session.StopAfterBattles)
        {
            _isRunning = false;
            _cts?.Cancel();
            _pauseEvent.Set();
            Console.WriteLine($"[FSM-CS] phase=auto_stop status=triggered reason=battle_limit current={_sessionBattlesCompleted} limit={session.StopAfterBattles}");
            return true;
        }

        if (session.StopAfterMinutesEnabled && session.StopAfterMinutes > 0)
        {
            TimeSpan activeElapsed = DateTime.Now - _sessionStartedAt - _pausedDuration;
            if (_pauseStartedAt != null) activeElapsed -= DateTime.Now - _pauseStartedAt.Value;

            if (activeElapsed.TotalMinutes >= session.StopAfterMinutes)
            {
                _isRunning = false;
                _cts?.Cancel();
                _pauseEvent.Set();
                Console.WriteLine($"[FSM-CS] phase=auto_stop status=triggered reason=minute_limit elapsed_minutes={activeElapsed.TotalMinutes:F1} limit={session.StopAfterMinutes}");
                return true;
            }
        }

        return false;
    }

    private bool InterruptibleSleep(int milliseconds, CancellationToken token)
    {
        DateTime end = DateTime.Now.AddMilliseconds(milliseconds);
        while (DateTime.Now < end)
        {
            int remaining = Math.Min(500, Math.Max(1, (int)(end - DateTime.Now).TotalMilliseconds));
            if (ThreadingUtil.InterruptibleSleep(remaining, token) || !_isRunning) return true;
            _popups.HandleBlockingConnectionPopup("[WARN] Connection popup during wait → recover");
        }
        return false;
    }

    private void WaitIfPaused(CancellationToken token)
    {
        while (!_pauseEvent.WaitOne(100))
        {
            if (CheckStop(token)) break;
        }
    }

    public void OneCycle(CancellationToken token)
    {
        bool fastAttack = _fastAttackQueued;
        _mainCycleRunner.RunCycle(
            _currentVillageIdx,
            ref _cycleCount,
            ref fastAttack,
            ref _sessionBattlesCompleted,
            CheckStop,
            () => WaitIfPaused(token),
            InterruptibleSleep,
            BootRecovery,
            IsNightVillageMode,
            OneBuilderBaseCycle,
            RunDonateOnlyCycle,
            TryUseCakeIfConfigured,
            TryRequestTroopsIfConfigured,
            ShouldSmartSurrender,
            ExecuteSurrender,
            token);
        _fastAttackQueued = fastAttack;
    }

    private void OneBuilderBaseCycle(CancellationToken token)
    {
        _builderBaseCycleRunner.OneBuilderBaseCycle(
            _currentVillageIdx,
            ref _cycleCount,
            CheckStop,
            () => WaitIfPaused(token),
            InterruptibleSleep,
            EnsureBuilderBaseEntry,
            DismissBuilderBasePopups,
            token);
    }

    internal BuilderBaseReportSnapshot ReadDebouncedReport(
        string farmMode, bool trophyRangeEnabled, int minTrophy, int maxTrophy, bool haltOnGoldFull, bool haltOnElixirFull, CancellationToken token, out bool shouldStop, out string stopReason)
        => BuilderBaseStopPolicy.ReadDebouncedReport(() => _builderBaseReport.Read(), farmMode, trophyRangeEnabled, minTrophy, maxTrophy, haltOnGoldFull, haltOnElixirFull, token, InterruptibleSleep, out shouldStop, out stopReason);

    internal static BuilderBaseReportSnapshot ReadDebouncedReport(
        Func<BuilderBaseReportSnapshot> readReport, string farmMode, bool trophyRangeEnabled, int minTrophy, int maxTrophy, bool haltOnGoldFull, bool haltOnElixirFull, CancellationToken token, Func<int, CancellationToken, bool>? sleepFunc, out bool shouldStop, out string stopReason)
        => BuilderBaseStopPolicy.ReadDebouncedReport(readReport, farmMode, trophyRangeEnabled, minTrophy, maxTrophy, haltOnGoldFull, haltOnElixirFull, token, sleepFunc, out shouldStop, out stopReason);

    internal static bool ShouldStopBuilderBaseAttacks(
        string farmMode, BuilderBaseReportSnapshot report, bool trophyRangeEnabled, int minTrophy, int maxTrophy, bool haltOnGoldFull, bool haltOnElixirFull, out string reason)
        => BuilderBaseStopPolicy.ShouldStopBuilderBaseAttacks(farmMode, report, trophyRangeEnabled, minTrophy, maxTrophy, haltOnGoldFull, haltOnElixirFull, out reason);

    private void TryUpgradeWallsFromHome(CancellationToken token, string phase)
        => _wallRunner.TryUpgradeWallsFromHome(_currentVillageIdx, _cycleCount, maxWait => EnsureHomeBase(maxWait), token, phase);

    private void BotLoop(CancellationToken token)
    {
        bool fastAttack = _fastAttackQueued;
        _accountLoop.Run(
            ref _currentVillageIdx,
            ref fastAttack,
            ref _cycleCount,
            ref _sessionBattlesCompleted,
            CheckStop,
            () => WaitIfPaused(token),
            InterruptibleSleep,
            OneCycle,
            token);
        _fastAttackQueued = fastAttack;
    }

    private bool EnsureHomeBase(int maxWaitSeconds = 50, bool allowBootRecovery = true)
        => _homeDetector.EnsureHomeBase(InterruptibleSleep, BootRecovery, _cts?.Token ?? CancellationToken.None, maxWaitSeconds, allowBootRecovery);

    private bool DetectHomeBase(out string reason) => _homeDetector.DetectHomeBase(out reason);

    public void BootRecovery()
    {
        Console.WriteLine("[FSM-CS] phase=recovery status=start action=restart_app package=\"com.supercell.clashofclans\"");

        _adb.ExecuteShell("am force-stop com.supercell.clashofclans");
        _adb.ExecuteShell("monkey -p com.supercell.clashofclans -c android.intent.category.LAUNCHER 1");

        Console.WriteLine("[FSM-CS] phase=recovery status=pending action=wait_app_load");
        if (InterruptibleSleep(10000, _cts?.Token ?? CancellationToken.None)) return;

        Console.WriteLine("[FSM-CS] phase=recovery status=pending action=clear_popups");
        _adb.Tap(146, 487);
        _wallUpdater.ResetSavedOffset();
    }

    public void SaveDebugImage(Mat image, string fileName) => _stats.SaveDebugImage(image, fileName);

    public void ZoomOut() => _zoom.ZoomOut();

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Stop();
        _pauseEvent.Dispose();
        _cts?.Dispose();
    }

    private static AttackDelayConfig CreateAttackDelayConfig(CvAut.Configuration.AdvancedConfig adv) => new()
    {
        TroopDeployDelayMs = adv.TroopDeployDelayMs,
        RageSpellDelayMs = adv.RageSpellDelayMs,
        FreezeSpellDelayMs = adv.FreezeSpellDelayMs,
        GrandWardenAbilityDelayMs = adv.GrandWardenAbilityDelayMs
    };
}
