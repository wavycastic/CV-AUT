using System;
using System.IO;
using CvAut.Configuration;

namespace CvAut.Automation;

/// <summary>
/// The collaborators that the automation facade still needs to hold after start-up.
/// Everything else built by <see cref="AutomationCompositionRoot"/> is reachable
/// only through one of these objects.
/// </summary>
internal sealed record AutomationParts(
    Attacks Attacks,
    WallUpdater WallUpdater,
    BuilderBaseNavigator BuilderBaseNavigator,
    BuilderBaseReport BuilderBaseReport,
    HomeBaseDetector HomeDetector,
    HomeWallUpgradeRunner WallRunner,
    MainVillageCycleRunner MainCycleRunner,
    BuilderBaseCycleRunner BuilderBaseCycleRunner,
    AccountRotationLoop AccountLoop);

/// <summary>
/// Builds the automation object graph. This logic used to sit inside the
/// CVAutomationFramework constructor; keeping it here leaves the facade
/// responsible for lifecycle and delegation only.
/// </summary>
internal static class AutomationCompositionRoot
{
    /// <summary>Creates the shared services implied by a config file path.</summary>
    public static (IConfigService Config, IADBHelper Adb, IVisionEngine Vision, string TemplatesPath) CreateServices(string configPath)
    {
        var config = new ConfigService(configPath);
        DeviceConnectionConfig devConfig = config.Current.DeviceConnection;
        var adb = new ADBHelper(devConfig.Host, devConfig.Port, devConfig.Serial);
        string templatesPath = Path.Combine(AppContext.BaseDirectory, "assets", "Templates");
        var vision = new VisionEngine(templatesPath);
        return (config, adb, vision, templatesPath);
    }

    /// <summary>
    /// Creates the attack engine. The facade calls this again on every Start so that
    /// delay settings edited while the bot was stopped are picked up.
    /// </summary>
    public static Attacks CreateAttacks(IADBHelper adb, IVisionEngine vision, string templatesPath, AdvancedConfig advanced)
        => new(adb, vision, templatesPath, CreateAttackDelayConfig(advanced));

    /// <summary>Builds the full graph of automation collaborators.</summary>
    /// <param name="currentAttacks">
    /// Reads the facade's current attack engine. This has to stay a delegate: the
    /// field is replaced on every Start, and the cycle runners must observe the
    /// replacement instead of a snapshot taken here at construction time.
    /// </param>
    public static AutomationParts Build(
        IConfigService configService,
        IADBHelper adb,
        IVisionEngine vision,
        string templatesPath,
        StatsRepository stats,
        PopupHandlerService popups,
        ZoomService zoom,
        AccountSwitcher accounts,
        Func<Attacks> currentAttacks)
    {
        Attacks attacks = CreateAttacks(adb, vision, templatesPath, configService.Current.Advanced);
        var training = new Training(adb, templatesPath, vision);
        var wallUpdater = new WallUpdater(adb, vision, templatesPath);

        var builderBaseNavigator = new BuilderBaseNavigator(adb, vision);
        var builderBaseResources = new BuilderBaseResources(adb, vision, builderBaseNavigator);
        var builderBaseReport = new BuilderBaseReport(adb, vision, builderBaseNavigator);
        var builderBaseArmyManager = new BuilderBaseArmyManager(adb, vision, builderBaseNavigator);
        var builderBaseAttacks = new BuilderBaseAttacks(adb, vision, builderBaseNavigator);
        var builderBaseClockTower = new BuilderBaseClockTower(adb, vision, builderBaseNavigator);
        var builderBaseWallUpdater = new BuilderBaseWallUpdater(adb, vision, builderBaseNavigator);

        var homeDetector = new HomeBaseDetector(adb, vision, popups);
        var scouting = new ScoutingFlow(adb, vision, popups);
        var battleWatcher = new BattleCompletionWatcher(adb, vision, popups);
        var collector = new HomeResourceCollector(adb, popups, templatesPath);
        var wallRunner = new HomeWallUpgradeRunner(wallUpdater, configService, stats);

        var mainCycleRunner = new MainVillageCycleRunner(adb, vision, configService, zoom, popups, training, currentAttacks, stats, homeDetector, scouting, battleWatcher, collector, wallRunner);
        var builderBaseCycleRunner = new BuilderBaseCycleRunner(adb, vision, configService, builderBaseNavigator, builderBaseResources, builderBaseReport, builderBaseArmyManager, builderBaseAttacks, builderBaseClockTower, builderBaseWallUpdater, stats, templatesPath);
        var accountLoop = new AccountRotationLoop(configService, accounts, wallUpdater);

        return new AutomationParts(
            attacks,
            wallUpdater,
            builderBaseNavigator,
            builderBaseReport,
            homeDetector,
            wallRunner,
            mainCycleRunner,
            builderBaseCycleRunner,
            accountLoop);
    }

    private static AttackDelayConfig CreateAttackDelayConfig(AdvancedConfig adv) => new()
    {
        TroopDeployDelayMs = adv.TroopDeployDelayMs,
        RageSpellDelayMs = adv.RageSpellDelayMs,
        FreezeSpellDelayMs = adv.FreezeSpellDelayMs,
        GrandWardenAbilityDelayMs = adv.GrandWardenAbilityDelayMs
    };
}
