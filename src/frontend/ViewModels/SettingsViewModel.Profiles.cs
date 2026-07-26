using CommunityToolkit.Mvvm.Input;
using CvAut.Models;

namespace CvAut.ViewModels
{
    /// <summary>
    /// Profile CRUD for the settings page: loading a profile into the four village view models,
    /// saving it back, and keeping the profile list and play mode in sync with the config store.
    /// </summary>
    public partial class SettingsViewModel
    {
        private bool _syncingProfiles;

        [RelayCommand]
        private void LoadProfile()
        {
            LoadSelectedProfile();
        }

        partial void OnSelectedProfileChanged(BotProfile? value)
        {
            if (!_syncingProfiles && value is not null && value.Name != _configStore.ActiveProfileName)
            {
                LoadSelectedProfile();
            }
        }

        private void LoadSelectedProfile()
        {
            if (SelectedProfile is null)
            {
                return;
            }

            _configStore.LoadProfile(SelectedProfile.Name);
            ProfileName = _configStore.ActiveProfileName;
            _mainVillage.Reload();
            _nightVillage.Reload();
            _clanGames.Reload();
            _clanCapital.Reload();
            RefreshProfiles();
            SyncPlayModeFromConfig();
            Status = "Đã tải cấu hình " + ProfileName;
        }

        [RelayCommand]
        private void SaveNewProfile()
        {
            var config = _configStore.LoadActiveConfig();
            _mainVillage.ApplyTo(config);
            _nightVillage.ApplyTo(config);
            _clanGames.ApplyTo(config);
            _clanCapital.ApplyTo(config);
            _configStore.SaveProfileAs(ProfileName, config);
            RefreshProfiles();
            Status = "Đã lưu cấu hình " + _configStore.ActiveProfileName;
        }

        [RelayCommand]
        private void UpdateProfile()
        {
            var config = _configStore.LoadActiveConfig();
            _mainVillage.ApplyTo(config);
            _nightVillage.ApplyTo(config);
            _clanGames.ApplyTo(config);
            _clanCapital.ApplyTo(config);
            _configStore.SaveActiveConfig(config);
            RefreshProfiles();
            Status = "Đã cập nhật cấu hình " + _configStore.ActiveProfileName;
        }

        [RelayCommand]
        private void DeleteProfile()
        {
            if (SelectedProfile is null)
            {
                return;
            }

            string deletedName = SelectedProfile.Name;
            _configStore.DeleteProfile(deletedName);
            RefreshProfiles();
            _mainVillage.Reload();
            _nightVillage.Reload();
            _clanGames.Reload();
            _clanCapital.Reload();
            Status = "Đã xóa cấu hình " + deletedName;
        }

        private void SyncPlayModeFromConfig()
        {
            try
            {
                var config = _configStore.LoadActiveConfig();
                if (config.TryGetPropertyValue("play_mode", out var val) && val is not null)
                {
                    SelectedPlayMode = PlayMode.ToDisplay(val.ToString());
                }
                else
                {
                    SelectedPlayMode = PlayMode.MainVillageLabel;
                }
            }
            catch
            {
                SelectedPlayMode = PlayMode.MainVillageLabel;
            }
        }

        private void RefreshProfiles()
        {
            _syncingProfiles = true;
            try
            {
                Profiles.Clear();
                foreach (BotProfile profile in _configStore.Profiles)
                {
                    Profiles.Add(profile);
                    if (profile.Name == _configStore.ActiveProfileName)
                    {
                        SelectedProfile = profile;
                        ProfileName = profile.Name;
                    }
                }
            }
            finally
            {
                _syncingProfiles = false;
            }
        }

        public void LoadSelectedProfileDirectly(string name, string playMode = "")
        {
            _mainVillage.Reload();
            _nightVillage.Reload();
            _clanGames.Reload();
            _clanCapital.Reload();
            RefreshProfiles();

            // After RefreshProfiles, which rewrites ProfileName to the active profile name.
            ProfileName = name;

            SelectedPlayMode = string.IsNullOrEmpty(playMode) ? PlayMode.MainVillageLabel : playMode;
            RebuildTabsByPlayMode(SelectedPlayMode);

            Status = "Đã tải cấu hình " + name;
        }

        public void UpdateProfileDirectly()
        {
            var config = _configStore.LoadActiveConfig();
            config["play_mode"] = PlayMode.ToToken(SelectedPlayMode);
            _mainVillage.ApplyTo(config);
            _nightVillage.ApplyTo(config);
            _clanGames.ApplyTo(config);
            _clanCapital.ApplyTo(config);
            _configStore.SaveActiveConfig(config);
            RefreshProfiles();
            Status = "Đã cập nhật cấu hình " + _configStore.ActiveProfileName;
        }
    }
}
