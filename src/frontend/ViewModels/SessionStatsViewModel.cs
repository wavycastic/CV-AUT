using CommunityToolkit.Mvvm.ComponentModel;

namespace CvAut.ViewModels
{
    /// <summary>
    /// UI-facing projection of <see cref="CvAut.Models.SessionStats"/> for binding in
    /// <c>DevicePanelView</c>. Lives inside <see cref="DeviceViewModel"/> (device-scoped).
    /// </summary>
    public sealed partial class SessionStatsViewModel : ViewModelBase
    {
        [ObservableProperty] private int _battles;
        [ObservableProperty] private int _stars;
        [ObservableProperty] private long _gold;
        [ObservableProperty] private long _elixir;
        [ObservableProperty] private long _darkElixir;
        [ObservableProperty] private int _wallsUpgraded;
        [ObservableProperty] private int _clanGamesPoints;

        /// <summary>Replaces all totals from a backend <see cref="CvAut.Models.SessionStats"/> snapshot.</summary>
        public void Apply(CvAut.Models.SessionStats stats)
        {
            Battles = stats.Battles;
            Stars = stats.Stars;
            Gold = stats.Gold;
            Elixir = stats.Elixir;
            DarkElixir = stats.DarkElixir;
            WallsUpgraded = stats.WallsUpgraded;
            ClanGamesPoints = stats.ClanGamesPoints;
        }
    }
}
