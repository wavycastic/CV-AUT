using System;
using System.Collections.Generic;

namespace CvAut.AttackPipelines;

internal sealed class AttackPipeline
{
    private readonly IReadOnlyList<IAttackStage> _stages;

    public AttackPipeline(IAttackStageOperations operations)
        : this(new IAttackStage[]
        {
            new AttackPreparationStage(operations),
            new TroopDeploymentStage(operations),
            new SpellDeploymentStage(operations),
            new HeroAbilityStage(operations),
            new BattleCompletionStage(operations)
        })
    {
    }

    internal AttackPipeline(IReadOnlyList<IAttackStage> stages)
    {
        _stages = stages ?? throw new ArgumentNullException(nameof(stages));
    }

    public AttackStageResult Execute(AttackContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        foreach (IAttackStage stage in _stages)
        {
            if (context.IsCancellationRequested)
            {
                context.FailureReason = "cancelled";
                return AttackStageResult.Cancelled();
            }

            context.CurrentStage = stage.Name;
            Console.WriteLine($"[ATTACK-CS] phase=pipeline stage={stage.Name} status=start strategy={context.NormalizedStrategy}");

            AttackStageResult result;
            try
            {
                result = stage.Execute(context);
            }
            catch (OperationCanceledException)
            {
                result = AttackStageResult.Cancelled();
            }
            catch (Exception ex)
            {
                result = AttackStageResult.Fail(ex.Message);
            }

            if (!result.CanContinue)
            {
                context.FailureReason = result.Reason;
                Console.WriteLine($"[ATTACK-CS] phase=pipeline stage={stage.Name} status={result.Status.ToString().ToLowerInvariant()} reason=\"{result.Reason}\"");
                return result;
            }

            context.MarkCompleted(stage.Name);
            Console.WriteLine($"[ATTACK-CS] phase=pipeline stage={stage.Name} status={result.Status.ToString().ToLowerInvariant()}");
        }

        context.CurrentStage = string.Empty;
        return AttackStageResult.Success();
    }
}
