using System;
using System.IO;
using CvAut.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace CvAut;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddCvAutBackend(this IServiceCollection services, string configPath = "Config/test_config.json")
    {
        services.AddSingleton<IConfigService>(_ => new ConfigService(configPath));

        services.AddSingleton<ADBHelper>(serviceProvider =>
        {
            var configService = serviceProvider.GetRequiredService<IConfigService>();
            DeviceConnectionConfig device = DeviceConnectionConfigReader.Read(configService.Config);
            return new ADBHelper(device.Host, device.Port, device.Serial);
        });
        services.AddSingleton<IADBHelper>(serviceProvider =>
            serviceProvider.GetRequiredService<ADBHelper>());

        services.AddSingleton<IVisionEngine>(_ =>
        {
            string templatesPath = Path.Combine(AppContext.BaseDirectory, "assets", "Templates");
            return new VisionEngine(templatesPath);
        });

        services.AddSingleton<IPopupHandlerService>(serviceProvider =>
        {
            var adb = serviceProvider.GetRequiredService<ADBHelper>();
            var vision = (VisionEngine)serviceProvider.GetRequiredService<IVisionEngine>();
            string templatesPath = Path.Combine(AppContext.BaseDirectory, "assets", "Templates");
            return new PopupHandlerService(adb, vision, templatesPath);
        });

        services.AddSingleton<IZoomService>(serviceProvider =>
            new ZoomService(serviceProvider.GetRequiredService<IADBHelper>()));

        services.AddSingleton<IStatsRepository>(serviceProvider =>
        {
            var adb = serviceProvider.GetRequiredService<ADBHelper>();
            var vision = (VisionEngine)serviceProvider.GetRequiredService<IVisionEngine>();
            string templatesPath = Path.Combine(AppContext.BaseDirectory, "assets", "Templates");
            return new StatsRepository(adb, vision, templatesPath);
        });

        services.AddSingleton<IAccountSwitcher>(serviceProvider =>
        {
            var adb = serviceProvider.GetRequiredService<ADBHelper>();
            var vision = (VisionEngine)serviceProvider.GetRequiredService<IVisionEngine>();
            string templatesPath = Path.Combine(AppContext.BaseDirectory, "assets", "Templates");
            return new AccountSwitcher(adb, vision, templatesPath, _ => true);
        });

        services.AddSingleton<CVAutomationFramework>(serviceProvider =>
        {
            var configService = (ConfigService)serviceProvider.GetRequiredService<IConfigService>();
            var adb = serviceProvider.GetRequiredService<ADBHelper>();
            var vision = (VisionEngine)serviceProvider.GetRequiredService<IVisionEngine>();
            string templatesPath = Path.Combine(AppContext.BaseDirectory, "assets", "Templates");
            return new CVAutomationFramework(configPath, configService, adb, vision, templatesPath);
        });

        services.AddSingleton<BotOrchestrator>();
        return services;
    }
}
