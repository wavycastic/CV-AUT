namespace CvAut.Adb
{
    /// <summary>
    /// Executes bundled ADB commands without exposing process-management details to callers.
    /// </summary>
    internal interface IAdbCommandRunner
    {
        string AdbExePath { get; }

        string RunAdbCommand(string deviceAddress, string arguments);

        string RunRawAdbCommand(string arguments);
    }
}
