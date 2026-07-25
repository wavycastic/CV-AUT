using System;
using System.Text.Json;

namespace CvAut;

internal sealed record ArmySpec(
    string Main,
    string[] Troops,
    string[] Spells,
    string Siege);

internal sealed record ArmyState(
    bool ArmyReady,
    bool SpellsReady,
    bool SiegeReady,
    bool HeroesReady);

internal sealed record TrainingReadiness(
    bool IsReady,
    bool RebuildArmy,
    bool RebuildSpells,
    bool RebuildSiege,
    bool WaitForHeroes);

internal static class TrainingPlanResolver
{
    public static ArmySpec Resolve(JsonElement config, string? requestedStrategy)
    {
        string? strategy = requestedStrategy;
        if (string.IsNullOrWhiteSpace(strategy)
            && config.ValueKind == JsonValueKind.Object
            && config.TryGetProperty("attack", out JsonElement attack)
            && attack.ValueKind == JsonValueKind.String)
        {
            strategy = attack.GetString();
        }

        string normalized = (strategy ?? "Dragon_Attack")
            .Replace("_", string.Empty)
            .Replace(" ", string.Empty)
            .ToLowerInvariant();
        return normalized.Contains("electrodragon", StringComparison.Ordinal)
            ? new ArmySpec("electro_dragon", new[] { "electro_dragon", "balloon" }, new[] { "rage", "freeze" }, "slammer")
            : new ArmySpec("dragon", new[] { "dragon", "balloon" }, new[] { "rage", "freeze" }, "slammer");
    }
}
