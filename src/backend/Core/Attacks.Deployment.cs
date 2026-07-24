using System;
using System.Threading;
using CvAut.AttackPipelines;

namespace CvAut
{
    internal partial class Attacks : IAttackStageOperations
    {
        private AttackPipeline? _attackPipeline;

        private AttackPipeline Pipeline
            => _attackPipeline ??= new AttackPipeline(this);

        public bool RunPipeline(
            string attackStrategy = "Dragon_Attack",
            CancellationToken token = default,
            bool useEventTroops = false)
        {
            var context = new AttackContext(attackStrategy, useEventTroops, token);
            AttackStageResult result = Pipeline.Execute(context);
            return result.Status == AttackStageStatus.Succeeded;
        }

        AttackStageResult IAttackStageOperations.Prepare(AttackContext context)
        {
            if (context.IsCancellationRequested)
                return AttackStageResult.Cancelled();

            string[] directions = { "top_left", "top_right" };
            _attackDirection = directions[_rand.Next(directions.Length)];
            _side = _attackDirection.EndsWith("left", StringComparison.OrdinalIgnoreCase)
                ? "left"
                : "right";
            InitializePatterns();

            bool electroDragon = context.NormalizedStrategy == "ElectroDragon_Attack";
            _scanElectroDragonTab = electroDragon;
            _requiredTabs.Clear();
            foreach (string key in electroDragon
                ? new[] { "e_drag", "balloon", "rage", "freeze" }
                : new[] { "dragon", "balloon", "rage", "freeze" })
            {
                _requiredTabs.Add(key);
            }

            if (_deployCoords.ContainsKey("siege_machine"))
                _requiredTabs.Add("siege_machine");

            Console.WriteLine(
                $"[ATTACK-CS] phase=prepare status=start strategy=\"{context.RequestedStrategy}\" " +
                $"normalized_strategy=\"{context.NormalizedStrategy}\" side=\"{_side.ToUpperInvariant()}\" " +
                $"direction=\"{_attackDirection}\"");
            UpdateTabs();
            return AttackStageResult.Success();
        }

        AttackStageResult IAttackStageOperations.DeployTroops(AttackContext context)
        {
            if (context.IsCancellationRequested)
                return AttackStageResult.Cancelled();

            string primaryTroop = context.NormalizedStrategy == "ElectroDragon_Attack"
                ? "e_drag"
                : "dragon";

            DeployTroops(primaryTroop, context.CancellationToken);
            if (context.IsCancellationRequested) return AttackStageResult.Cancelled();

            DeployIfPresent("ice_minion", context.CancellationToken);
            DeployIfPresent("ice_golem", context.CancellationToken);
            DeployIfPresent("azure_dragon", context.CancellationToken);
            DeployConfiguredEventTroops(context.UseEventTroops, context.CancellationToken);
            if (context.IsCancellationRequested) return AttackStageResult.Cancelled();

            DeployTroops("balloon", context.CancellationToken);
            if (context.IsCancellationRequested) return AttackStageResult.Cancelled();

            DeployTroops("siege_machine", context.CancellationToken);
            return context.IsCancellationRequested
                ? AttackStageResult.Cancelled()
                : AttackStageResult.Success();
        }

        AttackStageResult IAttackStageOperations.DeploySpells(AttackContext context)
        {
            if (context.IsCancellationRequested)
                return AttackStageResult.Cancelled();

            if (InterruptibleSleep(SpellPhaseDelayMs, context.CancellationToken))
                return AttackStageResult.Cancelled();

            DeploySpells("rage_initial", context.CancellationToken);
            DeploySpells("freeze", context.CancellationToken);
            DeploySpells("rage_remaining", context.CancellationToken);
            return context.IsCancellationRequested
                ? AttackStageResult.Cancelled()
                : AttackStageResult.Success();
        }

        AttackStageResult IAttackStageOperations.ActivateHeroes(AttackContext context)
        {
            if (context.IsCancellationRequested)
                return AttackStageResult.Cancelled();

            DeployHeroes(context.CancellationToken);
            if (context.IsCancellationRequested)
                return AttackStageResult.Cancelled();

            if (InterruptibleSleep(
                _delays.GrandWardenAbilityDelayMs,
                context.CancellationToken))
            {
                return AttackStageResult.Cancelled();
            }

            RetapHeroes(context.CancellationToken);
            return context.IsCancellationRequested
                ? AttackStageResult.Cancelled()
                : AttackStageResult.Success();
        }

        AttackStageResult IAttackStageOperations.Complete(AttackContext context)
        {
            if (context.IsCancellationRequested)
                return AttackStageResult.Cancelled();

            string primaryTroop = context.NormalizedStrategy == "ElectroDragon_Attack"
                ? "e_drag"
                : "dragon";
            EnsureTroopFullyDeployed(primaryTroop, context.CancellationToken);
            return context.IsCancellationRequested
                ? AttackStageResult.Cancelled()
                : AttackStageResult.Success();
        }

        public bool DeployTroopsWithStrategy(string strategyName, CancellationToken token)
        {
            if (token.IsCancellationRequested) return false;
            Console.WriteLine($"[ATTACKS] phase=deploy_strategy status=start strategy={strategyName}");
            var strategy = new StandardBarchStrategy();
            strategy.Execute(_adb, _vision, token);
            return !token.IsCancellationRequested;
        }
    }
}
