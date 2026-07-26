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

    /// <summary>
    /// Correlation score required to accept an army, spell or siege icon.
    /// <para>
    /// This used to be 0.92, which rejected armies that were in fact complete. Every other match in
    /// this subsystem runs at 0.60-0.80, and a normalised cross-correlation of 0.92 on a colour icon
    /// leaves no room for the level badge, the troop count overlay or emulator rescaling. The
    /// validation failures now log the observed score, so this value can be tuned from evidence.
    /// </para>
    /// </summary>
    private const double IconMatchThreshold = 0.80;

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
        if (screenshot == null || screenshot.Empty())
        {
            // Reporting everything as not ready makes the caller wipe and rebuild the whole army,
            // so a failed capture must never reach that decision silently.
            Console.WriteLine("[TRAIN] phase=validate status=fail reason=screenshot_empty");
            return new ArmyState(false, false, false, false);
        }

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
            if (!TryMatchTroopIcon(army, troop, out double score, out string diagnostic))
            {
                Console.WriteLine(
                    $"[TRAIN] phase=validate_troops status=fail reason=troop_missing_{troop} score={score:F2} threshold={IconMatchThreshold:F2} detail=\"{diagnostic}\"");
                return false;
            }
        }

        if (!_vision.TryReadFraction(screenshot, ArmySpaceRoi, out int current, out int capacity, out string spaceDiagnostic))
        {
            Console.WriteLine(
                $"[TRAIN] phase=validate_troops status=fail reason=army_space_unreadable detail=\"{spaceDiagnostic}\"");
            return false;
        }

        if (current <= 0 || current != capacity)
        {
            Console.WriteLine(FormattableString.Invariant(
                $"[TRAIN] phase=validate_troops status=fail reason=army_space_not_full current={current} capacity={capacity} detail=\"{spaceDiagnostic}\""));
            return false;
        }

        return true;
    }

    /// <summary>
    /// Looks for a troop icon in the primary template folder and then in the fallback folder,
    /// reporting the better of the two scores so the log explains which one came closest.
    /// </summary>
    private bool TryMatchTroopIcon(Mat army, string troop, out double score, out string diagnostic)
    {
        if (_vision.TryMatchWithScore("Army Troops", troop, army, IconMatchThreshold, out _, out score, out diagnostic))
        {
            return true;
        }

        if (_vision.TryMatchWithScore("s_troops", troop, army, IconMatchThreshold, out _, out double fallbackScore, out string fallbackDiagnostic))
        {
            score = fallbackScore;
            diagnostic = fallbackDiagnostic;
            return true;
        }

        if (fallbackScore > score)
        {
            score = fallbackScore;
            diagnostic = fallbackDiagnostic;
        }
        return false;
    }

    private bool DetectSpells(Mat screenshot, ArmySpec spec)
    {
        using Mat spells = TrainingVision.Crop(screenshot, SpellRoi);
        foreach (string spell in spec.Spells)
        {
            if (!_vision.TryMatchWithScore("Spells", spell, spells, IconMatchThreshold, out _, out double score, out string diagnostic))
            {
                Console.WriteLine(
                    $"[TRAIN] phase=validate_spells status=fail reason=spell_missing_{spell} score={score:F2} threshold={IconMatchThreshold:F2} detail=\"{diagnostic}\"");
                return false;
            }
        }

        if (!_vision.TryReadFraction(screenshot, SpellSpaceRoi, out int current, out int capacity, out string spaceDiagnostic))
        {
            Console.WriteLine(
                $"[TRAIN] phase=validate_spells status=fail reason=spell_space_unreadable detail=\"{spaceDiagnostic}\"");
            return false;
        }

        if (current <= 0 || current != capacity)
        {
            Console.WriteLine(FormattableString.Invariant(
                $"[TRAIN] phase=validate_spells status=fail reason=spell_space_not_full current={current} capacity={capacity} detail=\"{spaceDiagnostic}\""));
            return false;
        }

        return true;
    }

    private bool DetectSiege(Mat screenshot, ArmySpec spec)
    {
        using Mat siege = TrainingVision.Crop(screenshot, SiegeRoi);
        if (_vision.TryMatchWithScore("Siege Machines", spec.Siege, siege, IconMatchThreshold, out _, out double score, out string diagnostic))
        {
            return true;
        }
        Console.WriteLine(
            $"[TRAIN] phase=validate_siege status=fail reason=siege_missing_{spec.Siege} score={score:F2} threshold={IconMatchThreshold:F2} detail=\"{diagnostic}\"");
        return false;
    }
}
