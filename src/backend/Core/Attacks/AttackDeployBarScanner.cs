using System;
using System.Collections.Generic;
using System.Linq;
using OpenCvSharp;

namespace CvAut;

internal sealed record EventTroopTab(string Key, Point Tab, int DropCount);

internal sealed record AttackDeployBarSnapshot(
    IReadOnlyDictionary<string, Point> Tabs,
    IReadOnlyList<EventTroopTab> EventTroops)
{
    public static AttackDeployBarSnapshot Empty { get; } = new(
        new Dictionary<string, Point>(StringComparer.OrdinalIgnoreCase),
        Array.Empty<EventTroopTab>());
}

internal sealed class AttackDeployBarScanner
{
    private const double MatchThreshold = 0.52;
    private const int DuplicateDistance = 45;
    private static readonly Rect DeployBarRoi = Rect.FromLTRB(70, 720, 1180, 890);

    private readonly IADBHelper _adb;
    private readonly VisionEngine _vision;
    private readonly string _templatesPath;

    public AttackDeployBarScanner(IADBHelper adb, VisionEngine vision, string templatesPath)
    {
        _adb = adb;
        _vision = vision;
        _templatesPath = templatesPath;
    }

    public AttackDeployBarSnapshot Scan(bool includeElectroDragon, IReadOnlyCollection<string> required)
    {
        Console.WriteLine("[ATTACK-CS] phase=scan_bar status=start");
        using Mat? screenshot = _adb.TakeScreenshot();
        if (screenshot == null || screenshot.Empty()) return AttackDeployBarSnapshot.Empty;

        var tabs = new Dictionary<string, Point>(StringComparer.OrdinalIgnoreCase);
        var eventTroops = new List<EventTroopTab>();
        var categories = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["dragon"] = "troops/dragon",
            ["balloon"] = "troops/balloon",
            ["azure_dragon"] = "troops/azure_dragon",
            ["ice_minion"] = "troops/ice_minion",
            ["ice_golem"] = "troops/ice_golem",
            ["rage"] = "spells/rage",
            ["freeze"] = "spells/freeze",
            ["queen"] = "heroes/queen",
            ["bk"] = "heroes/bk",
            ["warden"] = "heroes/warden",
            ["prince"] = "heroes/prince",
            ["rc"] = "heroes/rc"
        };
        if (includeElectroDragon) categories["e_drag"] = "troops/E_Drag";

        foreach ((string key, string template) in categories)
        {
            double threshold = key is "rage" or "freeze" ? 0.45 : MatchThreshold;
            Point? point = FindWithFallback(screenshot, key, template, threshold, out _);
            if (point == null || IsDuplicate(point.Value, tabs, key)) continue;
            tabs[key] = point.Value;
        }

        Rect wide = Rect.FromLTRB(0, 650, screenshot.Width, screenshot.Height);
        Point? siege = _vision.FindElement(screenshot, "troops/siege_with_troops", MatchThreshold, DeployBarRoi, out _)
            ?? _vision.FindElement(screenshot, "troops/icon_siege", 0.42, wide, out _)
            ?? _vision.FindElement(screenshot, "troops/empty_siege", 0.42, wide, out _);
        if (siege != null) tabs["siege_machine"] = siege.Value;

        foreach ((string key, string template, int count) in EnumerateEventTemplates())
        {
            Point? point = _vision.FindElement(screenshot, template, MatchThreshold, DeployBarRoi, out _);
            if (point == null || IsDuplicate(point.Value, tabs, key)) continue;
            tabs[key] = point.Value;
            eventTroops.Add(new EventTroopTab(key, point.Value, count));
        }

        foreach (string key in required.Where(key => !tabs.ContainsKey(key)))
            Console.WriteLine($"[ATTACK-CS WARNING] phase=scan_bar status=missing item={key} reason=required_tab_not_found");

        return new AttackDeployBarSnapshot(tabs, eventTroops);
    }

    private Point? FindWithFallback(Mat screenshot, string key, string template, double threshold, out double score)
    {
        Point? point = _vision.FindElement(screenshot, template, threshold, DeployBarRoi, out score);
        if (point != null) return point;

        Rect wide = Rect.FromLTRB(0, 650, screenshot.Width, screenshot.Height);
        string[] alternatives = key switch
        {
            "dragon" => new[] { "troops/icon_dragon", "Smart_Auto_train/Army Troops/dragon", "Smart_Auto_train/to_train/dragon" },
            "e_drag" => new[] { "troops/e_drag", "troops/electro_dragon", "Smart_Auto_train/Army Troops/electro_dragon" },
            "freeze" => new[] { "Smart_Auto_train/Spells/freeze", "Smart_Auto_train/to_train/freeze" },
            _ => Array.Empty<string>()
        };
        foreach (string alternative in alternatives)
        {
            point = _vision.FindElement(screenshot, alternative, 0.40, wide, out score);
            if (point != null) return point;
        }
        return null;
    }

    private static bool IsDuplicate(Point candidate, IReadOnlyDictionary<string, Point> tabs, string candidateName)
    {
        foreach ((string name, Point point) in tabs)
        {
            int dx = point.X - candidate.X;
            int dy = point.Y - candidate.Y;
            if ((dx * dx) + (dy * dy) > DuplicateDistance * DuplicateDistance) continue;
            bool bothPrimary = (candidateName is "dragon" or "e_drag") && (name is "dragon" or "e_drag");
            return !bothPrimary;
        }
        return false;
    }

    private IEnumerable<(string Key, string Template, int Count)> EnumerateEventTemplates()
    {
        foreach (string name in TemplateAssetLoader.EnumerateNames(_templatesPath, "event"))
        {
            int count = 10;
            string baseName = name;
            int underscore = name.LastIndexOf('_');
            if (underscore > 0 && int.TryParse(name[(underscore + 1)..], out int parsed))
            {
                count = Math.Clamp(parsed, 1, 200);
                baseName = name[..underscore];
            }
            string key = "event_" + baseName.ToLowerInvariant().Replace(' ', '_').Replace('-', '_');
            yield return (key, "event/" + name, count);
        }
    }
}
