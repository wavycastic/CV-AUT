namespace CvAut.AttackPipelines;

internal enum AttackStageStatus
{
    Succeeded,
    Skipped,
    Failed,
    Cancelled
}

internal readonly record struct AttackStageResult(
    AttackStageStatus Status,
    string? Reason = null)
{
    public bool CanContinue
        => Status is AttackStageStatus.Succeeded or AttackStageStatus.Skipped;

    public static AttackStageResult Success() => new(AttackStageStatus.Succeeded);
    public static AttackStageResult Skip(string reason) => new(AttackStageStatus.Skipped, reason);
    public static AttackStageResult Fail(string reason) => new(AttackStageStatus.Failed, reason);
    public static AttackStageResult Cancelled() => new(AttackStageStatus.Cancelled, "cancelled");
}

internal interface IAttackStage
{
    string Name { get; }
    AttackStageResult Execute(AttackContext context);
}

internal interface IAttackStageOperations
{
    AttackStageResult Prepare(AttackContext context);
    AttackStageResult DeployTroops(AttackContext context);
    AttackStageResult DeploySpells(AttackContext context);
    AttackStageResult ActivateHeroes(AttackContext context);
    AttackStageResult Complete(AttackContext context);
}
