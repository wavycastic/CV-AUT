using System;
using System.Collections.Generic;
using System.Threading;
using CvAut.AttackPipelines;
using OpenCvSharp;

namespace CvAut;

internal sealed class AttackHeroDeploymentService
{
    private readonly IADBHelper _adb;
    private readonly AttackDelayConfig _delays;
    private readonly Random _random = new();
    private readonly Dictionary<string, Point> _abilityTabs = new(StringComparer.OrdinalIgnoreCase);
    private AttackDeployBarSnapshot _bar = AttackDeployBarSnapshot.Empty;
    private AttackCoordinateSet? _coordinates;

    public AttackHeroDeploymentService(IADBHelper adb, AttackDelayConfig delays)
    {
        _adb = adb;
        _delays = delays;
    }

    public void Configure(AttackDeployBarSnapshot bar, AttackCoordinateSet coordinates)
    {
        _bar = bar;
        _coordinates = coordinates;
        _abilityTabs.Clear();
    }

    public AttackStageResult DeployAndActivate(AttackContext context)
    {
        Deploy(context.CancellationToken);
        if (context.IsCancellationRequested) return AttackStageResult.Cancelled();
        if (context.CancellationToken.WaitHandle.WaitOne(_delays.GrandWardenAbilityDelayMs))
            return AttackStageResult.Cancelled();
        Activate(context.CancellationToken);
        return context.IsCancellationRequested ? AttackStageResult.Cancelled() : AttackStageResult.Success();
    }

    public void Deploy(CancellationToken token)
    {
        if (_coordinates == null) return;
        foreach (HeroDeploymentPoint hero in _coordinates.Heroes)
        {
            if (token.IsCancellationRequested || hero.Name == "siege_machine") continue;
            if (!_bar.Tabs.TryGetValue(hero.Name, out Point tab)) continue;
            _abilityTabs[hero.Name] = tab;
            _adb.Tap(tab.X, tab.Y);
            if (token.WaitHandle.WaitOne(_adb.FramePacer.AdjustDelay(72))) return;
            _adb.Tap(hero.Coordinate.X + _random.Next(-10, 11), hero.Coordinate.Y + _random.Next(-10, 11));
            if (token.WaitHandle.WaitOne(_adb.FramePacer.AdjustDelay(72))) return;
        }
    }

    public void Activate(CancellationToken token)
    {
        foreach (string hero in new[] { "warden", "queen", "bk", "prince", "rc" })
        {
            if (token.IsCancellationRequested) return;
            if (!_abilityTabs.TryGetValue(hero, out Point tab) && !_bar.Tabs.TryGetValue(hero, out tab)) continue;
            _adb.Tap(tab.X, tab.Y);
            if (token.WaitHandle.WaitOne(108)) return;
        }
    }
}
