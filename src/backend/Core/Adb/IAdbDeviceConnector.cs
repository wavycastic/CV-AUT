using SharpAdbClient;

namespace CvAut.Adb
{
    internal sealed record AdbDeviceConnection(
        string Host,
        int Port,
        string DeviceAddress,
        DeviceData Device,
        bool IsConnected);

    internal interface IAdbDeviceConnector
    {
        AdbDeviceConnection Connect(string host, int port, string? preferredSerial = null);
    }
}
