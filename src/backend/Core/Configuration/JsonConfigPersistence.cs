using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace CvAut.Configuration;

internal sealed class JsonConfigPersistence
{
    private readonly string _path;

    public JsonConfigPersistence(string path)
    {
        _path = path;
        Root = LoadRoot();
    }

    public JsonElement Root { get; private set; }

    public void Reload() => Root = LoadRoot();

    public static JsonElement LoadVillageProfile(int villageIndex)
    {
        string fileName = $"Village_{villageIndex}.json";
        string local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        string[] candidates =
        {
            Path.Combine(local, "SimpliMixi", "profiles", fileName),
            Path.Combine(Directory.GetCurrentDirectory(), "profiles", fileName),
            Path.Combine(AppContext.BaseDirectory, "profiles", fileName)
        };
        foreach (string path in candidates)
        {
            if (!File.Exists(path)) continue;
            try
            {
                using JsonDocument document = JsonDocument.Parse(File.ReadAllText(path));
                return document.RootElement.Clone();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[CONFIG-CS WARNING] phase=load_profile status=fail path=\"{path}\" reason=\"{ex.Message}\"");
                return default;
            }
        }
        return default;
    }

    public static int ReadClanGamesPoints(int villageIndex)
    {
        string local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        string path = Path.Combine(local, "SimpliMixi", "profiles", $"Stats_{villageIndex}.json");
        try
        {
            if (File.Exists(path)
                && JsonNode.Parse(File.ReadAllText(path)) is JsonObject stats
                && stats["clan_games_points"] is JsonNode points)
            {
                return points.GetValue<int>();
            }
        }
        catch
        {
            // Corrupt or legacy stats fall back to zero.
        }
        return 0;
    }

    private JsonElement LoadRoot()
    {
        try
        {
            if (File.Exists(_path))
            {
                using JsonDocument document = JsonDocument.Parse(File.ReadAllText(_path));
                return document.RootElement.Clone();
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[CONFIG-CS WARNING] phase=load status=fail reason=\"{ex.Message}\" details=\"using_defaults\"");
        }

        using JsonDocument fallback = JsonDocument.Parse(DefaultJson);
        return fallback.RootElement.Clone();
    }

    private const string DefaultJson = """
    {
      "device_connection": { "host": "127.0.0.1", "port": 5556 },
      "farming_thresholds": {
        "gold_threshold": 650000,
        "elixir_threshold": 650000,
        "dark_elixir_threshold": 1000,
        "total_resource_threshold": 1300000,
        "target_logic": "total"
      },
      "attack": "Dragon_Attack",
      "attack_mode": "attack",
      "train_mode": "smart",
      "quick_slot": 1,
      "upgrade_wall": false,
      "wall_level": 14,
      "wall_gold_threshold": 5000000,
      "wall_elixir_threshold": 5000000,
      "wall_gold_reserve": 100000,
      "wall_elixir_reserve": 0,
      "enable_stats": true,
      "night_village": {
        "farm_mode": "auto",
        "min_cups": 0,
        "max_cups": 5000,
        "enable_attack": true,
        "army_management": true,
        "fill_army": true,
        "army_formation": "auto",
        "wait_for_heroes": true,
        "hero_wait_seconds": 90
      },
      "run_session": {
        "play_mode": "main_village",
        "stop_after_battles_enabled": false,
        "stop_after_battles": 0,
        "stop_after_minutes_enabled": false,
        "stop_after_minutes": 0
      },
      "multi_account": {
        "enable_multi_account": false,
        "multi_interval_mins": 60,
        "selected_villages": [1],
        "accounts": []
      }
    }
    """;
}
