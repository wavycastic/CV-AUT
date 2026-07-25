using System;
using System.IO;
using CvAut.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace CvAut;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddCvAutBackend(this IServiceCollection services, string configPath = "Config/test_config.json")
    {
        string templatesPath = Path.Combine(AppContext.BaseDirectory, "assets", "Templates");

        // --- Composition root: the only place that knows about concrete implementations. ---
        services.AddSingleton<IConfigService>(_ => new ConfigService(configPath));

        services.AddSingleton<IADBHelper>(serviceProvider =>
        {
            DeviceConnectionConfig device = serviceProvider
                .GetRequiredService<IConfigService>()
                .DeviceConnection;
            return new ADBHelper(device.Host, device.Port, device.Serial);
        });

        services.AddSingleton<IVisionEngine>(_ => new VisionEngine(templatesPath));

        // --- Consumers: abstractions only. ---
        services.AddSingleton<IPopupHandlerService>(serviceProvider => new PopupHandlerService(
            serviceProvider.GetRequiredService<IADBHelper>(),
            serviceProvider.GetRequiredService<IVisionEngine>(),
            templatesPath));

        services.AddSingleton<IZoomService>(serviceProvider =>
            new ZoomService(serviceProvider.GetRequiredService<IADBHelper>()));

        services.AddSingleton<IStatsRepository>(serviceProvider => new StatsRepository(
            serviceProvider.GetRequiredService<IADBHelper>(),
            serviceProvider.GetRequiredService<IVisionEngine>(),
            templatesPath));

        services.AddSingleton<IAccountSwitcher>(serviceProvider => new AccountSwitcher(
            serviceProvider.GetRequiredService<IADBHelper>(),
            serviceProvider.GetRequiredService<IVisionEngine>(),
            templatesPath,
            _ => true));

        services.AddSingleton<CVAutomationFramework>(serviceProvider => new CVAutomationFramework(
            configPath,
            serviceProvider.GetRequiredService<IConfigService>(),
            serviceProvider.GetRequiredService<IADBHelper>(),
            serviceProvider.GetRequiredService<IVisionEngine>(),
            templatesPath));

        services.AddSingleton<BotOrchestrator>();
        return services;
    }
}
