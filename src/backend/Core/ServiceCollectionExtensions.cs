using System;
using System.IO;
using Microsoft.Extensions.DependencyInjection;

namespace CvAut;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddCvAutBackend(this IServiceCollection services, string configPath = "Config/test_config.json")
    {
        services.AddSingleton<IConfigService>(sp => new ConfigService(configPath));

        services.AddSingleton<IADBHelper>(sp =>
        {
            var configService = sp.GetRequiredService<IConfigService>();
            var devConfig = ConfigManager.GetObjectOrDefault(configService.Config, "device_connection");
            string host = ConfigManager.GetStringOrDefault(devConfig, "host", "127.0.0.1");
            int port = ConfigManager.GetIntOrDefault(devConfig, "port", 5556);
            return new ADBHelper(host, port);
        });

        services.AddSingleton<IVisionEngine>(sp =>
        {
            string templatesPath = Path.Combine(AppContext.BaseDirectory, "assets", "Templates");
            return new VisionEngine(templatesPath);
        });

        services.AddSingleton<IPopupHandlerService>(sp =>
        {
            var adb = (ADBHelper)sp.GetRequiredService<IADBHelper>();
            var vision = (VisionEngine)sp.GetRequiredService<IVisionEngine>();
            string templatesPath = Path.Combine(AppContext.BaseDirectory, "assets", "Templates");
            return new PopupHandlerService(adb, vision, templatesPath);
        });

        services.AddSingleton<IZoomService>(sp =>
        {
            var adb = (ADBHelper)sp.GetRequiredService<IADBHelper>();
            return new ZoomService(adb);
        });

        services.AddSingleton<IStatsRepository>(sp =>
        {
            var adb = (ADBHelper)sp.GetRequiredService<IADBHelper>();
            var vision = (VisionEngine)sp.GetRequiredService<IVisionEngine>();
            string templatesPath = Path.Combine(AppContext.BaseDirectory, "assets", "Templates");
            return new StatsRepository(adb, vision, templatesPath);
        });

        services.AddSingleton<IAccountSwitcher>(sp =>
        {
            var adb = (ADBHelper)sp.GetRequiredService<IADBHelper>();
            var vision = (VisionEngine)sp.GetRequiredService<IVisionEngine>();
            string templatesPath = Path.Combine(AppContext.BaseDirectory, "assets", "Templates");
            return new AccountSwitcher(adb, vision, templatesPath, maxWaitSeconds => true);
        });

        services.AddSingleton<CVAutomationFramework>(sp =>
        {
            var configService = (ConfigService)sp.GetRequiredService<IConfigService>();
            var adb = (ADBHelper)sp.GetRequiredService<IADBHelper>();
            var vision = (VisionEngine)sp.GetRequiredService<IVisionEngine>();
            string templatesPath = Path.Combine(AppContext.BaseDirectory, "assets", "Templates");
            return new CVAutomationFramework(configPath, configService, adb, vision, templatesPath);
        });

        services.AddSingleton<BotOrchestrator>();

        return services;
    }
}
