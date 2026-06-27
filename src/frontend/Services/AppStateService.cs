using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CvAut.Models;

namespace CvAut.Services
{
    /// <summary>
    /// App-level (NOT runtime-device) state: known devices, the active device id, theme/lang.
    /// Registered as a DI singleton. Per the architecture rules this holds only app-scoped state —
    /// status/stats/logs live in the per-device view models, never here.
    /// </summary>
    public sealed partial class AppStateService : ObservableObject
    {
        [ObservableProperty]
        private string? _activeDeviceId;

        [ObservableProperty]
        private bool _isGridMode;

        /// <summary>Known/configured devices (app-scoped registry, not runtime session state).</summary>
        public ObservableCollection<Device> Devices { get; } = new();
    }
}
