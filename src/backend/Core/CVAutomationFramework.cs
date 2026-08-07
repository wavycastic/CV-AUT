using System;
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
    private Attacks _attacks;
    private readonly WallUpdater _wallUpdater;
    private readonly BuilderBaseNavigator _builderBaseNavigator;
    private readonly BuilderBaseReport _builderBaseReport;
    private readonly string _templatesPath;
    private readonly IConfigService _configService;
    private readonly StatsRepository _stats;
    private readonly PopupHandlerService _popups;
    private readonly ZoomService _zoom;
    private readonly HomeBaseDetector _homeDetector;
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
    private int _sessionBattlesCompleted;
    private volatile bool _fastAttackQueued;
    private bool _disposed;
    private DateTime _sessionStartedAt;
    private DateTime? _pauseStartedAt;
    private TimeSpan _pausedDuration = TimeSpan.Zero;

    public CVAutomationFramework(string configPath = "Config/test_config.json")
        : this(AutomationCompositionRoot.CreateServices(configPath))
    {
    }

    private CVAutomationFramework((IConfigService Config, IADBHelper Adb, IVisionEngine Vision, string TemplatesPath) services)
        : this(services.Config, services.Adb, services.Vision, services.TemplatesPath)
    {
    }

    private CVAutomationFramework(IConfigService configService, IADBHelper adb, IVisionEngine vision, string templatesPath)
        : this(configService, adb, vision, templatesPath,
              new StatsRepository(adb, vision, templatesPath),
              new PopupHandlerService(adb, vision, templatesPath),
              new ZoomService(adb),
              new AccountSwitcher(adb, vision, templatesPath, maxWait => true))
    {
    }

    internal CVAutomationFramework(string configPath, IConfigService configService, IADBHelper adb, IVisionEngine vision, string templatesPath)
        : this(configService, adb, vision, templatesPath,
              new StatsRepository(adb, vision, templatesPath),
              new PopupHandlerService(adb, vision, templatesPath),
              new ZoomService(adb),
              new AccountSwitcher(adb, vision, templatesPath, maxWait => true))
    {
    }

    private CVAutomationFramework(
        IConfigService configService,
        IADBHelper adb,
        IVisionEngine vision,
        string templatesPath,
        StatsRepository stats,
        PopupHandlerService popups,
        ZoomService zoom,
        AccountSwitcher accounts)
    {
        _configService = configService;
        _stats = stats;
        _popups = popups;
        _zoom = zoom;
        _adb = adb;
        _adb.BeforeInputAction = null;
        _templatesPath = templatesPath;
        _vision = vision;

        // _attacks is assigned a few lines below and the delegate is only invoked
        // from RunCycle, so the capture can never observe the unassigned field.
        AutomationParts parts = AutomationCompositionRoot.Build(
            configService, adb, vision, templatesPath, stats, popups, zoom, accounts, () => _attacks!);

        _attacks = parts.Attacks;
        _wallUpdater = parts.WallUpdater;
        _builderBaseNavigator = parts.BuilderBaseNavigator;
        _builderBaseReport = parts.BuilderBaseReport;
        _homeDetector = parts.HomeDetector;
        _wallRunner = parts.WallRunner;
        _mainCycleRunner = parts.MainCycleRunner;
        _builderBaseCycleRunner = parts.BuilderBaseCycleRunner;
        _accountLoop = parts.AccountLoop;

        Console.WriteLine("[FSM-CS] phase=init status=success details=\"automation_core_initialized\"");
    }

    public void Start()
    {
        if (_isRunning) return;

        _configService.Reload();
        _attacks = AutomationCompositionRoot.CreateAttacks(_adb, _vision, _templatesPath, _configService.Current.Advanced);

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
            if (_pauseStartedAt != null)
            {
                activeElapsed -= DateTime.Now - _pauseStartedAt.Value;
            }

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
        _mainCycleRunner.RunCycle(
            _currentVillageIdx,
            ref _cycleCount,
            () => _fastAttackQueued,
            val => _fastAttackQueued = val,
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

    private void BotLoop(CancellationToken token)
    {
        _accountLoop.Run(
            ref _currentVillageIdx,
            () => _fastAttackQueued,
            val => _fastAttackQueued = val,
            ref _cycleCount,
            ref _sessionBattlesCompleted,
            CheckStop,
            () => WaitIfPaused(token),
            InterruptibleSleep,
            OneCycle,
            token);
    }

    private bool EnsureHomeBase(int maxWaitSeconds = 50, bool allowBootRecovery = true)
        => _homeDetector.EnsureHomeBase(InterruptibleSleep, BootRecovery, _cts?.Token ?? CancellationToken.None, maxWaitSeconds, allowBootRecovery);

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
}
