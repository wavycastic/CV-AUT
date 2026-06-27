namespace CvAut.Models
{
    /// <summary>
    /// Lifecycle/health state of a single device's bot session. Device-scoped — never global.
    /// </summary>
    public enum BotStatus
    {
        Idle,
        Starting,
        Running,
        Paused,
        Stopping,
        Stopped,
        Error,
    }
}
