using System.Threading;

namespace CvAut
{
    /// <summary>
    /// Chịu trách nhiệm duy nhất: các lệnh chạy một lần dựng riêng một
    /// <see cref="CVAutomationFramework"/> từ tệp cấu hình để thử nghiệm thủ công.
    /// </summary>
    internal static class WorkflowTestCommands
    {
        public static void ZoomOut(string configPath)
        {
            new CVAutomationFramework(configPath).ZoomOut();
        }

        public static void BootRecovery(string configPath)
        {
            new CVAutomationFramework(configPath).BootRecovery();
        }

        public static void RunWorkflowTemplate(string configPath, int cycleCount, CancellationToken token)
        {
            new CVAutomationFramework(configPath).RunCyclesForTest(cycleCount, token);
        }
    }
}
