using System.Collections.Generic;
using System.Threading;

namespace CvAut
{
    /// <summary>
    /// Mặt tiền (facade) tập hợp các tiện ích chẩn đoán cho lớp UI.
    /// Bản thân lớp này không chứa logic; mọi trách nhiệm được uỷ quyền cho
    /// <see cref="AdbDeviceProbe"/>, <see cref="EmulatorDisplayProbe"/>,
    /// <see cref="VisionTestHarness"/> và <see cref="WorkflowTestCommands"/>.
    /// </summary>
    public static class BackendDiagnostics
    {
        public static byte[] LoadTemplatePngBytes(string templatesRoot, string relativePath)
        {
            return TemplateAssetLoader.LoadPngBytes(templatesRoot, relativePath);
        }

        /// <inheritdoc cref="AdbDeviceProbe.ListDevices"/>
        public static IReadOnlyList<string> ListAdbDevices() => AdbDeviceProbe.ListDevices();

        /// <inheritdoc cref="AdbDeviceProbe.ListDevicesWithStatus"/>
        public static IReadOnlyList<(string Serial, string State)> ListAdbDevicesWithStatus() => AdbDeviceProbe.ListDevicesWithStatus();

        /// <inheritdoc cref="EmulatorDisplayProbe.GetDisplayInfo"/>
        public static (int Width, int Height, int DensityDpi, string Raw) GetEmulatorDisplayInfo(string host, int port, string? serial = null)
            => EmulatorDisplayProbe.GetDisplayInfo(host, port, serial);

        public static void DiagnoseSavedArmyWindow(string outputPath, string templatesPath)
        {
            Training.DiagnoseSavedArmyWindow(outputPath, templatesPath);
        }

        /// <inheritdoc cref="VisionTestHarness.RunOfflineMockTest"/>
        public static void RunOfflineMockTest(string templatesPath) => VisionTestHarness.RunOfflineMockTest(templatesPath);

        /// <inheritdoc cref="VisionTestHarness.RunLiveScoutingTest"/>
        public static void RunLiveScoutingTest(string templatesPath, string debugPath) => VisionTestHarness.RunLiveScoutingTest(templatesPath, debugPath);

        /// <inheritdoc cref="VisionTestHarness.RunLiveHomeBaseTest"/>
        public static void RunLiveHomeBaseTest(string templatesPath, string debugPath) => VisionTestHarness.RunLiveHomeBaseTest(templatesPath, debugPath);

        /// <inheritdoc cref="VisionTestHarness.RunSmartTrainTest"/>
        public static void RunSmartTrainTest(string configPath, string templatesPath) => VisionTestHarness.RunSmartTrainTest(configPath, templatesPath);

        /// <inheritdoc cref="WorkflowTestCommands.ZoomOut"/>
        public static void ZoomOut(string configPath) => WorkflowTestCommands.ZoomOut(configPath);

        /// <inheritdoc cref="WorkflowTestCommands.BootRecovery"/>
        public static void BootRecovery(string configPath) => WorkflowTestCommands.BootRecovery(configPath);

        /// <inheritdoc cref="WorkflowTestCommands.RunWorkflowTemplate"/>
        public static void RunWorkflowTemplate(string configPath, int cycleCount, CancellationToken token)
            => WorkflowTestCommands.RunWorkflowTemplate(configPath, cycleCount, token);
    }
}
