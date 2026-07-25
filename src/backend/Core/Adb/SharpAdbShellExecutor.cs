using System;
using SharpAdbClient;

namespace CvAut.Adb
{
    /// <summary>
    /// SharpAdbClient-backed Android shell executor.
    /// </summary>
    internal sealed class SharpAdbShellExecutor : IAdbShellExecutor
    {
        private readonly DeviceData _device;

        public SharpAdbShellExecutor(DeviceData device)
        {
            _device = device ?? throw new ArgumentNullException(nameof(device));
        }

        public string Execute(string command)
        {
            if (string.IsNullOrWhiteSpace(command))
            {
                return "Error: Shell command is required";
            }

            try
            {
                var receiver = new ConsoleOutputReceiver();
                AdbClient.Instance.ExecuteRemoteCommand(command, _device, receiver);
                return receiver.ToString().Trim();
            }
            catch (Exception ex)
            {
                return $"Error: {ex.Message}";
            }
        }
    }
}
