using System;
using System.IO;
using System.Text.Json.Nodes;

namespace CvAut.Services
{
    /// <summary>
    /// Stores application-scoped UI preferences. View models consume this abstraction instead
    /// of reading or writing files directly.
    /// </summary>
    public interface IAppPreferences
    {
        string LoadSelectedEmulatorFilter();
        void SaveSelectedEmulatorFilter(string filter);
    }

    /// <summary>
    /// JSON-backed application preferences stored under the current user's local app data.
    /// </summary>
    public sealed class JsonAppPreferences : IAppPreferences
    {
        private const string DefaultEmulatorFilter = "BlueStacks";
        private readonly string _path;

        public JsonAppPreferences()
            : this(Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "AutoClashOfClan20206",
                "app_settings.json"))
        {
        }

        /// <summary>Test/override constructor using an explicit preferences file path.</summary>
        public JsonAppPreferences(string path)
        {
            _path = path;
        }

        public string LoadSelectedEmulatorFilter()
        {
            try
            {
                if (File.Exists(_path) &&
                    JsonNode.Parse(File.ReadAllText(_path)) is JsonObject obj &&
                    obj.TryGetPropertyValue("SelectedEmulatorFilter", out JsonNode? value) &&
                    value is not null)
                {
                    string filter = value.ToString();
                    return string.IsNullOrWhiteSpace(filter) ? DefaultEmulatorFilter : filter;
                }
            }
            catch
            {
                // Corrupt or inaccessible preferences fall back to the safe default.
            }

            return DefaultEmulatorFilter;
        }

        public void SaveSelectedEmulatorFilter(string filter)
        {
            try
            {
                string? directory = Path.GetDirectoryName(Path.GetFullPath(_path));
                if (!string.IsNullOrWhiteSpace(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                var obj = new JsonObject
                {
                    ["SelectedEmulatorFilter"] = filter
                };
                File.WriteAllText(_path, obj.ToJsonString());
            }
            catch
            {
                // Preferences are best-effort and must never block the dashboard.
            }
        }
    }
}
