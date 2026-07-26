using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CvAut.Configuration;
using CvAut.Models;
using CvAut.Services;
using CvAut.Services.Configuration;
using CvAut.Services.Sessions;

namespace CvAut.ViewModels
{
    /// <summary>
    /// Which village this device plays. The label shown in the dropdown is translated to the typed
    /// mode the config layer persists, and it is loaded from and saved to the device's own profile
    /// so the choice survives a restart.
    /// </summary>
    public partial class DeviceViewModel
    {
        [ObservableProperty]
        private string _selectedPlayMode = "Làng chính";

        public ObservableCollection<string> PlayModes { get; } = new()
        {
            "Làng chính", "Làng đêm", "Trò chơi hội (sắp ra mắt)", "Kinh đô hội (sắp ra mắt)"
        };

        /// <summary>The UI label mapped onto the typed play mode the config layer persists.</summary>
        private VillagePlayMode SelectedVillagePlayMode
            => ProfileConfigSnapshotProvider.ParsePlayMode(Models.PlayMode.ToToken(SelectedPlayMode));

        private void LoadSelectedPlayMode()
        {
            string profileName = Device.ProfileKey;
            try
            {
                if (_configStore.Profiles.Any(p => string.Equals(p.Name, profileName, StringComparison.OrdinalIgnoreCase)))
                {
                    SelectedPlayMode = Models.PlayMode.ToDisplay(
                        ProfileConfigSnapshotProvider.ToToken(_configSnapshots.LoadPlayMode(profileName)));
                }
            }
            catch
            {
                SelectedPlayMode = "Làng chính";
            }
        }

        partial void OnSelectedPlayModeChanged(string value)
        {
            try
            {
                _configSnapshots.SavePlayMode(
                    Device.ProfileKey,
                    ProfileConfigSnapshotProvider.ParsePlayMode(Models.PlayMode.ToToken(value)));
            }
            catch
            {
                // Best effort
            }
        }
    }
}
