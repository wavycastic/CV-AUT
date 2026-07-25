using System;
using System.Threading;
using OpenCvSharp;

namespace CvAut;

internal sealed class ArmyStateDetector
{
    internal static readonly Rect ArmyRoi = Rect.FromLTRB(682, 228, 1573, 383);
    internal static readonly Rect SpellRoi = Rect.FromLTRB(689, 461, 1250, 600);
    internal static readonly Rect SiegeRoi = Rect.FromLTRB(1256, 457, 1554, 608);
    internal static readonly Rect ArmySpaceRoi = Rect.FromLTRB(750, 195, 826, 225);
    internal static readonly Rect SpellSpaceRoi = Rect.FromLTRB(731, 398, 810, 464);

    private readonly IADBHelper _adb;
    private readonly TrainingVision _vision;
    private readonly HeroReadinessService _heroes;

    public ArmyStateDetector(IADBHelper adb, TrainingVision vision, HeroReadinessService heroes)
    {
        _adb = adb;
        _vision = vision;
        _heroes = heroes;
    }

    public ArmyState Detect(ArmySpec spec, CancellationToken token)
    {
        if (token.IsCancellationRequested) return new ArmyState(false, false, false, false);
        using Mat? screenshot = _adb.TakeScreenshot();
        if (screenshot == null || screenshot.Empty()) return new ArmyState(false, false, false, false);

        bool armyReady = DetectArmy(screenshot, spec);
        bool spellsReady = DetectSpells(screenshot, spec);
        bool siegeReady = DetectSiege(screenshot, spec);
        bool heroesReady = _heroes.IsReady(screenshot);
        return new ArmyState(armyReady, spellsReady, siegeReady, heroesReady);
    }

    private bool DetectArmy(Mat screenshot, ArmySpec spec)
    {
        using Mat army = TrainingVision.Crop(screenshot, ArmyRoi);
        foreach (string troop in spec.Troops)
        {
            if (!_vision.TryMatch("Army Troops", troop, army, 0.92, out _)
                && !_vision.TryMatch("s_troops", troop, army, 0.92, out _))
            {
                Console.WriteLine($"[TRAIN] phase=validate_troops status=fail reason=troop_missing_{troop}");
                return false;
            }
        }
        return _vision.TryReadFraction(screenshot, ArmySpaceRoi, out int current, out int capacity)
            && current > 0
            && current == capacity;
    }

    private bool DetectSpells(Mat screenshot, ArmySpec spec)
    {
        using Mat spells = TrainingVision.Crop(screenshot, SpellRoi);
        foreach (string spell in spec.Spells)
        {
            if (!_vision.TryMatch("Spells", spell, spells, 0.92, out _))
            {
                Console.WriteLine($"[TRAIN] phase=validate_spells status=fail reason=spell_missing_{spell}");
                return false;
            }
        }
        return _vision.TryReadFraction(screenshot, SpellSpaceRoi, out int current, out int capacity)
            && current > 0
            && current == capacity;
    }

    private bool DetectSiege(Mat screenshot, ArmySpec spec)
    {
        using Mat siege = TrainingVision.Crop(screenshot, SiegeRoi);
        return _vision.TryMatch("Siege Machines", spec.Siege, siege, 0.92, out _);
    }
}
