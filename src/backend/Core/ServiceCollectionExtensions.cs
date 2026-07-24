using System;
using System.IO;
using Microsoft.Extensions.DependencyInjection;

namespace CvAut;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddCvAutBackend(this IServiceCollection services, string configPath = "Config/test_config.json")
    {
        services.AddSingleton<IConfigService>(_ => new ConfigService(configPath));

        services.AddSingleton<IADBHelper>(serviceProvider =>
        {
            var configService = serviceProvider.GetRequiredService<IConfigService>();
            var deviceConfig = ConfigManager.GetObjectOrDefault(configService.Config, "device_connection");
            string host = ConfigManager.GetStringOrDefault(deviceConfig, "host", "127.0.0.1");
            int port = ConfigManager.GetIntOrDefault(deviceConfig, "port", 5556);
            return new ADBHelper(host, port);
        });

        services.AddSingleton<IVisionEngine>(_ =>
        {
            string templatesPath = Path.Combine(AppContext.BaseDirectory, "assets", "Templates");
            return new VisionEngine(templatesPath);
        });

        services.AddSingleton<IPopupHandlerService>(serviceProvider =>
        {
            var adb = (ADBHelper)serviceProvider.GetRequiredService<IADBHelper>();
            var vision = (VisionEngine)serviceProvider.GetRequiredService<IVisionEngine>();
            string templatesPath = Path.Combine(AppContext.BaseDirectory, "assets", "Templates");
            return new PopupHandlerService(adb, vision, templatesPath);
        });

        services.AddSingleton<IZoomService>(serviceProvider =>
            new ZoomService(serviceProvider.GetRequiredService<IADBHelper>()));

        services.AddSingleton<IStatsRepository>(serviceProvider =>
        {
            var adb = (ADBHelper)serviceProvider.GetRequiredService<IADBHelper>();
            var vision = (VisionEngine)serviceProvider.GetRequiredService<IVisionEngine>();
            string templatesPath = Path.Combine(AppContext.BaseDirectory, "assets", "Templates");
            return new StatsRepository(adb, vision, templatesPath);
        });

        services.AddSingleton<IAccountSwitcher>(serviceProvider =>
        {
            var adb = (ADBHelper)serviceProvider.GetRequiredService<IADBHelper>();
            var vision = (VisionEngine)serviceProvider.GetRequiredService<IVisionEngine>();
            string templatesPath = Path.Combine(AppContext.BaseDirectory, "assets", "Templates");
            return new AccountSwitcher(adb, vision, templatesPath, _ => true);
        });

        services.AddSingleton<CVAutomationFramework>(serviceProvider =>
        {
            var configService = (ConfigService)serviceProvider.GetRequiredService<IConfigService>();
            var adb = (ADBHelper)serviceProvider.GetRequiredService<IADBHelper>();
            var vision = (VisionEngine)serviceProvider.GetRequiredService<IVisionEngine>();
            string templatesPath = Path.Combine(AppContext.BaseDirectory, "assets", "Templates");
            return new CVAutomationFramework(configPath, configService, adb, vision, templatesPath);
        });

        services.AddSingleton<BotOrchestrator>();
        return services;
    }
}
