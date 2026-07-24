using System;
using System.Collections.Generic;
using System.Text.Json;
using OpenCvSharp;

namespace CvAut;

internal static class ConfigManager
{
    public static int Clamp(int value, int min, int max) => Math.Min(Math.Max(value, min), max);

    public static string GetStringOrDefault(JsonElement element, string propertyName, string fallback)
    {
        if (element.ValueKind == JsonValueKind.Object
            && element.TryGetProperty(propertyName, out JsonElement value)
            && value.ValueKind == JsonValueKind.String)
        {
            return value.GetString() ?? fallback;
        }
        return fallback;
    }

    public static int GetIntOrDefault(JsonElement element, string propertyName, int fallback)
    {
        if (element.ValueKind == JsonValueKind.Object
            && element.TryGetProperty(propertyName, out JsonElement value)
            && value.ValueKind == JsonValueKind.Number
            && value.TryGetInt32(out int result))
        {
            return result;
        }
        return fallback;
    }

    public static bool GetBoolOrDefault(JsonElement element, string propertyName, bool fallback)
    {
        if (element.ValueKind == JsonValueKind.Object
            && element.TryGetProperty(propertyName, out JsonElement value)
            && (value.ValueKind == JsonValueKind.True || value.ValueKind == JsonValueKind.False))
        {
            return value.GetBoolean();
        }
        return fallback;
    }

    public static JsonElement GetObjectOrDefault(JsonElement element, string propertyName)
    {
        if (element.ValueKind == JsonValueKind.Object
            && element.TryGetProperty(propertyName, out JsonElement value)
            && value.ValueKind == JsonValueKind.Object)
        {
            return value;
        }
        return default;
    }

    public static int GetThresholdOrDefault(
        JsonElement profile,
        JsonElement farming,
        JsonElement legacyTarget,
        string profileKey,
        string legacyKey,
        int fallback)
    {
        int rootFallback = GetIntOrDefault(
            farming, profileKey,
            GetIntOrDefault(legacyTarget, legacyKey, fallback));
        return GetIntOrDefault(profile, profileKey, rootFallback);
    }

    public static bool TryReadInt(JsonElement element, string key, out int value)
    {
        value = 0;
        return element.ValueKind == JsonValueKind.Object
            && element.TryGetProperty(key, out JsonElement prop)
            && prop.ValueKind == JsonValueKind.Number
            && prop.TryGetInt32(out value);
    }

    public static bool TryReadPoint(JsonElement point, out Point parsed)
    {
        parsed = default;
        int x, y;

        if (point.ValueKind == JsonValueKind.Object)
        {
            x = GetIntOrDefault(point, "x", -1);
            y = GetIntOrDefault(point, "y", -1);
        }
        else if (point.ValueKind == JsonValueKind.Array && point.GetArrayLength() >= 2)
        {
            JsonElement xNode = point[0];
            JsonElement yNode = point[1];
            if (!xNode.TryGetInt32(out x) || !yNode.TryGetInt32(out y))
                return false;
        }
        else
        {
            return false;
        }

        if (x < 0 || y < 0) return false;
        parsed = new Point(Clamp(x, 0, 1599), Clamp(y, 0, 899));
        return true;
    }

    public static List<Point> ReadPointList(JsonElement element, string propertyName)
    {
        if (element.ValueKind != JsonValueKind.Object
            || !element.TryGetProperty(propertyName, out JsonElement points)
            || points.ValueKind != JsonValueKind.Array)
        {
            return new List<Point>();
        }

        List<Point> result = new();
        foreach (JsonElement point in points.EnumerateArray())
        {
            if (TryReadPoint(point, out Point parsed))
                result.Add(parsed);
        }
        return result;
    }

    public static AttackDelayConfig ReadAttackDelayConfig(JsonElement cfg)
    {
        JsonElement advanced = GetObjectOrDefault(cfg, "advanced_config");
        bool useDefault = GetBoolOrDefault(advanced, "use_default_config", true);
        JsonElement attackDelays = useDefault ? default : GetObjectOrDefault(advanced, "attack_delays");

        return new AttackDelayConfig
        {
            TroopDeployDelayMs = Clamp(GetIntOrDefault(attackDelays, "troop_deploy_delay_ms", 60), 20, 500),
            RageSpellDelayMs = Clamp(GetIntOrDefault(attackDelays, "rage_spell_delay_ms", 650), 100, 5000),
            FreezeSpellDelayMs = Clamp(GetIntOrDefault(attackDelays, "freeze_spell_delay_ms", 850), 100, 5000),
            GrandWardenAbilityDelayMs = Clamp(GetIntOrDefault(attackDelays, "grand_warden_ability_delay_ms", 2500), 500, 15000)
        };
    }

    public static AttackCoordinateConfig ReadAttackCoordinateConfig(JsonElement cfg)
    {
        JsonElement advanced = GetObjectOrDefault(cfg, "advanced_config");
        bool useDefault = GetBoolOrDefault(advanced, "use_default_config", true);
        JsonElement spellCoordinates = useDefault ? default : GetObjectOrDefault(advanced, "spell_coordinates");
        AttackCoordinateConfig coordinateConfig = new();

        foreach (string direction in new[] { "top_left", "top_right", "bottom_left", "bottom_right" })
        {
            JsonElement directionNode = GetObjectOrDefault(spellCoordinates, direction);
            SpellDeploymentGroups groups = new()
            {
                RageInitial = ReadPointList(directionNode, "rage_initial"),
                Freeze = ReadPointList(directionNode, "freeze"),
                RageRemaining = ReadPointList(directionNode, "rage_remaining")
            };

            if (groups.RageInitial.Count > 0 || groups.Freeze.Count > 0 || groups.RageRemaining.Count > 0)
                coordinateConfig.SpellCoordinates[direction] = groups;
        }

        return coordinateConfig;
    }
}
