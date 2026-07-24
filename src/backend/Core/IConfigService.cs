using CvAut.Configuration;

namespace CvAut;

public interface IConfigService : IConfigSnapshotProvider
{
    AutomationConfigSnapshot Snapshot => Current;
    DeviceConnectionConfig DeviceConnection => Current.DeviceConnection;
    RunSessionConfig RunSession => Current.RunSession;
}
