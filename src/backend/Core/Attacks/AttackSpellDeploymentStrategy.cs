using System;
using System.Collections.Generic;
using System.Threading;
using CvAut.AttackPipelines;
using OpenCvSharp;

namespace CvAut;

internal sealed class AttackSpellDeploymentStrategy : ISpellDeploymentStrategy
{
    private readonly IADBHelper _adb;
    private readonly AttackDelayConfig _delays;
    private readonly Random _random = new();
    private AttackDeployBarSnapshot _bar = AttackDeployBarSnapshot.Empty;
    private AttackCoordinateSet? _coordinates;

    public AttackSpellDeploymentStrategy(IADBHelper adb, AttackDelayConfig delays)
    {
        _adb = adb;
        _delays = delays;
    }

    public string Name => "rage_freeze_rage";

    public void Configure(AttackDeployBarSnapshot bar, AttackCoordinateSet coordinates)
    {
        _bar = bar;
        _coordinates = coordinates;
    }

    public AttackStageResult Deploy(AttackContext context)
    {
        if (context.CancellationToken.WaitHandle.WaitOne(1200)) return AttackStageResult.Cancelled();
        DeploySpell("rage_initial", context.CancellationToken);
        DeploySpell("freeze", context.CancellationToken);
        DeploySpell("rage_remaining", context.CancellationToken);
        return context.IsCancellationRequested ? AttackStageResult.Cancelled() : AttackStageResult.Success();
    }

    public void DeploySpell(string key, CancellationToken token)
    {
        if (_coordinates == null || token.IsCancellationRequested) return;
        string tabKey = key.StartsWith("rage", StringComparison.OrdinalIgnoreCase) ? "rage" : "freeze";
        if (!_bar.Tabs.TryGetValue(tabKey, out Point tab)) return;
        IReadOnlyList<Point> points = key switch
        {
            "rage_initial" => _coordinates.RageInitial,
            "rage_remaining" => _coordinates.RageRemaining,
            _ => _coordinates.Freeze
        };
        int delay = tabKey == "rage" ? _delays.RageSpellDelayMs : _delays.FreezeSpellDelayMs;
        _adb.Tap(tab.X, tab.Y);
        foreach (Point point in points)
        {
            if (token.WaitHandle.WaitOne(delay)) return;
            _adb.Tap(point.X + _random.Next(-10, 11), point.Y + _random.Next(-10, 11));
        }
    }
}
