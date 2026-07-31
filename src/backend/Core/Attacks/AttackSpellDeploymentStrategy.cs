using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using CvAut.AttackPipelines;
using OpenCvSharp;

namespace CvAut;

internal readonly record struct SpellDeploymentPlan(
    int RageDetected,
    int FreezeDetected,
    int RageInitial,
    int Freeze,
    int RageRemaining,
    int SpellSpace);

internal sealed class AttackSpellDeploymentStrategy : ISpellDeploymentStrategy
{
    private const int ExpectedSpellSpace = 11;
    private const int RageHousingSpace = 2;
    private const int FreezeHousingSpace = 1;

    private readonly IADBHelper _adb;
    private readonly AttackDelayConfig _delays;
    private readonly TroopCountReader _countReader;
    private readonly AttackDeployBarScanner _scanner;
    private readonly Random _random = new();
    private AttackDeployBarSnapshot _bar = AttackDeployBarSnapshot.Empty;
    private AttackCoordinateSet? _coordinates;

    public AttackSpellDeploymentStrategy(
        IADBHelper adb,
        AttackDelayConfig delays,
        TroopCountReader countReader,
        AttackDeployBarScanner scanner)
    {
        _adb = adb;
        _delays = delays;
        _countReader = countReader;
        _scanner = scanner;
    }

    public string Name => "rage_freeze_rage";

    public void Configure(AttackDeployBarSnapshot bar, AttackCoordinateSet coordinates)
    {
        _bar = bar;
        _coordinates = coordinates;
    }

    public AttackStageResult Deploy(AttackContext context)
    {
        CancellationToken token = context.CancellationToken;
        if (token.WaitHandle.WaitOne(650)) return AttackStageResult.Cancelled();
        if (_coordinates == null)
            return Degraded("coordinates_unavailable");

        int maxRage = _coordinates.RageInitial.Count + _coordinates.RageRemaining.Count;
        int maxFreeze = _coordinates.Freeze.Count;
        AttackDeployBarSnapshot initialBar = _scanner.Scan(false, new[] { "rage", "freeze" });
        int rage = ReadSpellCount("rage", initialBar, maxRage, token, out double rageConfidence, out string rageDiagnostic);
        int freeze = ReadSpellCount("freeze", initialBar, maxFreeze, token, out double freezeConfidence, out string freezeDiagnostic);
        if (token.IsCancellationRequested) return AttackStageResult.Cancelled();
        if (rage < 0 || freeze < 0)
        {
            return Degraded($"spell_count_unreadable rage={rage} freeze={freeze} rage_detail={LogSafe(rageDiagnostic)} freeze_detail={LogSafe(freezeDiagnostic)}");
        }

        int expectedSpellSpace = ResolveExpectedSpellSpace(rage, freeze);

        if (!TryCreatePlan(
                rage,
                freeze,
                _coordinates.RageInitial.Count,
                _coordinates.Freeze.Count,
                _coordinates.RageRemaining.Count,
                expectedSpellSpace,
                out SpellDeploymentPlan plan,
                out string planReason))
        {
            return Degraded($"invalid_spell_plan:{planReason}");
        }

        Console.WriteLine($"[ATTACK-CS] phase=spell_plan status=success rage_detected={rage} rage_confidence={rageConfidence:F2} rage_initial={plan.RageInitial} rage_remaining={plan.RageRemaining} freeze_detected={freeze} freeze_confidence={freezeConfidence:F2} freeze_planned={plan.Freeze} spell_space={expectedSpellSpace} calculated_space={plan.SpellSpace}");

        int rageAfterInitial = rage - plan.RageInitial;
        if (!DeployAndVerify(
                "rage",
                "rage_initial",
                plan.RageInitial,
                rage,
                rageAfterInitial,
                maxRage,
                _coordinates.RageInitial,
                _delays.RageSpellDelayMs,
                token,
                out string failure))
            return token.IsCancellationRequested ? AttackStageResult.Cancelled() : Degraded(failure);

        if (!DeployAndVerify(
                "freeze",
                "freeze",
                plan.Freeze,
                freeze,
                0,
                maxFreeze,
                _coordinates.Freeze,
                _delays.FreezeSpellDelayMs,
                token,
                out failure))
            return token.IsCancellationRequested ? AttackStageResult.Cancelled() : Degraded(failure);

        if (!DeployAndVerify(
                "rage",
                "rage_remaining",
                plan.RageRemaining,
                rageAfterInitial,
                0,
                maxRage,
                _coordinates.RageRemaining,
                _delays.RageSpellDelayMs,
                token,
                out failure))
            return token.IsCancellationRequested ? AttackStageResult.Cancelled() : Degraded(failure);

        return token.IsCancellationRequested ? AttackStageResult.Cancelled() : AttackStageResult.Success();
    }

    public void DeploySpell(string key, CancellationToken token)
    {
        if (_coordinates == null || token.IsCancellationRequested) return;
        string tabKey = key.StartsWith("rage", StringComparison.OrdinalIgnoreCase) ? "rage" : "freeze";
        IReadOnlyList<Point> points = key switch
        {
            "rage_initial" => _coordinates.RageInitial,
            "rage_remaining" => _coordinates.RageRemaining,
            _ => _coordinates.Freeze
        };
        int maximumExpected = tabKey == "rage"
            ? _coordinates.RageInitial.Count + _coordinates.RageRemaining.Count
            : _coordinates.Freeze.Count;
        AttackDeployBarSnapshot bar = _scanner.Scan(false, new[] { tabKey });
        int count = ReadSpellCount(tabKey, bar, maximumExpected, token, out _, out _);
        if (count <= 0) return;
        int planned = Math.Min(count, points.Count);
        int delay = tabKey == "rage" ? _delays.RageSpellDelayMs : _delays.FreezeSpellDelayMs;
        _ = DeployGroup(tabKey, key, planned, points, delay, token, out _);
    }

    internal static bool TryCreatePlan(
        int rage,
        int freeze,
        int rageInitialSlots,
        int freezeSlots,
        int rageRemainingSlots,
        int expectedSpellSpace,
        out SpellDeploymentPlan plan,
        out string reason)
    {
        plan = default;
        if (rage < 0 || freeze < 0)
        {
            reason = "negative_count";
            return false;
        }

        int calculatedSpace = rage * RageHousingSpace + freeze * FreezeHousingSpace;
        if (calculatedSpace != expectedSpellSpace)
        {
            reason = $"spell_space_mismatch rage={rage} freeze={freeze} calculated={calculatedSpace} expected={expectedSpellSpace}";
            return false;
        }

        int rageInitial = Math.Min(2, Math.Min(rage, rageInitialSlots));
        int rageRemaining = rage - rageInitial;
        if (rageRemaining > rageRemainingSlots)
        {
            reason = $"rage_coordinate_shortage required={rageRemaining} available={rageRemainingSlots}";
            return false;
        }
        if (freeze > freezeSlots)
        {
            reason = $"freeze_coordinate_shortage required={freeze} available={freezeSlots}";
            return false;
        }

        plan = new SpellDeploymentPlan(rage, freeze, rageInitial, freeze, rageRemaining, calculatedSpace);
        reason = "valid";
        return true;
    }

    internal static int ResolveExpectedSpellSpace(int rage, int freeze)
    {
        int detectedSpace = Math.Max(0, rage) * RageHousingSpace
            + Math.Max(0, freeze) * FreezeHousingSpace;
        return detectedSpace == 9 ? 9 : ExpectedSpellSpace;
    }

    private bool DeployAndVerify(
        string tabKey,
        string group,
        int plannedCount,
        int expectedBefore,
        int expectedAfter,
        int maximumExpected,
        IReadOnlyList<Point> points,
        int delay,
        CancellationToken token,
        out string failure)
    {
        failure = string.Empty;
        if (plannedCount == 0) return true;
        if (plannedCount > points.Count)
        {
            failure = $"coordinate_shortage:{group}:required={plannedCount}:available={points.Count}";
            return false;
        }

        // The count was already established by the initial plan or the previous group's
        // post-deploy verification. Avoid another screenshot/OCR cycle before every group.
        Console.WriteLine($"[ATTACK-CS] phase=deploy_spell status=planned item={tabKey} group={group} count_before={expectedBefore} planned_count={plannedCount} expected_remaining={expectedAfter}");
        if (!DeployGroup(tabKey, group, plannedCount, points, delay, token, out failure))
            return false;
        if (token.WaitHandle.WaitOne(100))
        {
            failure = $"cancelled_after_deploy:{group}";
            return false;
        }

        AttackDeployBarSnapshot afterBar = _scanner.Scan(false, expectedAfter > 0 ? new[] { tabKey } : Array.Empty<string>());
        if (!afterBar.Tabs.ContainsKey(tabKey))
        {
            if (expectedAfter == 0)
            {
                Console.WriteLine($"[ATTACK-CS] phase=validate_spell_remaining status=success item={tabKey} group={group} remaining=0 reason=tab_absent");
                return true;
            }
            failure = $"tab_disappeared_early:{group}:expected_remaining={expectedAfter}";
            return false;
        }

        int after = ReadSpellCount(
            tabKey,
            afterBar,
            maximumExpected,
            token,
            out double confidence,
            out string diagnostic,
            allowEmptyAsZero: expectedAfter == 0);
        if (after != expectedAfter)
        {
            failure = $"count_after_mismatch:{group}:expected={expectedAfter}:actual={after}:confidence={confidence:F2}:detail={LogSafe(diagnostic)}";
            return false;
        }
        Console.WriteLine($"[ATTACK-CS] phase=validate_spell_remaining status=success item={tabKey} group={group} remaining={after} confidence={confidence:F2} reason=ocr_match detail=\"{LogSafe(diagnostic)}\"");
        return true;
    }

    private bool DeployGroup(
        string tabKey,
        string group,
        int count,
        IReadOnlyList<Point> points,
        int delay,
        CancellationToken token,
        out string failure)
    {
        failure = string.Empty;
        AttackDeployBarSnapshot bar = _scanner.Scan(false, new[] { tabKey });
        if (!bar.Tabs.TryGetValue(tabKey, out Point tab))
        {
            failure = $"tab_not_found:{group}";
            return false;
        }

        var taps = new List<Point>(count);
        Stopwatch watch = Stopwatch.StartNew();
        _adb.Tap(tab.X, tab.Y);
        for (int index = 0; index < count; index++)
        {
            if (token.WaitHandle.WaitOne(delay))
            {
                failure = $"cancelled_during_deploy:{group}:sent={taps.Count}:planned={count}";
                return false;
            }
            Point point = points[index];
            Point tap = new(point.X + _random.Next(-10, 11), point.Y + _random.Next(-10, 11));
            _adb.Tap(tap.X, tap.Y);
            taps.Add(tap);
        }
        watch.Stop();
        Console.WriteLine($"[ATTACK-CS] phase=deploy_spell status=success item={tabKey} group={group} planned_count={count} tap_count={taps.Count} tab={tab.X},{tab.Y} delay_ms={delay} first={taps[0].X},{taps[0].Y} last={taps[^1].X},{taps[^1].Y} duration={watch.ElapsedMilliseconds}ms");
        return true;
    }

    private int ReadSpellCount(
        string key,
        AttackDeployBarSnapshot bar,
        int maximumExpected,
        CancellationToken token,
        out double confidence,
        out string diagnostic,
        bool allowEmptyAsZero = false)
    {
        confidence = 0;
        diagnostic = string.Empty;
        int bestValue = -1;
        double bestConfidence = 0;
        var votes = new Dictionary<int, int>();
        var attempts = new List<string>();

        for (int attempt = 1; attempt <= 3 && !token.IsCancellationRequested; attempt++)
        {
            int value = _countReader.Read(
                key,
                bar.Tabs,
                maximumExpected,
                out double sampleConfidence,
                out string sampleDiagnostic,
                captureDebug: attempt == 3);
            attempts.Add($"attempt={attempt}:value={value}:confidence={sampleConfidence:F2}:{sampleDiagnostic}");
            Console.WriteLine($"[ATTACK-CS] phase=read_spell_count status=sample item={key} attempt={attempt} value={value} confidence={sampleConfidence:F2} max_expected={maximumExpected} detail=\"{LogSafe(sampleDiagnostic)}\"");
            if (value < 0 && allowEmptyAsZero && IsEmptyBadgeDiagnostic(sampleDiagnostic))
            {
                confidence = 1;
                diagnostic = $"reason=quantity_badge_absent attempt={attempt} detail={sampleDiagnostic}";
                Console.WriteLine($"[ATTACK-CS] phase=read_spell_count status=success item={key} value=0 confidence=1.00 reason=quantity_badge_absent");
                return 0;
            }
            if (value >= 0)
            {
                votes[value] = votes.TryGetValue(value, out int count) ? count + 1 : 1;
                if (sampleConfidence > bestConfidence)
                {
                    bestValue = value;
                    bestConfidence = sampleConfidence;
                }
                if (attempt == 1 && sampleConfidence >= 0.88)
                {
                    confidence = sampleConfidence;
                    diagnostic = $"reason=single_high_confidence attempts={string.Join(";", attempts)}";
                    Console.WriteLine($"[ATTACK-CS] phase=read_spell_count status=success item={key} value={value} confidence={confidence:F2} reason=single_high_confidence");
                    return value;
                }
                if (votes[value] >= 2)
                {
                    confidence = sampleConfidence;
                    diagnostic = $"reason=consensus votes={FormatVotes(votes)} attempts={string.Join(";", attempts)}";
                    Console.WriteLine($"[ATTACK-CS] phase=read_spell_count status=success item={key} value={value} confidence={confidence:F2} reason=consensus votes=\"{FormatVotes(votes)}\"");
                    return value;
                }
            }
            if (attempt < 3 && token.WaitHandle.WaitOne(50))
            {
                diagnostic = $"reason=cancelled attempts={string.Join(";", attempts)}";
                return -1;
            }
        }

        confidence = bestConfidence;
        bool acceptHighConfidence = bestConfidence >= 0.75;
        diagnostic = $"reason={(acceptHighConfidence ? "single_high_confidence" : "no_consensus")} votes={FormatVotes(votes)} attempts={string.Join(";", attempts)}";
        Console.WriteLine($"[ATTACK-CS] phase=read_spell_count status={(acceptHighConfidence ? "success" : "fail")} item={key} value={(acceptHighConfidence ? bestValue : -1)} confidence={confidence:F2} reason={(acceptHighConfidence ? "single_high_confidence" : "no_consensus")} votes=\"{FormatVotes(votes)}\"");
        return acceptHighConfidence ? bestValue : -1;
    }

    internal static bool IsEmptyBadgeDiagnostic(string diagnostic)
        => diagnostic.Contains("reason=no_candidate_accepted", StringComparison.Ordinal)
            && diagnostic.Contains("reason=no_result", StringComparison.Ordinal)
            && !diagnostic.Contains("reason=out_of_range", StringComparison.Ordinal)
            && !diagnostic.Contains("reason=low_confidence", StringComparison.Ordinal)
            && !diagnostic.Contains("reason=screenshot_empty", StringComparison.Ordinal)
            && !diagnostic.Contains("reason=tab_missing", StringComparison.Ordinal);

    private static AttackStageResult Degraded(string reason)
    {
        Console.WriteLine($"[ATTACK-CS WARNING] phase=spell_deployment status=degraded reason=\"{LogSafe(reason)}\"");
        return AttackStageResult.Skip(reason);
    }

    private static string FormatVotes(IReadOnlyDictionary<int, int> votes)
    {
        if (votes.Count == 0) return "none";
        var parts = new List<string>(votes.Count);
        foreach ((int value, int count) in votes) parts.Add($"{value}:{count}");
        return string.Join(',', parts);
    }

    private static string LogSafe(string value)
        => value.Replace('"', '\'').Replace('\r', ' ').Replace('\n', ' ');
}
