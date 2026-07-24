namespace CvAut;

public interface IStatsRepository
{
    void UpdateStats(int villageIdx, int starsGot, (int Gold, int Elixir, int DarkElixir) gained);
}
