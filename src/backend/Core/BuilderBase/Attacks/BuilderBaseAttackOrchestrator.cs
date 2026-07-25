using System;
using System.Threading;

namespace CvAut
{
    internal sealed class BuilderBaseAttackOrchestrator
    {
        private readonly IADBHelper _adb;
        private readonly BuilderBaseNavigator _navigator;
        private readonly HeroAbilityController _heroController;
        private readonly TroopDeploymentExecutor _deploymentExecutor;
        private readonly AttackEntryFlow _entryFlow;
        private readonly ReturnHomeController _returnHomeController;
        private readonly BattleOutcomeWatcher _outcomeWatcher;

        public BuilderBaseAttackOrchestrator(
            IADBHelper adb,
            BuilderBaseNavigator navigator,
            HeroAbilityController heroController,
            TroopDeploymentExecutor deploymentExecutor,
            AttackEntryFlow entryFlow,
            ReturnHomeController returnHomeController,
            BattleOutcomeWatcher outcomeWatcher)
        {
            _adb = adb;
            _navigator = navigator;
            _heroController = heroController;
            _deploymentExecutor = deploymentExecutor;
            _entryFlow = entryFlow;
            _returnHomeController = returnHomeController;
            _outcomeWatcher = outcomeWatcher;
        }

        public BuilderBaseBattleResult RunDropTrophyAttack(CancellationToken token)
        {
            Console.WriteLine("[BB-ATTACK] phase=attack status=start mode=drop_trophy");
            if (!_navigator.IsOnBuilderBase())
            {
                Console.WriteLine("[BB-ATTACK] phase=attack status=skip reason=not_on_builder_base");
                return new(false, 0, 0, false);
            }

            if (!_entryFlow.WaitForAttackReady(token, "attack_entry", 3))
            {
                Console.WriteLine("[BB-ATTACK] phase=attack status=fail reason=attack_not_ready_after_retry");
                AttackDebugRecorder.CaptureDebugSnapshot(_adb, "attack_not_ready_after_retry");
                return new(false, 0, 0, false);
            }

            if (!_entryFlow.TapFirstVisible(BuilderBaseAttackLayout.OpenAttackTemplates, BuilderBaseAttackLayout.ButtonThreshold, BuilderBaseAttackLayout.HomeAttackButtonRoi, token, out string openTemplate))
            {
                Console.WriteLine("[BB-ATTACK] phase=open_attack status=fail reason=button_not_found");
                AttackDebugRecorder.CaptureDebugSnapshot(_adb, "open_attack_button_not_found");
                return new(false, 0, 0, false);
            }

            if (Sleep(1800, token)) return new(false, 0, 0, false);

            if (!_entryFlow.TapFirstVisible(BuilderBaseAttackLayout.StartBattleTemplates, BuilderBaseAttackLayout.ButtonThreshold, BuilderBaseAttackLayout.BattleButtonRoi, token, out string startTemplate))
            {
                Console.WriteLine("[BB-ATTACK] phase=start_battle status=fail reason=button_not_found");
                _entryFlow.CloseAttackPrep(token);
                return new(false, 0, 0, false);
            }

            if (!_entryFlow.ClickFindNowIfRequired(token))
            {
                _entryFlow.CloseAttackPrep(token);
                return new(false, 0, 0, false);
            }

            if (!_entryFlow.WaitCloudsAndEnemyVillage(token))
            {
                return new(false, 0, 0, false);
            }

            // Drop 1 troop then immediately surrender
            _adb.Tap(100, 750); // Tap troop slot
            if (Sleep(500, token)) return new(false, 0, 0, false);
            _adb.Tap(400, 450); // Drop troop on field
            if (Sleep(800, token)) return new(false, 0, 0, false);

            bool surrendered = _returnHomeController.ReturnHomeDropTrophyBB(token);
            Console.WriteLine($"[BB-ATTACK] phase=drop_trophy status=complete surrendered={surrendered}");
            return new(surrendered, 0, 0, false);
        }

        public BuilderBaseBattleResult RunSingleAttack(BuilderBaseBattleOptions options, CancellationToken token)
        {
            Console.WriteLine($"[BB-ATTACK] phase=attack status=start mode=full custom_order={options.UseCustomDropOrder} next_delay={options.NextTroopDelayMs} same_delay={options.SameTroopDelayMs} bomber={options.HandleBomber} hero_loop=true");
            _outcomeWatcher.ResetClanGamesChecks();
            _heroController.ResetCounters();

            if (!_navigator.IsOnBuilderBase())
            {
                Console.WriteLine("[BB-ATTACK] phase=attack status=skip reason=not_on_builder_base");
                return new(false, 0, 0, false);
            }

            if (!_entryFlow.WaitForAttackReady(token, "attack_entry", 3))
            {
                Console.WriteLine("[BB-ATTACK] phase=attack status=fail reason=attack_not_ready_after_retry");
                AttackDebugRecorder.CaptureDebugSnapshot(_adb, "attack_not_ready_after_retry");
                return new(false, 0, 0, false);
            }

            if (!_entryFlow.TapFirstVisible(BuilderBaseAttackLayout.OpenAttackTemplates, BuilderBaseAttackLayout.ButtonThreshold, BuilderBaseAttackLayout.HomeAttackButtonRoi, token, out string openTemplate))
            {
                Console.WriteLine("[BB-ATTACK] phase=open_attack status=fail reason=button_not_found");
                AttackDebugRecorder.CaptureDebugSnapshot(_adb, "open_attack_button_not_found");
                return new(false, 0, 0, false);
            }

            Console.WriteLine($"[BB-ATTACK] phase=open_attack status=success template=\"{openTemplate}\"");
            if (Sleep(1800, token)) return new(false, 0, 0, false);

            if (!_entryFlow.HasVisibleTroopsOnPrepScreen())
            {
                Console.WriteLine("[BB-ATTACK] phase=army_ready status=skip reason=troops_not_detected_on_prep_screen");
                _entryFlow.CloseAttackPrep(token);
                return new(false, 0, 0, false);
            }

            Console.WriteLine("[BB-ATTACK] phase=army_ready status=success");

            if (!_entryFlow.TapFirstVisible(BuilderBaseAttackLayout.StartBattleTemplates, BuilderBaseAttackLayout.ButtonThreshold, BuilderBaseAttackLayout.BattleButtonRoi, token, out string startTemplate))
            {
                Console.WriteLine("[BB-ATTACK] phase=start_battle status=retry reason=button_not_found action=wait_and_recheck");
                if (!_entryFlow.WaitForAttackReady(token, "start_battle", 2) || !_entryFlow.TapFirstVisible(BuilderBaseAttackLayout.StartBattleTemplates, BuilderBaseAttackLayout.ButtonThreshold, BuilderBaseAttackLayout.BattleButtonRoi, token, out startTemplate))
                {
                    Console.WriteLine("[BB-ATTACK] phase=start_battle status=fail reason=button_not_found_after_retry");
                    _entryFlow.CloseAttackPrep(token);
                    return new(false, 0, 0, false);
                }
            }

            Console.WriteLine($"[BB-ATTACK] phase=start_battle status=success template=\"{startTemplate}\"");
            if (!_entryFlow.ClickFindNowIfRequired(token))
            {
                _entryFlow.CloseAttackPrep(token);
                return new(false, 0, 0, false);
            }

            if (!_entryFlow.WaitCloudsAndEnemyVillage(token))
            {
                Console.WriteLine("[BB-ATTACK] phase=cloud status=fail reason=enemy_village_not_detected action=abort_attack");
                _entryFlow.CloseAttackPrep(token);
                return new(false, 0, 0, false);
            }

            if (_entryFlow.DetectObstructedLayout())
            {
                Console.WriteLine("[BB-ATTACK] phase=obstructed status=warning action=continue_with_safe_drop_points");
            }

            _outcomeWatcher.ZoomOutBattleView(token, "initial_attack");

            _deploymentExecutor.DeployAllVisibleTroops(options, token, secondAttack: false);
            BuilderBaseBattleResult result = _outcomeWatcher.WaitBattleAndReturn(options, token, _deploymentExecutor);

            bool returned = _navigator.IsOnBuilderBase();
            if (!returned && result.ReturnedHome)
            {
                Console.WriteLine("[BB-ATTACK] phase=attack status=pending action=verify_return_home reason=result_handled_but_builder_base_not_detected");
                returned = _returnHomeController.ReturnHomeDropTrophyBB(token);
            }
            Console.WriteLine($"[BB-ATTACK] phase=attack status={(returned ? "success" : "warning")} return_handled={result.ReturnedHome} returned_builder_base={returned} damage={result.Damage} stars={result.Stars} stage2={result.Stage2Entered}");
            return result with { ReturnedHome = returned };
        }

        private static bool Sleep(int milliseconds, CancellationToken token) => token.WaitHandle.WaitOne(milliseconds);
    }
}
