using CommunityToolkit.Mvvm.ComponentModel;

namespace CvAut.ViewModels
{
    /// <summary>One entry of the settings tab strip: a title, a Material icon kind, and the page
    /// view model rendered when the tab is selected.</summary>
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
