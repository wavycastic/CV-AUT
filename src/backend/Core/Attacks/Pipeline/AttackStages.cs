namespace CvAut.AttackPipelines;

internal abstract class AttackStageBase : IAttackStage
{
    protected AttackStageBase(IAttackStageOperations operations)
    {
        Operations = operations;
    }

    protected IAttackStageOperations Operations { get; }
    public abstract string Name { get; }
    protected abstract AttackStageResult ExecuteCore(AttackContext context);

    public AttackStageResult Execute(AttackContext context)
        => context.IsCancellationRequested
            ? AttackStageResult.Cancelled()
            : ExecuteCore(context);
}

internal sealed class AttackPreparationStage : AttackStageBase
{
    public AttackPreparationStage(IAttackStageOperations operations) : base(operations) { }
    public override string Name => "preparation";
    protected override AttackStageResult ExecuteCore(AttackContext context)
        => Operations.Prepare(context);
}

internal sealed class TroopDeploymentStage : AttackStageBase
{
    public TroopDeploymentStage(IAttackStageOperations operations) : base(operations) { }
    public override string Name => "troop_deployment";
    protected override AttackStageResult ExecuteCore(AttackContext context)
        => Operations.DeployTroops(context);
}

internal sealed class SpellDeploymentStage : AttackStageBase
{
    public SpellDeploymentStage(IAttackStageOperations operations) : base(operations) { }
    public override string Name => "spell_deployment";
    protected override AttackStageResult ExecuteCore(AttackContext context)
        => Operations.DeploySpells(context);
}

internal sealed class HeroAbilityStage : AttackStageBase
{
    public HeroAbilityStage(IAttackStageOperations operations) : base(operations) { }
    public override string Name => "hero_ability";
    protected override AttackStageResult ExecuteCore(AttackContext context)
        => Operations.ActivateHeroes(context);
}

internal sealed class BattleCompletionStage : AttackStageBase
{
    public BattleCompletionStage(IAttackStageOperations operations) : base(operations) { }
    public override string Name => "battle_completion";
    protected override AttackStageResult ExecuteCore(AttackContext context)
        => Operations.Complete(context);
}
