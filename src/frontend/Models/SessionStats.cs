namespace CvAut.Models
{
    /// <summary>
    /// Per-device running totals surfaced to the UI. Device-scoped — one instance per session,
    /// never a shared singleton.
    /// </summary>
    public sealed class SessionStats
    {
        public int Battles { get; set; }

        public int Stars { get; set; }

        public long Gold { get; set; }

        public long Elixir { get; set; }

        public long DarkElixir { get; set; }

        public int WallsUpgraded { get; set; }

        public int ClanGamesPoints { get; set; }

        public int ClanGamesTasks { get; set; }
    }
}
