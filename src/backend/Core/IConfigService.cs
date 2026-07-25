using System.Text.Json;
using CvAut.Configuration;

namespace CvAut;

public interface IConfigService : IConfigSnapshotProvider
{
    // TODO(config-migration): Temporary migration surface for raw JSON configuration access.
    // Migrate consumers to typed snapshot properties and remove raw JsonElement access.
    [Obsolete("Temporary migration surface for raw JSON configuration access. Prefer typed snapshot properties.")]
    JsonElement Config { get; }
    AutomationConfigSnapshot Snapshot => Current;
    DeviceConnectionConfig DeviceConnection => Current.DeviceConnection;
    RunSessionConfig RunSession => Current.RunSession;
}
