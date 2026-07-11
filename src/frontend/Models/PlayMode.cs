namespace CvAut.Models
{
    /// <summary>
    /// Two-way mapping between the play-mode config token (e.g. "main_village") and its
    /// Vietnamese display label. Centralised so DeviceViewModel and any config reader map
    /// identically instead of duplicating switch statements that can drift.
    /// </summary>
    public static class PlayMode
    {
        public const string MainVillageLabel = "Làng chính";
        public const string NightVillageLabel = "Làng đêm";
        public const string ClanGamesLabel = "Trò chơi hội (sắp ra mắt)";
        public const string ClanCapitalLabel = "Kinh đô hội (sắp ra mắt)";

        /// <summary>Config token → display label. Unknown tokens fall back to Main Village.</summary>
        public static string ToDisplay(string? token) => token switch
        {
            "main_village" => MainVillageLabel,
            "night_village" => NightVillageLabel,
            "clan_games" => ClanGamesLabel,
            "clan_capital" => ClanCapitalLabel,
            _ => MainVillageLabel,
        };

        /// <summary>Display label → config token. Unknown labels fall back to main_village.</summary>
        public static string ToToken(string? label) => label switch
        {
            NightVillageLabel => "night_village",
            ClanGamesLabel => "clan_games",
            ClanCapitalLabel => "clan_capital",
            _ => "main_village",
        };
    }
}