using System;
using System.Collections.Generic;
using System.Text.Json;

namespace CvAut.Configuration;

internal readonly struct JsonConfigReader
{
    private readonly JsonElement _element;

    public JsonConfigReader(JsonElement element)
    {
        _element = element;
    }

    public JsonConfigReader Section(string name)
        => TryGet(name, out JsonElement value) && value.ValueKind == JsonValueKind.Object
            ? new JsonConfigReader(value)
            : default;

    public string String(string name, string fallback)
        => TryGet(name, out JsonElement value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? fallback
            : fallback;

    public int Int(string name, int fallback, int min = int.MinValue, int max = int.MaxValue)
    {
        int result = TryGet(name, out JsonElement value)
            && value.ValueKind == JsonValueKind.Number
            && value.TryGetInt32(out int parsed)
                ? parsed
                : fallback;
        return Math.Clamp(result, min, max);
    }

    public bool Bool(string name, bool fallback)
        => TryGet(name, out JsonElement value)
            && value.ValueKind is JsonValueKind.True or JsonValueKind.False
                ? value.GetBoolean()
                : fallback;

    public IReadOnlyList<int> IntArray(string name, params int[] fallback)
    {
        if (!TryGet(name, out JsonElement value) || value.ValueKind != JsonValueKind.Array)
            return fallback;
        var result = new List<int>();
        foreach (JsonElement item in value.EnumerateArray())
            if (item.TryGetInt32(out int parsed)) result.Add(parsed);
        return result.Count == 0 ? fallback : result;
    }

    public IEnumerable<JsonConfigReader> ObjectArray(string name)
    {
        if (!TryGet(name, out JsonElement value) || value.ValueKind != JsonValueKind.Array)
            yield break;
        foreach (JsonElement item in value.EnumerateArray())
            if (item.ValueKind == JsonValueKind.Object) yield return new JsonConfigReader(item);
    }

    private bool TryGet(string name, out JsonElement value)
    {
        if (_element.ValueKind == JsonValueKind.Object && _element.TryGetProperty(name, out value))
            return true;
        value = default;
        return false;
    }
}
