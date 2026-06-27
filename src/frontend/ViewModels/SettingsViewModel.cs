using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CvAut.ViewModels.Settings;

namespace CvAut.ViewModels
{
    /// <summary>
    /// Settings page host: tabs for Main Village / Night Village / Clan Games. Each tab binds a
    /// sub view-model. Phase 1 ships skeletons; Phase 2 fills the real config fields.
    /// </summary>
    public partial class SettingsViewModel : ViewModelBase
    {
        [ObservableProperty] private string _title = "Settings";

        public ObservableCollection<SettingsTab> Tabs { get; } = new();

        [ObservableProperty] private SettingsTab? _selectedTab;

        public SettingsViewModel(MainVillageViewModel mainVillage, NightVillageViewModel nightVillage, ClanGamesViewModel clanGames)
        {
            Tabs.Add(new SettingsTab("Main Village", "Home", mainVillage));
            Tabs.Add(new SettingsTab("Night Village", "MoonWaningCrescent", nightVillage));
            Tabs.Add(new SettingsTab("Clan Games", "SwordCross", clanGames));
            SelectedTab = Tabs[0];
        }

        /// <summary>Design-time ctor.</summary>
        public SettingsViewModel() : this(new MainVillageViewModel(), new NightVillageViewModel(), new ClanGamesViewModel())
        {
        }
    }

    /// <summary>One settings tab: label, icon kind, and the sub view-model.</summary>
    public sealed partial class SettingsTab : ObservableObject
    {
        public string Title { get; }
        public string IconKind { get; }
        public ViewModelBase Page { get; }

        public SettingsTab(string title, string iconKind, ViewModelBase page)
        {
            Title = title;
            IconKind = iconKind;
            Page = page;
        }
    }
}
