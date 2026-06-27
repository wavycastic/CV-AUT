using System;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using CvAut.ViewModels;
using CvAut.ViewModels.Settings;
using CvAut.Views;
using CvAut.Views.Settings;

namespace CvAut
{
    /// <summary>
    /// Maps a view model to its view. Uses a static switch instead of reflection so it
    /// is fully Native AOT / trimming safe (the default Avalonia template relies on
    /// Type.GetType/Activator.CreateInstance which gets trimmed away under AOT).
    /// </summary>
    public class ViewLocator : IDataTemplate
    {
        public Control? Build(object? param)
        {
            if (param is null)
                return null;

            return param switch
            {
                MainWindowViewModel => new MainWindow(),
                ShellViewModel => new ShellView(),
                DashboardViewModel => new DashboardView(),
                SettingsViewModel => new SettingsView(),
                AccountsViewModel => new AccountsView(),
                AdvancedViewModel => new AdvancedView(),
                LogsViewModel => new LogsView(),
                LicenseViewModel => new LicenseView(),
                DeviceViewModel => new DevicePanelView(),
                TopBarViewModel => new TopBarView(),
                SidebarViewModel => new SidebarView(),
                MainVillageViewModel => new MainVillageView(),
                NightVillageViewModel => new NightVillageView(),
                ClanGamesViewModel => new ClanGamesView(),
                _ => new TextBlock { Text = "Not Found: " + param.GetType().FullName },
            };
        }

        public bool Match(object? data)
        {
            return data is ViewModelBase;
        }
    }
}
