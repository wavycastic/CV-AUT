using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using CvAut.Configuration;

namespace CvAut.Automation;

internal sealed class BuilderBaseCycleRunner
{
    private static bool s_loggedBuilderBaseAssetAudit;

    private readonly IADBHelper _adb;
    private readonly IVisionEngine _vision;
    private readonly IConfigService _configService;
    private readonly BuilderBaseNavigator _builderBaseNavigator;
    private readonly BuilderBaseResources _builderBaseResources;
    private readonly BuilderBaseReport _builderBaseReport;
    private readonly BuilderBaseArmyManager _builderBaseArmyManager;
    private readonly BuilderBaseAttacks _builderBaseAttacks;
    private readonly BuilderBaseClockTower _builderBaseClockTower;
    private readonly BuilderBaseWallUpdater _builderBaseWallUpdater;
    private readonly StatsRepository _stats;
    private readonly string _templatesPath;

    public BuilderBaseCycleRunner(
        IADBHelper adb,
        IVisionEngine vision,
        IConfigService configService,
        BuilderBaseNavigator builderBaseNavigator,
        BuilderBaseResources builderBaseResources,
        BuilderBaseReport builderBaseReport,
        BuilderBaseArmyManager builderBaseArmyManager,
        BuilderBaseAttacks builderBaseAttacks,
        BuilderBaseClockTower builderBaseClockTower,
        BuilderBaseWallUpdater builderBaseWallUpdater,
        StatsRepository stats,
        string templatesPath)
    {
        _adb = adb;
        _vision = vision;
        _configService = configService;
        _builderBaseNavigator = builderBaseNavigator;
        _builderBaseResources = builderBaseResources;
        _builderBaseReport = builderBaseReport;
        _builderBaseArmyManager = builderBaseArmyManager;
        _builderBaseAttacks = builderBaseAttacks;
        _builderBaseClockTower = builderBaseClockTower;
        _builderBaseWallUpdater = builderBaseWallUpdater;
        _stats = stats;
        _templatesPath = templatesPath;
    }

    public void OneBuilderBaseCycle(
        int currentVillageIdx,
        ref int cycleCount,
        Func<CancellationToken, bool> checkStopFunc,
        Action waitIfPausedFunc,
        Func<int, CancellationToken, bool> interruptibleSleepFunc,
        Func<CancellationToken, bool> ensureBuilderBaseEntryFunc,
        Action<CancellationToken> dismissPopupsFunc,
        CancellationToken token)
    {
        waitIfPausedFunc();
        if (checkStopFunc(token)) return;

        Console.WriteLine($"[DEBUG][BB-CS] phase=cycle status=start village={currentVillageIdx}");

        if (!ensureBuilderBaseEntryFunc(token))
        {
            Console.WriteLine("[BB-CS] phase=cycle status=fail reason=switch_to_builder_base_failed");
            return;
        }

        waitIfPausedFunc();
        if (checkStopFunc(token)) return;

        NightVillageConfig night = _configService.Current.NightVillage;
        string farmMode = night.FarmMode;
        bool forceAttackForClanGames = false;
        bool trophyRangeEnabled = false;
        int minTrophy = Math.Clamp(night.MinCups, 0, 10000);
        int maxTrophy = Math.Clamp(night.MaxCups, 0, 10000);
        bool haltOnGoldFull = false;
        bool haltOnElixirFull = false;
        bool upgradeWall = night.UpgradeWall;
        bool enableAttack = night.EnableAttack;
        bool boostClockTower = night.BoostClockTower;
        int maxAttacksPerCycle = Math.Clamp(night.MaxAttacksPerCycle, 1, 100);
        var armyOptions = new BuilderBaseArmyOptions(
            Enabled: night.ArmyManagement,
            Formation: night.ArmyFormation,
            RequireHero: night.WaitForHeroes);
        var battleOptions = new BuilderBaseBattleOptions(
            DropOrder: night.DropOrder,
            UseCustomDropOrder: night.CustomDropOrderEnabled,
            NextTroopDelayMs: night.NextTroopDelayMs,
            SameTroopDelayMs: night.SameTroopDelayMs,
            HandleBomber: night.HandleBomber);
        var maintenanceOptions = new BuilderBaseMaintenanceOptions(
            SuggestedUpgrades: false,
            StarLaboratory: false,
            UpgradeBattleMachine: false,
            UpgradeBattleCopter: false,
            PlaceNewBuildings: false,
            IgnoreGoldUpgrades: false,
            IgnoreElixirUpgrades: false,
            IgnoreHallUpgrades: true,
            IgnoreWallUpgrades: true,
            StarLaboratoryTroop: "auto",
            VillageIdx: currentVillageIdx,
            StarLaboratoryDebugScreenshots: false);

        LogBuilderBaseBaselineAssetAudit(armyOptions, battleOptions, maintenanceOptions, boostClockTower, upgradeWall);

        Console.WriteLine($"[BB-CS] phase=cycle status=pending step=collect upgrade_wall={upgradeWall} enable_attack={enableAttack} boost_clock_tower={boostClockTower} trophy_range={trophyRangeEnabled} min_trophy={minTrophy} max_trophy={maxTrophy} halt_gold_full={haltOnGoldFull} halt_elixir_full={haltOnElixirFull} force_clan_games={forceAttackForClanGames} suggested_upgrades={maintenanceOptions.SuggestedUpgrades} star_laboratory={maintenanceOptions.StarLaboratory} hero_upgrades={maintenanceOptions.UpgradeBattleMachine || maintenanceOptions.UpgradeBattleCopter} army_management={armyOptions.Enabled} army_formation={armyOptions.Formation}  custom_drop_order={battleOptions.UseCustomDropOrder}");
        BuilderBaseReportSnapshot beforeReport = _builderBaseReport.Read();
        Console.WriteLine($"[BB-CS] phase=cycle status=pending step=report_before gold={beforeReport.Gold} elixir={beforeReport.Elixir} trophy={beforeReport.Trophy} free_builders={beforeReport.FreeBuilders} total_builders={beforeReport.TotalBuilders} builder_hall_level={beforeReport.BuilderHallLevel} loot_available={beforeReport.LootAvailable} remaining_stars={beforeReport.RemainingStars} max_stars={beforeReport.MaxStars} gold_storage_full={beforeReport.GoldStorageFull} elixir_storage_full={beforeReport.ElixirStorageFull}");
        int collected = _builderBaseResources.Collect(token);
        Console.WriteLine($"[BB-CS] phase=cycle status=pending step=collect_resources taps={collected}");

        if (boostClockTower)
        {
            bool boosted = _builderBaseClockTower.TryBoost(token);
            Console.WriteLine($"[BB-CS] phase=cycle status=pending step=clock_tower_boost success={boosted}");
        }
        if (upgradeWall && !checkStopFunc(token))
        {
            bool wallUpgraded = _builderBaseWallUpdater.TryUpgradeOne(token);
            Console.WriteLine($"[BB-CS] phase=cycle status=pending step=wall_upgrade_done success={wallUpgraded}");
            _stats.UpdateWallStats(currentVillageIdx, wallUpgraded ? 1 : 0);
        }

        if (!checkStopFunc(token)
            && (maintenanceOptions.SuggestedUpgrades || maintenanceOptions.StarLaboratory
                || maintenanceOptions.UpgradeBattleMachine || maintenanceOptions.UpgradeBattleCopter))
        {
            Console.WriteLine("[BB-CS] phase=cycle status=pending step=maintenance_skipped reason=temporary_scope_attack_and_wall_only");
        }

        if (!checkStopFunc(token))
        {
            BuilderBaseReportSnapshot afterMaintenanceReport = _builderBaseReport.Read();
            Console.WriteLine($"[BB-CS] phase=cycle status=pending step=report_after_maintenance gold={afterMaintenanceReport.Gold} elixir={afterMaintenanceReport.Elixir} trophy={afterMaintenanceReport.Trophy} free_builders={afterMaintenanceReport.FreeBuilders} total_builders={afterMaintenanceReport.TotalBuilders} builder_hall_level={afterMaintenanceReport.BuilderHallLevel} loot_available={afterMaintenanceReport.LootAvailable} remaining_stars={afterMaintenanceReport.RemainingStars} max_stars={afterMaintenanceReport.MaxStars} gold_storage_full={afterMaintenanceReport.GoldStorageFull} elixir_storage_full={afterMaintenanceReport.ElixirStorageFull}");
        }

        if (enableAttack && !checkStopFunc(token))
        {
            int completedAttacks = 0;
            int attempts = 0;
            int consecutiveFailures = 0;
            const int maxConsecutiveFailures = 3;
            for (int attack = 1; attack <= maxAttacksPerCycle && !checkStopFunc(token); attack++)
            {
                BuilderBaseReportSnapshot attackReport;
                if (forceAttackForClanGames)
                {
                    attackReport = _builderBaseReport.Read();
                    Console.WriteLine($"[BB-CS] phase=prepare_attack status=force_clan_games index={attack} loot_available={attackReport.LootAvailable} remaining_stars={attackReport.RemainingStars} max_stars={attackReport.MaxStars} gold_storage_full={attackReport.GoldStorageFull} elixir_storage_full={attackReport.ElixirStorageFull}");
                }
                else
                {
                    attackReport = BuilderBaseStopPolicy.ReadDebouncedReport(
                        () => _builderBaseReport.Read(),
                        farmMode, trophyRangeEnabled, minTrophy, maxTrophy, haltOnGoldFull, haltOnElixirFull, token, interruptibleSleepFunc, out bool shouldStop, out string stopReason);
                    if (shouldStop || checkStopFunc(token))
                    {
                        if (shouldStop)
                            Console.WriteLine($"[BB-CS] phase=prepare_attack status=skip index={attack} reason={stopReason} attack_avail={attackReport.AttackAvailable} attack_known={attackReport.AttackAvailabilityKnown} star_bonus_avail={attackReport.StarBonusAvailable} remaining_stars={attackReport.RemainingStars} max_stars={attackReport.MaxStars} trophy={attackReport.Trophy} min={minTrophy} max={maxTrophy} gold_storage_full={attackReport.GoldStorageFull} elixir_storage_full={attackReport.ElixirStorageFull} report_reliable={attackReport.Reliable}");
                        break;
                    }
                }

                attempts++;

                bool isDropTrophy = farmMode.Equals("drop_trophy", StringComparison.OrdinalIgnoreCase);
                if (!isDropTrophy)
                {
                    if (!_builderBaseArmyManager.EnsureReadyForAttack(armyOptions, token))
                    {
                        Console.WriteLine($"[BB-CS] phase=cycle status=pending step=army_not_ready index={attack}");
                        break;
                    }
                }

                BuilderBaseBattleResult battleResult = isDropTrophy
                    ? _builderBaseAttacks.RunDropTrophyAttack(token)
                    : _builderBaseAttacks.RunSingleAttack(battleOptions, token);
                Console.WriteLine($"[BB-CS] phase=cycle status=pending step=attack_done index={attack} success={battleResult.ReturnedHome} damage={battleResult.Damage} stars={battleResult.Stars} stage2={battleResult.Stage2Entered}");
                bool counted = battleResult.ReturnedHome;
                if (counted)
                {
                    _stats.UpdateBuilderBaseAttackStats(currentVillageIdx, battleResult);
                    completedAttacks++;
                    consecutiveFailures = 0;
                }
                else
                {
                    consecutiveFailures++;
                    Console.WriteLine($"[BB-CS] phase=cycle status=pending step=attack_not_counted index={attack} reason=abort_or_return_home_failed consecutive_failures={consecutiveFailures} max_allowed={maxConsecutiveFailures}");
                }

                if (!PostBuilderBaseAttackMaintenance(dismissPopupsFunc, ensureBuilderBaseEntryFunc, interruptibleSleepFunc, token, battleResult.ReturnedHome))
                {
                    Console.WriteLine($"[BB-CS] phase=cycle status=fail step=post_attack_maintenance index={attack} reason=builder_base_recovery_failed");
                    break;
                }

                if (consecutiveFailures >= maxConsecutiveFailures)
                {
                    Console.WriteLine($"[BB-CS] phase=cycle status=pending step=attack_stop reason=consecutive_attack_failures limit={maxConsecutiveFailures}");
                    break;
                }
            }
            if (attempts >= maxAttacksPerCycle && !checkStopFunc(token))
            {
                Console.WriteLine($"[BB-CS] phase=cycle status=pending step=attack_stop reason=max_attacks_per_cycle limit={maxAttacksPerCycle}");
            }
            Console.WriteLine($"[BB-CS] phase=cycle status=pending step=attacks_complete completed={completedAttacks} attempts={attempts}");
        }

        cycleCount++;
        Console.WriteLine($"[BB-CS] phase=cycle status=success village={currentVillageIdx}");
    }

    private bool PostBuilderBaseAttackMaintenance(
        Action<CancellationToken> dismissPopupsFunc,
        Func<CancellationToken, bool> ensureBuilderBaseEntryFunc,
        Func<int, CancellationToken, bool> interruptibleSleepFunc,
        CancellationToken token,
        bool returnedHome)
    {
        Console.WriteLine($"[BB-CS] phase=post_attack status=start returned_home={returnedHome}");
        dismissPopupsFunc(token);

        if (!_builderBaseNavigator.IsOnBuilderBase())
        {
            Console.WriteLine("[BB-CS] phase=post_attack status=pending step=recover_builder_base reason=not_on_builder_base");
            if (!ensureBuilderBaseEntryFunc(token)) return false;
        }

        dismissPopupsFunc(token);
        _builderBaseNavigator.ZoomOutApprox(token);
        if (interruptibleSleepFunc(700, token)) return false;

        dismissPopupsFunc(token);
        _builderBaseNavigator.ZoomOutApprox(token);
        bool ok = _builderBaseNavigator.IsOnBuilderBase();
        Console.WriteLine($"[BB-CS] phase=post_attack status={(ok ? "success" : "fail")} step=verify_builder_base");
        return ok;
    }

    private void LogBuilderBaseBaselineAssetAudit(
        BuilderBaseArmyOptions armyOptions,
        BuilderBaseBattleOptions battleOptions,
        BuilderBaseMaintenanceOptions maintenanceOptions,
        bool boostClockTower,
        bool upgradeWall)
    {
        if (s_loggedBuilderBaseAssetAudit) return;
        s_loggedBuilderBaseAssetAudit = true;

        var required = new SortedSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            @"ui\switch_builder",
            @"ui\game_setting",
            @"ui\shop",
            @"ui\builder_available",
            @"ui\x_night",
            @"resources\gold_collector",
            @"resources\elixir_collector",
            @"resources\collect",
            @"ui\attack_button",
            @"ui\start_battle",
            @"ui\return_home",
            @"ui\surrender_button"
        };

        if (armyOptions.Enabled || battleOptions.UseCustomDropOrder)
        {
            required.UnionWith(new[]
            {
                @"troops\builder_base\raged_barbarian",
                @"troops\builder_base\raged_barbarian_click",
                @"troops\builder_base\power_pekka",
                @"troops\builder_base\power_pekka_click",
                @"heroes\battle_machine",
                @"heroes\battle_machine_a"
            });
        }

        if (boostClockTower)
        {
            required.UnionWith(new[] { @"ui\clock_available", @"ui\free_boost", @"ui\boost" });
        }

        if (upgradeWall)
        {
            required.UnionWith(new[] { @"walls\wall", @"ui\icon_wall" });
        }

        if (maintenanceOptions.SuggestedUpgrades || maintenanceOptions.UpgradeBattleMachine || maintenanceOptions.UpgradeBattleCopter)
        {
            required.UnionWith(new[] { @"ui\builder_available", @"ui\open_upgrade", @"ui\icon_up", @"resources\gold", @"resources\elixir" });
        }

        if (maintenanceOptions.StarLaboratory)
        {
            required.UnionWith(new[] { @"builder_base\star_laboratory", @"ui\laboratory", @"ui\research" });
        }

        string[] missing = required.Where(template => !TemplateAssetLoader.Exists(_templatesPath, template)).ToArray();
        if (missing.Length == 0)
        {
            Console.WriteLine($"[BB-CS] phase=asset_audit status=success checked={required.Count}");
            return;
        }

        Console.WriteLine($"[BB-CS WARNING] phase=asset_audit status=partial checked={required.Count} missing={missing.Length} templates=\"{string.Join(",", missing)}\" action=skip_template_dependent_steps");
    }
}
