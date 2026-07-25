namespace CvAut.Adb
{
    /// <summary>
    /// Executes Android shell commands for one selected device.
    /// </summary>
    internal interface IAdbShellExecutor
    {
        string Execute(string command);
    }
}
