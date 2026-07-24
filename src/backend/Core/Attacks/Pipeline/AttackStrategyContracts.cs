using System;
using System.Collections.Generic;
using OpenCvSharp;

namespace CvAut.AttackPipelines;

internal interface ITroopDeploymentStrategy
{
    string Name { get; }
    AttackStageResult Deploy(AttackContext context);
}

internal interface ISpellDeploymentStrategy
{
    string Name { get; }
    AttackStageResult Deploy(AttackContext context);
}

internal sealed class DelegateTroopDeploymentStrategy : ITroopDeploymentStrategy
{
    private readonly Func<AttackContext, AttackStageResult> _deploy;

    public DelegateTroopDeploymentStrategy(
        string name,
        Func<AttackContext, AttackStageResult> deploy)
    {
        Name = name;
        _deploy = deploy ?? throw new ArgumentNullException(nameof(deploy));
    }

    public string Name { get; }

    public AttackStageResult Deploy(AttackContext context)
        => context.IsCancellationRequested
            ? AttackStageResult.Cancelled()
            : _deploy(context);
}

internal sealed class DelegateSpellDeploymentStrategy : ISpellDeploymentStrategy
{
    private readonly Func<AttackContext, AttackStageResult> _deploy;

    public DelegateSpellDeploymentStrategy(
        string name,
        Func<AttackContext, AttackStageResult> deploy)
    {
        Name = name;
        _deploy = deploy ?? throw new ArgumentNullException(nameof(deploy));
    }

    public string Name { get; }

    public AttackStageResult Deploy(AttackContext context)
        => context.IsCancellationRequested
            ? AttackStageResult.Cancelled()
            : _deploy(context);
}

internal sealed record AttackCoordinateSet(
    IReadOnlyList<Point> PrimaryTroops,
    IReadOnlyList<Point> SupportTroops,
    IReadOnlyList<Point> RageInitial,
    IReadOnlyList<Point> Freeze,
    IReadOnlyList<Point> RageRemaining);

internal interface IAttackCoordinateProvider
{
    AttackCoordinateSet GetCoordinates(string direction, string strategy);
}

internal sealed class DelegateAttackCoordinateProvider : IAttackCoordinateProvider
{
    private readonly Func<string, string, AttackCoordinateSet> _resolve;

    public DelegateAttackCoordinateProvider(
        Func<string, string, AttackCoordinateSet> resolve)
    {
        _resolve = resolve ?? throw new ArgumentNullException(nameof(resolve));
    }

    public AttackCoordinateSet GetCoordinates(string direction, string strategy)
        => _resolve(direction, strategy);
}
