using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace CvAut.ViewModels
{
    /// <summary>
    /// One sidebar navigation entry: label, Material.Icons kind, and the page view model it
    /// activates. Kept as a simple observable record so the sidebar list binds cleanly.
    /// </summary>
    public sealed partial class NavItem : ObservableObject
    {
        public string Title { get; }

        public string IconKind { get; }

        public ViewModelBase Page { get; }

        [ObservableProperty] private bool _isActive;

        public NavItem(string title, string iconKind, ViewModelBase page)
        {
            Title = title;
            IconKind = iconKind;
            Page = page;
        }
    }

    /// <summary>
    /// Sidebar navigation state. Owns the page list and the current page (rendered in the shell's
    /// <c>ContentControl</c>). <see cref="NavigateCommand"/> swaps <see cref="CurrentPage"/> and
    /// flips the active flag on the matching <see cref="NavItem"/>.
    /// </summary>
    public partial class SidebarViewModel : ViewModelBase
    {
        public ObservableCollection<NavItem> Items { get; } = new();

        [ObservableProperty]
        private ViewModelBase? _currentPage;

        public SidebarViewModel()
        {
        }

        [RelayCommand]
        private void Navigate(NavItem item)
        {
            foreach (NavItem i in Items)
            {
                i.IsActive = false;
            }

            item.IsActive = true;
            CurrentPage = item.Page;
        }

        /// <summary>Seed the nav list and activate the first page. Called once by the shell VM.</summary>
        public void Seed(System.Collections.Generic.IEnumerable<NavItem> items)
        {
            Items.Clear();
            bool first = true;
            foreach (NavItem item in items)
            {
                Items.Add(item);
                if (first)
                {
                    item.IsActive = true;
                    CurrentPage = item.Page;
                    first = false;
                }
            }
        }
    }
}
