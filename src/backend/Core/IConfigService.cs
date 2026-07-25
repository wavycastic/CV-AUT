using System.Text.Json;
using CvAut.Configuration;

namespace CvAut;

public interface IConfigService : IConfigSnapshotProvider
{
    JsonElement Config { get; }
    AutomationConfigSnapshot Snapshot => Current;
    DeviceConnectionConfig DeviceConnection => Current.DeviceConnection;
    RunSessionConfig RunSession => Current.RunSession;
}
