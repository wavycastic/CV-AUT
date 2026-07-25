using System;
using System.Collections.Generic;
using OpenCvSharp;

namespace CvAut;

internal sealed class AttackDelayConfig
{
    public int TroopDeployDelayMs { get; init; } = 60;
    public int RageSpellDelayMs { get; init; } = 650;
    public int FreezeSpellDelayMs { get; init; } = 850;
    public int GrandWardenAbilityDelayMs { get; init; } = 2500;
}

internal sealed class SpellDeploymentGroups
{
    public List<Point> RageInitial { get; init; } = new();
    public List<Point> Freeze { get; init; } = new();
    public List<Point> RageRemaining { get; init; } = new();
}

internal sealed class AttackCoordinateConfig
{
    public Dictionary<string, SpellDeploymentGroups> SpellCoordinates { get; init; }
        = new(StringComparer.OrdinalIgnoreCase);
}

internal sealed record HeroDeploymentPoint(string Name, Point Coordinate);
