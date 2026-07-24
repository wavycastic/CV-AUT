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

internal sealed record AttackCoordinateSet(
    IReadOnlyList<Point> PrimaryTroops,
    IReadOnlyList<Point> SupportTroops,
    IReadOnlyList<Point> RageInitial,
    IReadOnlyList<Point> Freeze,
    IReadOnlyList<Point> RageRemaining);

internal interface IAttackCoordinateProvider
{
    AttackCoordinateSet GetCoordinates(string direction);
}
