namespace CvAut.Models
{
    /// <summary>
    /// Coarse UI state for the Dashboard page. Consolidates what used to be scattered flags
    /// (<c>HasDetected</c>, <c>DeviceCount</c>, <c>ActiveDevice != null</c>, <c>IsRunning</c>...)
    /// into a single value so the view can bind visibility/enabled state from one source of
    /// truth and the page never goes blank mid-detect or after navigation (item 10).
    ///
    /// Transitions are driven by the shell host (<c>MainWindowViewModel</c>):
    /// <list type="bullet">
    /// <item><c>Idle</c> → <c>Detecting</c> when Detect starts.</item>
    /// <item><c>Detecting</c> → <c>NoDevices</c> (0 found) | <c>DeviceSelected</c> (1 ready
    ///   auto-selected, or multiple/zero ready awaiting user pick).</item>
    /// <item><c>DeviceSelected</c> ↔ <c>Running</c> ↔ <c>Paused</c> ↔ <c>Error</c> as the
    ///   active device's <see cref="BotStatus"/> changes.</item>
    /// </list>
    /// </summary>
    public enum DashboardDeviceState
    {
        /// <summary>No Detect has run yet — initial hint ("No device yet").</summary>
        Idle,

        /// <summary>DiscoverAsync is in flight — show a loading/detecting indicator.</summary>
        Detecting,

        /// <summary>Last Detect returned zero devices — show the "No device found" panel.</summary>
        NoDevices,

        /// <summary>
        /// Devices were detected and at least one is listed; either an active device is
        /// selected (panel rendered) or the user must pick from the list. The bot is not
        /// actively running (Idle/Stopped/Starting/Stopping).
        /// </summary>
        DeviceSelected,

        /// <summary>The active device's bot is running.</summary>
        Running,

        /// <summary>The active device's bot is paused.</summary>
        Paused,

        /// <summary>The active device's bot errored.</summary>
        Error,

        /// <summary>The user is configuring the settings for a specific device.</summary>
        ConfiguringDevice,
    }
}
