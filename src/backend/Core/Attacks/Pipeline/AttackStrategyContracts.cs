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

internal sealed record AttackCoordinateSet(
    IReadOnlyDictionary<string, IReadOnlyList<Point>> Troops,
    IReadOnlyDictionary<string, IReadOnlyList<Point>> FallbackTroops,
    IReadOnlyList<HeroDeploymentPoint> Heroes,
    IReadOnlyList<Point> RageInitial,
    IReadOnlyList<Point> Freeze,
    IReadOnlyList<Point> RageRemaining);

internal interface IAttackCoordinateProvider
{
    AttackCoordinateSet GetCoordinates(string direction, string strategy);
}
