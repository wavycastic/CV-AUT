using System;
using System.IO;
using CvAut;
using CvAut.Services;
using CvAut.ViewModels;
using CvAut.ViewModels.Settings;
using Xunit;

namespace CvAut.Tests
{
    public class AppPreferencesTests
    {
        [Fact]
        public void JsonPreferences_RoundTripsSelectedEmulatorFilter()
        {
            string root = Path.Combine(Path.GetTempPath(), "cvaut_prefs_" + Guid.NewGuid().ToString("N"));
            var preferences = new JsonAppPreferences(Path.Combine(root, "app_settings.json"));

            preferences.SaveSelectedEmulatorFilter("LDPlayer");

            Assert.Equal("LDPlayer", preferences.LoadSelectedEmulatorFilter());
        }

        [Fact]
        public void JsonPreferences_InvalidJson_FallsBackToBlueStacks()
        {
            string root = Path.Combine(Path.GetTempPath(), "cvaut_prefs_" + Guid.NewGuid().ToString("N"));
            string path = Path.Combine(root, "app_settings.json");
            Directory.CreateDirectory(root);
            File.WriteAllText(path, "not-json");

            var preferences = new JsonAppPreferences(path);

            Assert.Equal("BlueStacks", preferences.LoadSelectedEmulatorFilter());
        }

        [Fact]
        public void Dashboard_UsesInjectedPreferences()
        {
            string root = Path.Combine(Path.GetTempPath(), "cvaut_dash_prefs_" + Guid.NewGuid().ToString("N"));
            var preferences = new RecordingPreferences("LDPlayer");
            var dashboard = new DashboardViewModel(
                new SettingsViewModel(),
                new ConfigStore(root),
                preferences);

            Assert.Equal("LDPlayer", dashboard.SelectedEmulatorFilter);

            dashboard.SelectedEmulatorFilter = "MEmu";

            Assert.Equal("MEmu", preferences.SavedFilter);
        }

        private sealed class RecordingPreferences : IAppPreferences
        {
            private readonly string _loadedFilter;

            public RecordingPreferences(string loadedFilter)
            {
                _loadedFilter = loadedFilter;
            }

            public string? SavedFilter { get; private set; }

            public string LoadSelectedEmulatorFilter() => _loadedFilter;

            public void SaveSelectedEmulatorFilter(string filter)
            {
                SavedFilter = filter;
            }
        }
    }
}
