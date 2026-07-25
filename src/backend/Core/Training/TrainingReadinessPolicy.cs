namespace CvAut;

internal sealed class TrainingReadinessPolicy
{
    public TrainingReadiness Evaluate(ArmyState state, bool waitForHeroes = false)
    {
        bool heroesBlock = waitForHeroes && !state.HeroesReady;
        return new TrainingReadiness(
            IsReady: state.ArmyReady && state.SpellsReady && state.SiegeReady && !heroesBlock,
            RebuildArmy: !state.ArmyReady,
            RebuildSpells: !state.SpellsReady,
            RebuildSiege: !state.SiegeReady,
            WaitForHeroes: heroesBlock);
    }
}
