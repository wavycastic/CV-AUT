namespace CvAut.Models
{
    /// <summary>
    /// Display-only projections of the parsed line. The module tag was parsed from the very
    /// first version but never shown, so nothing on screen told the user which subsystem a
    /// row came from.
    /// </summary>
    public sealed partial class LogEntry
    {
        /// <summary>Vietnamese name of the subsystem that produced this line.</summary>
        public string ModuleLabel => LogVocabulary.TranslateModule(Module);

        /// <summary>Bracketed form used by the log list column.</summary>
        public string FormattedModuleLabel => $"[{ModuleLabel}]";
    }
}
