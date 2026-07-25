using System;
using System.Collections.Generic;
using System.Threading;

namespace CvAut.AttackPipelines;

internal sealed class AttackContext
{
    private readonly List<string> _completedStages = new();

    public AttackContext(
        string requestedStrategy,
        bool useEventTroops,
        CancellationToken cancellationToken)
    {
        RequestedStrategy = string.IsNullOrWhiteSpace(requestedStrategy)
            ? "Dragon_Attack"
            : requestedStrategy.Trim();
        NormalizedStrategy = NormalizeStrategy(RequestedStrategy);
        UseEventTroops = useEventTroops;
        CancellationToken = cancellationToken;
        StartedAt = DateTimeOffset.UtcNow;
    }

    public string RequestedStrategy { get; }
    public string NormalizedStrategy { get; }
    public bool UseEventTroops { get; }
    public CancellationToken CancellationToken { get; }
    public DateTimeOffset StartedAt { get; }
    public string CurrentStage { get; internal set; } = string.Empty;
    public string? FailureReason { get; internal set; }
    public IReadOnlyList<string> CompletedStages => _completedStages;
    public bool IsCancellationRequested => CancellationToken.IsCancellationRequested;

    internal void MarkCompleted(string stageName) => _completedStages.Add(stageName);

    private static string NormalizeStrategy(string strategy)
    {
        string normalized = strategy.Replace("-", string.Empty)
            .Replace("_", string.Empty)
            .Replace(" ", string.Empty)
            .ToLowerInvariant();
        return normalized switch
        {
            "electrodragonattack" or "edragattack" or "electrodragon" or "edrag"
                => "ElectroDragon_Attack",
            _ => "Dragon_Attack"
        };
    }
}
