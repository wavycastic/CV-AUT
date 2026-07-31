using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
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
    private readonly IVisionEngine _vision;
    private readonly string _templatesPath;

    public AttackDeployBarScanner(IADBHelper adb, IVisionEngine vision, string templatesPath)
    {
        _adb = adb;
        _vision = vision;
        _templatesPath = templatesPath;
    }

    public AttackDeployBarSnapshot Scan(
        bool includeElectroDragon,
        IReadOnlyCollection<string> required,
        bool requiredOnly = false,
        bool reportMissing = true)
    {
        using Mat? screenshot = _adb.TakeScreenshot();
        return screenshot == null || screenshot.Empty()
            ? LogEmptyScreenshot()
            : Scan(screenshot, includeElectroDragon, required, requiredOnly, reportMissing);
    }

    public AttackDeployBarSnapshot Scan(
        Mat screenshot,
        bool includeElectroDragon,
        IReadOnlyCollection<string> required,
        bool requiredOnly = false,
        bool reportMissing = true)
    {
        Console.WriteLine("[ATTACK-CS] phase=scan_bar status=start");
        if (screenshot == null || screenshot.Empty()) return LogEmptyScreenshot();

        var tabs = new Dictionary<string, Point>(StringComparer.OrdinalIgnoreCase);
        var scores = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
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
        if (requiredOnly)
        {
            var requested = new HashSet<string>(required, StringComparer.OrdinalIgnoreCase);
            categories = categories
                .Where(pair => requested.Contains(pair.Key))
                .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.OrdinalIgnoreCase);
        }

        foreach ((string key, string template) in categories)
        {
            double threshold = key is "rage" or "freeze" ? 0.45 : MatchThreshold;
            Point? point = FindWithFallback(screenshot, key, template, threshold, out double score);
            if (point == null || IsDuplicate(point.Value, tabs, key)) continue;
            tabs[key] = point.Value;
            scores[key] = score;
        }

        double siegeScore = 0;
        bool scanSiege = !requiredOnly || required.Contains("siege_machine", StringComparer.OrdinalIgnoreCase);
        Point? siege = scanSiege
            ? _vision.FindElement(screenshot, "troops/siege_with_troops", MatchThreshold, DeployBarRoi, out siegeScore)
            : null;
        if (siege != null && !IsDuplicate(siege.Value, tabs, "siege_machine"))
        {
            tabs["siege_machine"] = siege.Value;
            scores["siege_machine"] = siegeScore;
        }

        foreach ((string key, string template, int count) in requiredOnly
            ? Enumerable.Empty<(string Key, string Template, int Count)>()
            : EnumerateEventTemplates())
        {
            Point? point = _vision.FindElement(screenshot, template, MatchThreshold, DeployBarRoi, out double score);
            if (point == null || IsDuplicate(point.Value, tabs, key)) continue;
            tabs[key] = point.Value;
            scores[key] = score;
            eventTroops.Add(new EventTroopTab(key, point.Value, count));
        }

        string[] missing = reportMissing
            ? required.Where(key => !tabs.ContainsKey(key)).ToArray()
            : Array.Empty<string>();
        string debug = missing.Length > 0 ? DumpScanDebug(screenshot) : "not_captured";
        foreach (string key in missing)
            Console.WriteLine($"[ATTACK-CS WARNING] phase=scan_bar status=missing item={key} reason=required_tab_not_found debug_image=\"{debug}\"");

        string tabSummary = tabs.Count == 0
            ? "none"
            : string.Join(';', tabs
                .OrderBy(pair => pair.Value.X)
                .Select(pair => FormattableString.Invariant(
                    $"{pair.Key}@{pair.Value.X},{pair.Value.Y}#{scores.GetValueOrDefault(pair.Key):F2}")));
        Console.WriteLine($"[ATTACK-CS] phase=scan_bar status=success found={tabs.Count} tabs=\"{tabSummary}\" missing=\"{(missing.Length == 0 ? "none" : string.Join(',', missing))}\" debug_image=\"{debug}\"");

        return new AttackDeployBarSnapshot(tabs, eventTroops);
    }

    private static AttackDeployBarSnapshot LogEmptyScreenshot()
    {
        Console.WriteLine("[ATTACK-CS WARNING] phase=scan_bar status=fail reason=screenshot_empty");
        return AttackDeployBarSnapshot.Empty;
    }

    private static string DumpScanDebug(Mat screenshot)
    {
        try
        {
            string directory = Path.Combine("logs", "attack_scan_debug");
            Directory.CreateDirectory(directory);
            string stamp = DateTime.Now.ToString("yyyyMMdd_HHmmss_fff", CultureInfo.InvariantCulture);
            Rect roi = ImageUtils.ClampRect(DeployBarRoi, screenshot.Width, screenshot.Height);
            using Mat crop = new(screenshot, roi);
            string path = Path.Combine(directory, $"{stamp}_deploy_bar.png");
            return Cv2.ImWrite(path, crop) ? path.Replace('\\', '/') : "capture_failed:write";
        }
        catch (Exception ex)
        {
            return $"capture_failed:{ex.GetType().Name}";
        }
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

    internal static bool IsDuplicate(Point candidate, IReadOnlyDictionary<string, Point> tabs, string candidateName)
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
