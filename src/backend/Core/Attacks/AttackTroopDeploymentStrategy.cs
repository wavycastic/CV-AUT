using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using CvAut.AttackPipelines;
using OpenCvSharp;

namespace CvAut;

internal sealed class AttackTroopDeploymentStrategy : ITroopDeploymentStrategy
{
    private readonly IADBHelper _adb;
    private readonly AttackDelayConfig _delays;
    private readonly TroopCountReader _countReader;
    private readonly AttackDeployBarScanner _scanner;
    private readonly Random _random = new();
    private AttackDeployBarSnapshot _bar = AttackDeployBarSnapshot.Empty;
    private AttackCoordinateSet? _coordinates;
    private string _direction = "top_left";

    public AttackTroopDeploymentStrategy(
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

    public string Name => "standard_air_army";

    public void Configure(AttackDeployBarSnapshot bar, AttackCoordinateSet coordinates, string direction)
    {
        _bar = bar;
        _coordinates = coordinates;
        _direction = direction;
    }

    public AttackStageResult Deploy(AttackContext context)
    {
        string primary = context.NormalizedStrategy == "ElectroDragon_Attack" ? "e_drag" : "dragon";
        if (!DeployTroop(primary, context.CancellationToken))
        {
            return context.IsCancellationRequested
                ? AttackStageResult.Cancelled()
                : AttackStageResult.Fail($"required_troop_deploy_failed:{primary}");
        }
        if (context.IsCancellationRequested) return AttackStageResult.Cancelled();
        DeployIfPresent("ice_minion", context.CancellationToken);
        DeployIfPresent("ice_golem", context.CancellationToken);
        DeployIfPresent("azure_dragon", context.CancellationToken);
        if (context.UseEventTroops)
        {
            foreach (EventTroopTab troop in _bar.EventTroops)
                DeployQuick(troop.Key, troop.DropCount, context.CancellationToken);
        }
        if (!DeployTroop("balloon", context.CancellationToken))
            return context.IsCancellationRequested ? AttackStageResult.Cancelled() : AttackStageResult.Fail("required_troop_deploy_failed:balloon");
        if (_bar.Tabs.ContainsKey("siege_machine"))
        {
            bool siegeDeployed = DeployTroop("siege_machine", context.CancellationToken);
            if (context.IsCancellationRequested) return AttackStageResult.Cancelled();
            if (!siegeDeployed)
            {
                Console.WriteLine("[ATTACK-CS WARNING] phase=deploy status=skip item=siege_machine reason=deploy_failed");
            }
        }
        else
        {
            Console.WriteLine("[ATTACK-CS] phase=deploy status=skip item=siege_machine reason=not_available");
        }
        return context.IsCancellationRequested ? AttackStageResult.Cancelled() : AttackStageResult.Success();
    }

    public bool DeployTroop(string key, CancellationToken token)
    {
        if (token.IsCancellationRequested)
        {
            Console.WriteLine($"[ATTACK-CS WARNING] phase=deploy status=cancelled item={key} reason=token_cancelled");
            return false;
        }
        if (_coordinates == null)
        {
            Console.WriteLine($"[ATTACK-CS ERROR] phase=deploy status=fail item={key} reason=coordinates_unavailable");
            return false;
        }
        if (!_coordinates.Troops.TryGetValue(key, out IReadOnlyList<Point>? points) || points.Count == 0)
        {
            Console.WriteLine($"[ATTACK-CS ERROR] phase=deploy status=fail item={key} reason=deployment_coordinates_missing");
            return false;
        }

        bool includeElectroDragon = key.Equals("e_drag", StringComparison.OrdinalIgnoreCase);
        using Mat? scanFrame = _adb.TakeScreenshot();
        if (scanFrame == null || scanFrame.Empty())
        {
            Console.WriteLine($"[ATTACK-CS WARNING] phase=deploy status=fail item={key} reason=screenshot_empty");
            return false;
        }
        AttackDeployBarSnapshot currentBar = _scanner.Scan(
            scanFrame,
            includeElectroDragon,
            new[] { key },
            requiredOnly: true);
        if (!currentBar.Tabs.TryGetValue(key, out Point tab))
        {
            Console.WriteLine($"[ATTACK-CS WARNING] phase=deploy status=fail item={key} reason=tab_not_found");
            return false;
        }

        int detectedCount;
        double confidence;
        if (key.Equals("siege_machine", StringComparison.OrdinalIgnoreCase))
        {
            detectedCount = 1;
            confidence = 1;
        }
        else
        {
            detectedCount = ReadTroopCount(
                key,
                currentBar,
                points.Count,
                token,
                out confidence,
                out string countDiagnostic,
                scanFrame);
            if (detectedCount < 0)
            {
                Console.WriteLine($"[ATTACK-CS WARNING] phase=deploy status=fail item={key} reason=count_unreadable confidence={confidence:F2} detail=\"{LogSafe(countDiagnostic)}\"");
                return false;
            }
            Console.WriteLine($"[ATTACK-CS] phase=read_troop_count status=success item={key} value={detectedCount} confidence={confidence:F2} detail=\"{LogSafe(countDiagnostic)}\"");
        }

        int tapCount = ResolveTapCount(detectedCount, points.Count);
        if (tapCount == 0)
        {
            Console.WriteLine($"[ATTACK-CS WARNING] phase=deploy status=fail item={key} reason=empty detected_count=0");
            return false;
        }

        Stopwatch watch = Stopwatch.StartNew();
        _adb.Tap(tab.X, tab.Y);
        if (token.WaitHandle.WaitOne(60))
        {
            Console.WriteLine($"[ATTACK-CS WARNING] phase=deploy status=cancelled item={key} reason=cancelled_after_tab_select");
            return false;
        }
        var taps = new List<Point>(tapCount);
        for (int index = 0; index < tapCount; index++) taps.Add(Jitter(points[index]));
        if (!DeployPacedTaps(key, taps, token)) return false;
        watch.Stop();
        Console.WriteLine($"[ATTACK-CS] phase=deploy status=success item={key} detected_count={detectedCount} confidence={confidence:F2} tap_count={taps.Count} tab={tab.X},{tab.Y} direction={_direction} input_mode=single_command first={taps[0].X},{taps[0].Y} last={taps[^1].X},{taps[^1].Y} duration={watch.ElapsedMilliseconds}ms");

        if (!key.Equals("siege_machine", StringComparison.OrdinalIgnoreCase)
            && !token.IsCancellationRequested
            && !token.WaitHandle.WaitOne(80))
        {
            AttackDeployBarSnapshot refreshed = _scanner.Scan(
                includeElectroDragon,
                new[] { key },
                requiredOnly: true,
                reportMissing: false);
            EnsureFullyDeployed(key, refreshed, token);
        }
        return !token.IsCancellationRequested;
    }

    public void EnsureFullyDeployed(string key, AttackDeployBarSnapshot currentBar, CancellationToken token)
    {
        if (token.IsCancellationRequested || _coordinates == null) return;
        bool includeElectroDragon = key.Equals("e_drag", StringComparison.OrdinalIgnoreCase);
        if (!_coordinates.FallbackTroops.TryGetValue(key, out IReadOnlyList<Point>? fallback) || fallback.Count == 0) return;
        if (!_coordinates.Troops.TryGetValue(key, out IReadOnlyList<Point>? planned) || planned.Count == 0) return;
        AttackDeployBarSnapshot probe = currentBar;

        for (int round = 1; round <= 3 && !token.IsCancellationRequested; round++)
        {
            if (!probe.Tabs.TryGetValue(key, out Point tab))
            {
                Console.WriteLine($"[ATTACK-CS] phase=validate_remaining status=success item={key} remaining=0 reason=tab_absent round={round}");
                return;
            }

            int remaining = ReadTroopCount(key, probe, planned.Count, token, out double confidence, out string countDiagnostic);
            if (remaining < 0)
            {
                if (IsQuantityBadgeAbsent(countDiagnostic))
                {
                    Console.WriteLine($"[ATTACK-CS] phase=validate_remaining status=success item={key} remaining=0 reason=quantity_badge_absent round={round}");
                    return;
                }
                Console.WriteLine($"[ATTACK-CS WARNING] phase=validate_remaining status=skip item={key} reason=ocr_unreadable confidence={confidence:F2} detail=\"{LogSafe(countDiagnostic)}\"");
                return;
            }
            if (remaining == 0)
            {
                Console.WriteLine($"[ATTACK-CS] phase=validate_remaining status=success item={key} remaining=0 reason=ocr_zero confidence={confidence:F2} detail=\"{LogSafe(countDiagnostic)}\"");
                return;
            }

            int tapCount = ResolveTapCount(remaining, fallback.Count);
            Console.WriteLine($"[ATTACK-CS] phase=validate_remaining status=fallback item={key} remaining={remaining} confidence={confidence:F2} tap_count={tapCount} tab={tab.X},{tab.Y} round={round} detail=\"{LogSafe(countDiagnostic)}\"");
            _adb.Tap(tab.X, tab.Y);
            if (token.WaitHandle.WaitOne(60)) return;
            var taps = new List<Point>(tapCount);
            for (int index = 0; index < tapCount; index++) taps.Add(Jitter(fallback[index]));
            if (!DeployPacedTaps(key, taps, token)) return;
            if (token.WaitHandle.WaitOne(200)) return;
            probe = _scanner.Scan(
                includeElectroDragon,
                new[] { key },
                requiredOnly: true,
                reportMissing: false);
        }

        Console.WriteLine($"[ATTACK-CS WARNING] phase=validate_remaining status=skip item={key} reason=max_cleanup_rounds");
    }

    private void DeployIfPresent(string key, CancellationToken token)
    {
        if (_bar.Tabs.ContainsKey(key)) DeployTroop(key, token);
    }

    private void DeployQuick(string key, int count, CancellationToken token)
    {
        if (_coordinates == null || !_bar.Tabs.TryGetValue(key, out Point tab)) return;
        if (!_coordinates.Troops.TryGetValue("dragon", out IReadOnlyList<Point>? points) || points.Count == 0) return;
        _adb.Tap(tab.X, tab.Y);
        if (token.WaitHandle.WaitOne(60)) return;
        var taps = new List<Point>(count);
        for (int index = 0; index < count; index++) taps.Add(Jitter(points[index % points.Count]));
        _ = DeployPacedTaps(key, taps, token);
    }

    private int ReadTroopCount(
        string key,
        AttackDeployBarSnapshot bar,
        int maximumExpected,
        CancellationToken token,
        out double confidence,
        out string diagnostic,
        Mat? firstFrame = null)
    {
        confidence = 0;
        diagnostic = string.Empty;
        int bestValue = -1;
        double bestConfidence = 0;
        var votes = new Dictionary<int, int>();
        var attemptDetails = new List<string>();

        for (int attempt = 1; attempt <= 3 && !token.IsCancellationRequested; attempt++)
        {
            int value = attempt == 1 && firstFrame != null
                ? _countReader.Read(
                    firstFrame,
                    key,
                    bar.Tabs,
                    maximumExpected,
                    out double sampleConfidence,
                    out string sampleDiagnostic,
                    captureDebug: false)
                : _countReader.Read(
                    key,
                    bar.Tabs,
                    maximumExpected,
                    out sampleConfidence,
                    out sampleDiagnostic,
                    captureDebug: attempt == 3);
            attemptDetails.Add($"attempt={attempt}:value={value}:confidence={sampleConfidence:F2}:{sampleDiagnostic}");
            Console.WriteLine($"[ATTACK-CS] phase=read_troop_count status=sample item={key} attempt={attempt} value={value} confidence={sampleConfidence:F2} max_expected={maximumExpected} detail=\"{LogSafe(sampleDiagnostic)}\"");
            if (value >= 0)
            {
                votes[value] = votes.TryGetValue(value, out int count) ? count + 1 : 1;
                if (sampleConfidence > bestConfidence)
                {
                    bestValue = value;
                    bestConfidence = sampleConfidence;
                }
                if (attempt == 1 && sampleConfidence >= 0.82)
                {
                    confidence = sampleConfidence;
                    diagnostic = $"reason=single_high_confidence attempts={string.Join(";", attemptDetails)}";
                    return value;
                }
                if (votes[value] >= 2)
                {
                    confidence = sampleConfidence;
                    diagnostic = $"reason=consensus votes={FormatVotes(votes)} attempts={string.Join(";", attemptDetails)}";
                    return value;
                }
            }
            if (attempt < 3 && token.WaitHandle.WaitOne(50))
            {
                diagnostic = $"reason=cancelled attempts={string.Join(";", attemptDetails)}";
                return -1;
            }
        }

        confidence = bestConfidence;
        bool acceptHighConfidence = bestConfidence >= 0.75;
        diagnostic = $"reason={(acceptHighConfidence ? "single_high_confidence" : "no_consensus")} votes={FormatVotes(votes)} attempts={string.Join(";", attemptDetails)}";
        return acceptHighConfidence ? bestValue : -1;
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

    internal static int ResolveTapCount(int detectedCount, int availableCoordinates)
        => detectedCount <= 0 || availableCoordinates <= 0
            ? 0
            : Math.Min(detectedCount, availableCoordinates);

    internal static bool IsQuantityBadgeAbsent(string diagnostic)
        => diagnostic.Contains("reason=quantity_badge_absent", StringComparison.Ordinal);

    private bool DeployPacedTaps(string key, IReadOnlyList<Point> taps, CancellationToken token)
    {
        if (token.IsCancellationRequested || taps.Count == 0) return false;
        _adb.TapSequence(taps);
        return !token.IsCancellationRequested;
    }

    private Point Jitter(Point point)
    {
        bool left = _direction.EndsWith("left", StringComparison.OrdinalIgnoreCase);
        int dx = left ? _random.Next(0, 40) : _random.Next(-44, 1);
        int dy = left ? _random.Next(-27, 1) : _random.Next(-33, 1);
        return new Point(point.X + dx, point.Y + dy);
    }
}
