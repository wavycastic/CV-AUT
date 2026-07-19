using Avalonia.Controls;
using Avalonia.Controls.Templates;
using CvAut.ViewModels;
using CvAut.ViewModels.Settings;
using CvAut.Views;
using CvAut.Views.Settings;

namespace CvAut
{
    public class ViewLocator : IDataTemplate
    {
        public Control? Build(object? param)
        {
            if (param is null)
                return null;

            return param switch
            {
                MainWindowViewModel => new MainWindow(),
                DashboardViewModel => new DashboardView(),
                SettingsViewModel => new SettingsView(),
                AdvancedViewModel => new AdvancedView(),
                LogsViewModel => new LogsView(),
                LicenseViewModel => new LicenseView(),
                DeviceViewModel => null,
                TopBarViewModel => new HeaderView(),
                SidebarViewModel => new BottomNavView(),
                MainVillageViewModel => new MainVillageView(),
                NightVillageViewModel => new NightVillageView(),
                ClanGamesViewModel => new ClanGamesView(),
                ClanCapitalViewModel => new ClanCapitalView(),
                _ => new TextBlock { Text = "Not Found: " + param.GetType().FullName },
            };
        }

        public bool Match(object? data)
        {
            return data is ViewModelBase;
        }
    }
}
