using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using CvAut.AttackPipelines;

namespace CvAut;

/// <summary>
/// Compatibility facade over the staged attack pipeline. Scanning, coordinates,
/// troop deployment, spells, hero abilities and completion are separate services.
/// </summary>
internal sealed class Attacks : IAttackStageOperations
{
    private readonly IADBHelper _adb;
    private readonly AttackDelayConfig _delays;
    private readonly IAttackCoordinateProvider _coordinateProvider;
    private readonly AttackDeployBarScanner _scanner;
    private readonly AttackTroopDeploymentStrategy _troops;
    private readonly AttackSpellDeploymentStrategy _spells;
    private readonly AttackHeroDeploymentService _heroes;
    private readonly AttackPipeline _pipeline;
    private readonly Random _random = new();

    private AttackDeployBarSnapshot _bar = AttackDeployBarSnapshot.Empty;
    private AttackCoordinateSet? _coordinates;
    private string _direction = "top_left";
    private string _activeStrategy = "Dragon_Attack";

    public Attacks(
        IADBHelper adb,
        IVisionEngine vision,
        string? templatesPath = null,
        AttackDelayConfig? delays = null,
        AttackCoordinateConfig? coordinates = null)
    {
        _adb = adb ?? throw new ArgumentNullException(nameof(adb));
        ArgumentNullException.ThrowIfNull(vision);
        _delays = delays ?? new AttackDelayConfig();
        string root = string.IsNullOrWhiteSpace(templatesPath)
            ? vision.TemplatesDirectory
            : templatesPath;
        _coordinateProvider = new DefaultAttackCoordinateProvider(coordinates);
        _scanner = new AttackDeployBarScanner(adb, vision, root);
        var countReader = new TroopCountReader(adb, vision);
        _troops = new AttackTroopDeploymentStrategy(adb, _delays, countReader, _scanner);
        _spells = new AttackSpellDeploymentStrategy(adb, _delays, countReader, _scanner);
        _heroes = new AttackHeroDeploymentService(adb, _delays);
        _pipeline = new AttackPipeline(this);
    }

    public void Run(
        string attackStrategy = "Dragon_Attack",
        CancellationToken token = default,
        bool useEventTroops = false)
        => RunPipeline(attackStrategy, token, useEventTroops);

    public bool RunPipeline(
        string attackStrategy = "Dragon_Attack",
        CancellationToken token = default,
        bool useEventTroops = false)
    {
        AttackStageResult result = _pipeline.Execute(
            new AttackContext(attackStrategy, useEventTroops, token));
        return result.Status == AttackStageStatus.Succeeded;
    }

    public void UpdateTabs()
    {
        bool electro = _activeStrategy == "ElectroDragon_Attack";
        _bar = _scanner.Scan(electro, RequiredTabs(electro));
        ConfigureStrategies();
    }

    public void DeployTroops(string troopKey, CancellationToken token = default)
        => _troops.DeployTroop(troopKey.ToLowerInvariant(), token);

    public void EnsureTroopFullyDeployed(string troopKey, CancellationToken token = default)
    {
        string key = troopKey.ToLowerInvariant();
        bool electro = key == "e_drag";
        AttackDeployBarSnapshot currentBar = _scanner.Scan(electro, Array.Empty<string>());
        _troops.EnsureFullyDeployed(key, currentBar, token);
    }

    public void DeploySpells(string spellKey, CancellationToken token = default)
        => _spells.DeploySpell(spellKey.ToLowerInvariant(), token);

    public void DeployHeroes(CancellationToken token = default)
        => _heroes.Deploy(token);

    public void RetapHeroes(CancellationToken token = default)
        => _heroes.Activate(token);

    public bool DeployTroopsWithStrategy(string strategyName, CancellationToken token)
    {
        var strategy = new StandardBarchStrategy(_adb);
        AttackStageResult result = strategy.Deploy(new AttackContext(strategyName, false, token));
        return result.Status == AttackStageStatus.Succeeded;
    }

    public bool CastLightningSpells(CancellationToken token)
    {
        if (token.IsCancellationRequested) return false;
        Console.WriteLine("[ATTACKS] phase=cast_spells status=skip type=lightning reason=not_configured");
        return true;
    }

    AttackStageResult IAttackStageOperations.Prepare(AttackContext context)
    {
        _activeStrategy = context.NormalizedStrategy;
        _direction = _random.Next(2) == 0 ? "top_left" : "top_right";
        _coordinates = _coordinateProvider.GetCoordinates(_direction, _activeStrategy);
        bool electro = _activeStrategy == "ElectroDragon_Attack";
        _bar = _scanner.Scan(electro, RequiredTabs(electro));
        ConfigureStrategies();
        Console.WriteLine($"[ATTACK-CS] phase=prepare status=success strategy={_activeStrategy} direction={_direction}");
        return context.IsCancellationRequested ? AttackStageResult.Cancelled() : AttackStageResult.Success();
    }

    AttackStageResult IAttackStageOperations.DeployTroops(AttackContext context)
        => _troops.Deploy(context);

    AttackStageResult IAttackStageOperations.DeploySpells(AttackContext context)
        => _spells.Deploy(context);

    AttackStageResult IAttackStageOperations.ActivateHeroes(AttackContext context)
        => _heroes.DeployAndActivate(context);

    AttackStageResult IAttackStageOperations.Complete(AttackContext context)
    {
        string primary = context.NormalizedStrategy == "ElectroDragon_Attack" ? "e_drag" : "dragon";
        bool electro = primary == "e_drag";
        AttackDeployBarSnapshot currentBar = _scanner.Scan(electro, Array.Empty<string>());
        _troops.EnsureFullyDeployed(primary, currentBar, context.CancellationToken);
        return context.IsCancellationRequested ? AttackStageResult.Cancelled() : AttackStageResult.Success();
    }

    private void ConfigureStrategies()
    {
        if (_coordinates == null) return;
        _troops.Configure(_bar, _coordinates, _direction);
        _spells.Configure(_bar, _coordinates);
        _heroes.Configure(_bar, _coordinates);
    }

    private static IReadOnlyCollection<string> RequiredTabs(bool electro)
        => electro
            ? new[] { "e_drag", "balloon", "rage", "freeze" }
            : new[] { "dragon", "balloon", "rage", "freeze" };
}
