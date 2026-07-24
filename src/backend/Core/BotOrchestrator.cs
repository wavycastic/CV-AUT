using System;
using System.Threading;
using System.Threading.Tasks;

namespace CvAut;

public sealed class BotOrchestrator : IAutomationRunner
{
    private readonly CVAutomationFramework _framework;

    internal ConfigService Config { get; }
    internal StatsRepository Stats { get; }
    internal PopupHandlerService Popups { get; }
    internal ZoomService Zoom { get; }
    internal AccountSwitcher Accounts { get; }

    public Task Completion => _framework.Completion;

    public BotOrchestrator(string configPath = "Config/test_config.json")
    {
        var configService = new ConfigService(configPath);
        Config = configService;
        var cfg = configService.Config;
        var devConfig = ConfigManager.GetObjectOrDefault(cfg, "device_connection");
        string host = ConfigManager.GetStringOrDefault(devConfig, "host", "127.0.0.1");
        int port = ConfigManager.GetIntOrDefault(devConfig, "port", 5556);

        var adb = new ADBHelper(host, port);
        string templatesPath = System.IO.Path.Combine(AppContext.BaseDirectory, "assets", "Templates");
        var vision = new VisionEngine(templatesPath);

        Popups = new PopupHandlerService(adb, vision, templatesPath);
        Zoom = new ZoomService(adb);
        Stats = new StatsRepository(adb, vision, templatesPath);
        Accounts = new AccountSwitcher(adb, vision, templatesPath, maxWaitSeconds =>
        {
            // Placeholder — EnsureHomeBase logic lives in CVAutomationFramework
            return true;
        });

        _framework = new CVAutomationFramework(configPath, configService, adb, vision, templatesPath);
    }

    internal BotOrchestrator(CVAutomationFramework framework, ConfigService config, StatsRepository stats,
        PopupHandlerService popups, ZoomService zoom, AccountSwitcher accounts)
    {
        _framework = framework;
        Config = config;
        Stats = stats;
        Popups = popups;
        Zoom = zoom;
        Accounts = accounts;
    }

    public void Start() => _framework.Start();
    public void Stop() => _framework.Stop();
    public void Pause() => _framework.Pause();
    public void Resume() => _framework.Resume();
    public void Dispose() => _framework.Dispose();
}
