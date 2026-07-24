using System.Text.Json;
using CvAut.Configuration;

namespace CvAut;

public interface IConfigService
{
    JsonElement Config { get; }

    AutomationConfigSnapshot Snapshot
        => AutomationConfigSnapshotReader.Read(Config);

    DeviceConnectionConfig DeviceConnection
        => Snapshot.DeviceConnection;

    RunSessionConfig RunSession
        => Snapshot.RunSession;

    void Reload();
}
