using System;
using System.Collections.Generic;
using System.Threading;
using OpenCvSharp;
using Point = OpenCvSharp.Point;

namespace CvAut
{
    internal partial class BuilderBaseAttacks
    {
        private readonly BuilderBaseAttackOrchestrator _orchestrator;

        public BuilderBaseAttacks(IADBHelper adb, IVisionEngine vision, BuilderBaseNavigator navigator)
        {
            Random random = new();
            HeroAbilityController heroController = new(adb, vision);
            AttackBarScanner barScanner = new(adb, vision, heroController);
            TroopDeploymentExecutor deploymentExecutor = new(adb, vision, random, barScanner, heroController);
            AttackEntryFlow entryFlow = new(adb, vision, navigator);

            BattleOutcomeWatcher? outcomeWatcher = null;
            ReturnHomeController returnHomeController = new(adb, vision, navigator, entryFlow, () => outcomeWatcher?.IsBBAttackPage() ?? false);
            outcomeWatcher = new BattleOutcomeWatcher(adb, vision, navigator, heroController, barScanner, returnHomeController, entryFlow);

            _orchestrator = new BuilderBaseAttackOrchestrator(
                adb,
                navigator,
                heroController,
                deploymentExecutor,
                entryFlow,
                returnHomeController,
                outcomeWatcher);
        }

        public BuilderBaseBattleResult RunDropTrophyAttack(CancellationToken token)
            => _orchestrator.RunDropTrophyAttack(token);

        public bool RunSingleAttack(CancellationToken token)
            => RunSingleAttack(DropOrderPolicy.DefaultOptions(), token).ReturnedHome;

        public BuilderBaseBattleResult RunSingleAttack(BuilderBaseBattleOptions options, CancellationToken token)
            => _orchestrator.RunSingleAttack(options, token);

        internal static IEnumerable<BuilderBaseTroopSlot> OrderSlots(List<BuilderBaseTroopSlot> slots, BuilderBaseBattleOptions options)
            => DropOrderPolicy.OrderSlots(slots, options);

        internal static Point ScaleMbrPoint(int x, int y, int imageWidth, int imageHeight)
            => MbrScreenScaling.ScaleMbrPoint(x, y, imageWidth, imageHeight);
    }
}
