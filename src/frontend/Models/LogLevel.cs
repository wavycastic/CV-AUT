namespace CvAut.Models
{
    public enum LogLevel
    {
        Trace = -1,
        Debug = 0,
        Info = 1,
        Warning = 2,
        Error = 3,
        Critical = 4
    }

    public static class LogLevelExtensions
    {
        /// <summary>Three-letter tag used by the console-style log rows.</summary>
        public static string ToShortText(this LogLevel level) => level switch
        {
            LogLevel.Trace => "TRC",
            LogLevel.Debug => "DBG",
            LogLevel.Info => "INF",
            LogLevel.Warning => "WRN",
            LogLevel.Error => "ERR",
            LogLevel.Critical => "CRT",
            _ => level.ToString().ToUpperInvariant(),
        };
    }
}
