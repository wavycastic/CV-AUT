using System.Text.Json;
using CvAut.Configuration;

namespace CvAut;

public interface IConfigService
{
    JsonElement Config { get; }

    DeviceConnectionConfig DeviceConnection
        => DeviceConnectionConfigReader.Read(Config);

    void Reload();
}
