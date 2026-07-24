using System;
using System.Collections.Generic;
using System.Linq;
using CvAut.AttackPipelines;
using OpenCvSharp;

namespace CvAut;

internal sealed class DefaultAttackCoordinateProvider : IAttackCoordinateProvider
{
    private const int ScreenWidth = 1600;
    private readonly AttackCoordinateConfig _custom;

    private static readonly Point[] DragonLeft =
    {
        new(170, 384), new(214, 348), new(246, 327), new(270, 306),
        new(305, 285), new(345, 255), new(368, 238), new(396, 216),
        new(417, 201), new(442, 182), new(487, 152), new(535, 121),
        new(640, 35), new(442, 182)
    };

    private static readonly Point[] BalloonLeft =
    {
        new(170, 384), new(214, 348), new(246, 327), new(270, 306),
        new(305, 285), new(345, 255), new(368, 238), new(396, 216),
        new(417, 201), new(444, 183), new(486, 154), new(534, 122),
        new(345, 255), new(444, 183), new(368, 238), new(246, 327),
        new(417, 201)
    };

    private static readonly Point[] FallbackLeft =
    {
        new(145, 420), new(171, 384), new(214, 348), new(246, 327),
        new(270, 306), new(305, 285), new(345, 255), new(396, 216),
        new(442, 182), new(487, 152), new(535, 121), new(610, 66),
        new(185, 500), new(238, 562), new(304, 616), new(374, 670)
    };

    private static readonly Point[] RageLeft =
    {
        new(549, 353), new(674, 247), new(797, 317), new(690, 439),
        new(777, 403)
    };

    private static readonly Point[] FreezeLeft =
    {
        new(614, 371), new(769, 276), new(770, 363), new(704, 494),
        new(798, 405), new(874, 405)
    };

    private static readonly HeroDeploymentPoint[] HeroesLeft =
    {
        new("siege_machine", new Point(364, 236)),
        new("queen", new Point(364, 236)),
        new("bk", new Point(513, 135)),
        new("warden", new Point(445, 191)),
        new("prince", new Point(445, 191)),
        new("rc", new Point(426, 204))
    };

    public DefaultAttackCoordinateProvider(AttackCoordinateConfig? custom = null)
    {
        _custom = custom ?? new AttackCoordinateConfig();
    }

    public AttackCoordinateSet GetCoordinates(string direction, string strategy)
    {
        bool right = direction.EndsWith("right", StringComparison.OrdinalIgnoreCase);
        Point Transform(Point point) => right
            ? new Point(ScreenWidth - 1 - point.X, point.Y)
            : point;

        Point[] dragon = DragonLeft.Select(Transform).ToArray();
        Point[] balloon = BalloonLeft.Select(Transform).ToArray();
        Point[] fallback = FallbackLeft.Select(Transform).ToArray();
        Point[] rage = RageLeft.Select(Transform).ToArray();
        Point[] freeze = FreezeLeft.Select(Transform).ToArray();
        HeroDeploymentPoint[] heroes = HeroesLeft
            .Select(hero => new HeroDeploymentPoint(hero.Name, Transform(hero.Coordinate)))
            .ToArray();

        IReadOnlyList<Point> rageInitial = rage.Take(2).ToArray();
        IReadOnlyList<Point> rageRemaining = rage.Skip(2).ToArray();
        if (_custom.SpellCoordinates.TryGetValue(direction, out SpellDeploymentGroups? custom))
        {
            if (custom.RageInitial.Count > 0) rageInitial = custom.RageInitial.ToArray();
            if (custom.Freeze.Count > 0) freeze = custom.Freeze.ToArray();
            if (custom.RageRemaining.Count > 0) rageRemaining = custom.RageRemaining.ToArray();
        }

        var troops = new Dictionary<string, IReadOnlyList<Point>>(StringComparer.OrdinalIgnoreCase)
        {
            ["dragon"] = dragon,
            ["e_drag"] = dragon.Skip(2).Take(10).ToArray(),
            ["balloon"] = balloon,
            ["ice_minion"] = dragon.Skip(2).Take(10).ToArray(),
            ["ice_golem"] = dragon.Skip(4).Take(5).ToArray(),
            ["azure_dragon"] = new[] { heroes[2].Coordinate },
            ["siege_machine"] = new[] { heroes[0].Coordinate }
        };
        var fallbacks = new Dictionary<string, IReadOnlyList<Point>>(StringComparer.OrdinalIgnoreCase)
        {
            ["dragon"] = fallback,
            ["e_drag"] = fallback,
            ["balloon"] = balloon
        };

        return new AttackCoordinateSet(
            troops,
            fallbacks,
            heroes,
            rageInitial,
            freeze,
            rageRemaining);
    }
}
