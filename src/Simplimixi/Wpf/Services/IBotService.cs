using System;
using System.Text.Json.Nodes;

namespace CvAut.WpfApp.Services
{
    public interface IBotService
    {
        bool IsRunning { get; }
        bool IsPaused { get; }
        int CurrentVillage { get; set; }
        string StatusText { get; }
        
        // Runtime stats
        string UptimeText { get; }
        string MemoryUsageText { get; }
        string SuccessRateText { get; }
        int AttacksCount { get; }
        
        long GoldGained { get; }
        long ElixirGained { get; }
        long DarkElixirGained { get; }
        long AvgGoldPerHour { get; }
        long AvgElixirPerHour { get; }
        long AvgDarkElixirPerHour { get; }
        int Star0Count { get; }
        int Star1Count { get; }
        int Star2Count { get; }
        int Star3Count { get; }

        event Action<string>? LogReceived;
        event Action? StatusChanged;
        event Action? StatsUpdated;

        void StartBot();
        void StopBot();
        void TogglePause();
        
        JsonObject LoadMainConfig();
        void SaveMainConfig(JsonObject root);
        JsonObject LoadProfile(int villageId);
        void SaveProfile(int villageId, JsonObject profile);
    }
}
