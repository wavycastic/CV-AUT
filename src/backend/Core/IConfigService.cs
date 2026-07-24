using System.Text.Json;

namespace CvAut;

public interface IConfigService
{
    JsonElement Config { get; }
    void Reload();
}
