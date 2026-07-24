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
    private readonly Random _random = new();
    private AttackDeployBarSnapshot _bar = AttackDeployBarSnapshot.Empty;
    private AttackCoordinateSet? _coordinates;
    private string _direction = "top_left";

    public AttackTroopDeploymentStrategy(IADBHelper adb, AttackDelayConfig delays, TroopCountReader countReader)
    {
        _adb = adb;
        _delays = delays;
        _countReader = countReader;
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
        DeployTroop(primary, context.CancellationToken);
        if (context.IsCancellationRequested) return AttackStageResult.Cancelled();
        DeployIfPresent("ice_minion", context.CancellationToken);
        DeployIfPresent("ice_golem", context.CancellationToken);
        DeployIfPresent("azure_dragon", context.CancellationToken);
        if (context.UseEventTroops)
        {
            foreach (EventTroopTab troop in _bar.EventTroops)
                DeployQuick(troop.Key, troop.DropCount, context.CancellationToken);
        }
        DeployTroop("balloon", context.CancellationToken);
        DeployTroop("siege_machine", context.CancellationToken);
        return context.IsCancellationRequested ? AttackStageResult.Cancelled() : AttackStageResult.Success();
    }

    public void DeployTroop(string key, CancellationToken token)
    {
        if (token.IsCancellationRequested || _coordinates == null) return;
        if (!_bar.Tabs.TryGetValue(key, out Point tab))
        {
            Console.WriteLine($"[ATTACK-CS WARNING] phase=deploy status=skip item={key} reason=tab_not_found");
            return;
        }
        if (!_coordinates.Troops.TryGetValue(key, out IReadOnlyList<Point>? points) || points.Count == 0) return;

        Stopwatch watch = Stopwatch.StartNew();
        _adb.Tap(tab.X, tab.Y);
        if (token.WaitHandle.WaitOne(_adb.FramePacer.AdjustDelay(160))) return;
        var taps = new List<Point>(points.Count);
        foreach (Point point in points) taps.Add(Jitter(point));
        _adb.TapSequenceSafeFast(taps, 5, _delays.TroopDeployDelayMs, token);
        watch.Stop();
        Console.WriteLine($"[ATTACK-CS] phase=deploy status=success item={key} tap_count={taps.Count} duration={watch.ElapsedMilliseconds}ms");
    }

    public void EnsureFullyDeployed(string key, CancellationToken token)
    {
        if (token.IsCancellationRequested || _coordinates == null) return;
        if (!_bar.Tabs.TryGetValue(key, out Point tab)) return;
        if (!_coordinates.FallbackTroops.TryGetValue(key, out IReadOnlyList<Point>? fallback) || fallback.Count == 0) return;
        if (token.WaitHandle.WaitOne(540)) return;

        int remaining = _countReader.Read(key, _bar.Tabs, out double confidence);
        int tapCount = remaining < 0 ? Math.Min(4, fallback.Count) : Math.Min(remaining + 2, fallback.Count);
        if (remaining == 0) return;
        Console.WriteLine($"[ATTACK-CS] phase=validate_remaining status=fallback item={key} remaining={remaining} confidence={confidence:F2} tap_count={tapCount}");
        _adb.Tap(tab.X, tab.Y);
        if (token.WaitHandle.WaitOne(_adb.FramePacer.AdjustDelay(160))) return;
        var taps = new List<Point>(tapCount);
        for (int index = 0; index < tapCount; index++) taps.Add(Jitter(fallback[index]));
        _adb.TapSequenceSafeFast(taps, 5, _delays.TroopDeployDelayMs, token);
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
        if (token.WaitHandle.WaitOne(_adb.FramePacer.AdjustDelay(160))) return;
        var taps = new List<Point>(count);
        for (int index = 0; index < count; index++) taps.Add(Jitter(points[index % points.Count]));
        _adb.TapSequenceSafeFast(taps, 5, _delays.TroopDeployDelayMs, token);
    }

    private Point Jitter(Point point)
    {
        bool left = _direction.EndsWith("left", StringComparison.OrdinalIgnoreCase);
        int dx = left ? _random.Next(0, 40) : _random.Next(-44, 1);
        int dy = left ? _random.Next(-27, 1) : _random.Next(-33, 1);
        return new Point(point.X + dx, point.Y + dy);
    }
}
