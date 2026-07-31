namespace CvAut;

internal sealed class TrainingReadinessPolicy
{
    public TrainingReadiness Evaluate(ArmyState state, bool waitForHeroes = false)
    {
        bool heroesBlock = waitForHeroes && !state.HeroesReady;
        return new TrainingReadiness(
            IsReady: state.Army == TrainingDetectionState.Ready
                && state.Spells == TrainingDetectionState.Ready
                && state.Siege == TrainingDetectionState.Ready
                && !heroesBlock,
            RebuildArmy: state.Army == TrainingDetectionState.NotReady,
            RebuildSpells: state.Spells == TrainingDetectionState.NotReady,
            RebuildSiege: state.Siege == TrainingDetectionState.NotReady,
            WaitForHeroes: heroesBlock);
    }
}
