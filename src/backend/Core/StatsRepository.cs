using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using OpenCvSharp;

namespace CvAut;

internal sealed class StatsRepository : IStatsRepository
{
    private readonly IADBHelper _adb;
    private readonly IVisionEngine _vision;
    private readonly string _templatesPath;
    private static readonly string WritableLogsDirectory = ResolveWritableLogsDirectory();

    public StatsRepository(IADBHelper adb, IVisionEngine vision, string templatesPath)
    {
        _adb = adb;
        _vision = vision;
        _templatesPath = templatesPath;
    }

    public void UpdateStats(int villageIdx, int starsGot, (int Gold, int Elixir, int DarkElixir) gained)
    {
        string path = StatsFilePath(villageIdx);
        JsonObject stats = LoadStatsFromDisk(path);
        stats["gold"] = GetJsonInt(stats, "gold") + gained.Gold;
        stats["elixir"] = GetJsonInt(stats, "elixir") + gained.Elixir;
        stats["de"] = GetJsonInt(stats, "de") + gained.DarkElixir;
        stats["attacks"] = GetJsonInt(stats, "attacks") + 1;
        stats["last_update_ts"] = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

        JsonObject stars = stats["stars"] as JsonObject ?? DefaultStarsObject();
        string key = Math.Clamp(starsGot, 0, 3).ToString();
        stars[key] = GetJsonInt(stars, key) + 1;
        stats["stars"] = stars;

        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, stats.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
    }

    public void UpdateWallStats(int villageIdx, int upgradedCount)
    {
        if (upgradedCount <= 0) return;
        string path = StatsFilePath(villageIdx);
        JsonObject stats = LoadStatsFromDisk(path);
        stats["walls_upgraded"] = GetJsonInt(stats, "walls_upgraded") + upgradedCount;
        stats["last_update_ts"] = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, stats.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
    }

    public void UpdateBuilderBaseAttackStats(int villageIdx, BuilderBaseBattleResult result)
    {
        string path = StatsFilePath(villageIdx);
        JsonObject stats = LoadStatsFromDisk(path);
        JsonObject bb = stats["builder_base"] as JsonObject ?? new JsonObject();
        bb["attacks"] = GetJsonInt(bb, "attacks") + 1;
        bb["wins"] = GetJsonInt(bb, "wins") + (result.Stars > 0 ? 1 : 0);
        bb["losses"] = GetJsonInt(bb, "losses") + (result.Stars <= 0 ? 1 : 0);
        bb["stars"] = GetJsonInt(bb, "stars") + Math.Clamp(result.Stars, 0, 3);
        bb["damage"] = GetJsonInt(bb, "damage") + Math.Clamp(result.Damage, 0, 200);
        bb["stage2_entries"] = GetJsonInt(bb, "stage2_entries") + (result.Stage2Entered ? 1 : 0);
        bb["returned_home_failures"] = GetJsonInt(bb, "returned_home_failures") + (result.ReturnedHome ? 0 : 1);
        bb["last_update_ts"] = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        stats["builder_base"] = bb;
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, stats.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
    }

    public int GetStarsFromScreen()
    {
        Thread.Sleep(500);
        using Mat? screenshot = _adb.TakeScreenshot();
        Thread.Sleep(500);
        if (screenshot == null || screenshot.Empty()) return 0;

        if (_vision.FindElement(screenshot, @"ui\one_star.png", 0.40, Rect.FromLTRB(518, 90, 747, 316), out _) == null) return 0;
        if (_vision.FindElement(screenshot, @"ui\two_star.png", 0.40, Rect.FromLTRB(670, 106, 926, 285), out _) == null) return 1;
        return _vision.FindElement(screenshot, @"ui\three_star.png", 0.40, Rect.FromLTRB(840, 96, 1064, 317), out _) != null ? 3 : 2;
    }

    public (int Gold, int Elixir, int DarkElixir) GainResources(int stars)
    {
        Thread.Sleep(500);
        using Mat? screenshot = _adb.TakeScreenshot();
        Thread.Sleep(500);
        if (screenshot == null || screenshot.Empty()) return (0, 0, 0);

        SaveDebugImage(screenshot, "debug_stats_result.png");

        int goldLeft = OcrResourceSum(screenshot, Rect.FromLTRB(586, 372, 825, 420), "gold_loot", 1000);
        int elixirLeft = OcrResourceSum(screenshot, Rect.FromLTRB(590, 431, 827, 482), "elixir_loot", 1000);
        int deLeft = OcrResourceSum(screenshot, Rect.FromLTRB(643, 489, 826, 539), "dark_loot", 100);

        int goldRight = 0, elixirRight = 0, deRight = 0;
        if (stars > 0)
        {
            goldRight = OcrResourceSum(screenshot, Rect.FromLTRB(1012, 444, 1176, 490), "gold_bonus", 1000);
            elixirRight = OcrResourceSum(screenshot, Rect.FromLTRB(1016, 493, 1176, 537), "elixir_bonus", 1000);
            deRight = OcrResourceSum(screenshot, Rect.FromLTRB(1036, 541, 1176, 584), "dark_bonus", 100);
        }
        return (goldLeft + goldRight, elixirLeft + elixirRight, deLeft + deRight);
    }

    public int OcrResourceSum(Mat screenshot, Rect roi, string label, int minValidValue)
    {
        SaveStatsCrop(screenshot, roi, label);
        if (_vision.TryExtractNumericalMetrics(screenshot, roi, out int value, out double confidence, useRgbThresh: true))
            if (IsPlausibleResourceValue(value, confidence, minValidValue, label, "rgb")) return value;
        if (_vision.TryExtractNumericalMetrics(screenshot, roi, out value, out confidence))
            if (IsPlausibleResourceValue(value, confidence, minValidValue, label, "gray")) return value;
        return 0;
    }

    public void SaveDebugImage(Mat image, string fileName)
    {
        try
        {
            Directory.CreateDirectory(WritableLogsDirectory);
            Cv2.ImWrite(Path.Combine(WritableLogsDirectory, fileName), image);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[STATS-CS WARNING] phase=log status=fail action=save_debug_image file=\"{fileName}\" reason=\"{ex.Message}\"");
        }
    }

    private void SaveStatsCrop(Mat screenshot, Rect roi, string label)
    {
        try
        {
            Rect safeRoi = ImageUtils.ClampRect(roi, screenshot.Width, screenshot.Height);
            if (safeRoi.Width <= 0 || safeRoi.Height <= 0) return;
            using Mat crop = new Mat(screenshot, safeRoi);
            SaveDebugImage(crop, $"debug_stats_{label}.png");
        }
        catch { }
    }

    private static bool IsPlausibleResourceValue(int value, double confidence, int minValidValue, string label, string mode)
    {
        bool plausible = value == 0 || value >= minValidValue;
        if (confidence < 0.62) return false;
        if (!plausible) return false;
        return true;
    }

    private static string StatsFilePath(int villageIdx)
    {
        string appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        return Path.Combine(appData, "SimpliMixi", "profiles", $"Stats_{villageIdx}.json");
    }

    private static JsonObject LoadStatsFromDisk(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                JsonNode? node = JsonNode.Parse(File.ReadAllText(path));
                if (node is JsonObject obj) { obj["stars"] ??= DefaultStarsObject(); return obj; }
            }
        }
        catch { }
        return new JsonObject { ["gold"] = 0, ["elixir"] = 0, ["de"] = 0, ["attacks"] = 0, ["stars"] = DefaultStarsObject(), ["last_update_ts"] = 0 };
    }

    private static JsonObject DefaultStarsObject() => new() { ["0"] = 0, ["1"] = 0, ["2"] = 0, ["3"] = 0 };

    private static int GetJsonInt(JsonObject obj, string key)
    {
        if (!obj.TryGetPropertyValue(key, out JsonNode? node) || node == null) return 0;
        return node.GetValueKind() == JsonValueKind.Number && node.GetValue<int>() is int value ? value : 0;
    }

    private static string ResolveWritableLogsDirectory()
    {
        string appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        return Path.Combine(appData, "SimpliMixi", "logs");
    }
}
