using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using OpenCvSharp;

namespace CvAut;

internal sealed class AccountSwitcher : IAccountSwitcher
{
    private readonly ADBHelper _adb;
    private readonly VisionEngine _vision;
    private readonly string _templatesPath;
    private readonly AccountManager _manager;
    private readonly Func<int, bool> _ensureHomeBase;

    private string _activeAccountName = "unknown";

    public string ActiveAccountName => _activeAccountName;

    public AccountSwitcher(ADBHelper adb, VisionEngine vision, string templatesPath, Func<int, bool> ensureHomeBase)
    {
        _adb = adb;
        _vision = vision;
        _templatesPath = templatesPath;
        _manager = new AccountManager();
        _ensureHomeBase = ensureHomeBase;
    }

    public AccountConfig[] GetConfiguredAccounts(JsonElement multiConfig)
        => _manager.GetConfiguredAccounts(multiConfig);

    public int[] GetSelectedVillages(JsonElement multiConfig)
        => AccountManager.GetSelectedVillages(multiConfig);

    public bool SwitchToAccount(AccountConfig account, CancellationToken token)
    {
        string previousAccount = _activeAccountName;
        Console.WriteLine($"[ACCOUNT-CS] phase=switch status=start current=\"{previousAccount}\" target=\"{account.Name}\" village={account.ProfileVillage} target_village={account.TargetVillage}");

        if (!_ensureHomeBase(20))
        {
            Console.WriteLine("[ACCOUNT-CS] phase=switch status=fail reason=home_not_detected");
            return false;
        }

        if (!TapFirstVisibleTemplate(new[] { @"ui\settings_logo", "settings_logo", "game_setting" }, 0.68, AutomationRoiConstants.GameSettingHomeRoi, out string settingsTemplate))
        {
            Console.WriteLine("[ACCOUNT-CS] phase=switch status=fail step=open_settings reason=settings_button_not_found");
            return false;
        }
        Console.WriteLine($"[ACCOUNT-CS] phase=switch status=pending step=open_settings template=\"{settingsTemplate}\"");
        if (InterruptibleSleep(1200, token)) return false;

        if (!TapFirstVisibleTemplate(new[] { @"ui\supercell_ID", "supercell_ID" }, 0.68, null, out string supercellTemplate))
        {
            Console.WriteLine("[ACCOUNT-CS] phase=switch status=fail step=open_supercell_id reason=template_not_found");
            _adb.ExecuteShell("input keyevent KEYCODE_BACK");
            return false;
        }
        Console.WriteLine($"[ACCOUNT-CS] phase=switch status=pending step=open_supercell_id template=\"{supercellTemplate}\"");
        if (InterruptibleSleep(1800, token)) return false;

        if (!TapFirstVisibleTemplate(new[] { @"ui\switch_button", "switch_button", @"ui\icon_switch", "icon_switch" }, 0.68, null, out string switchTemplate))
        {
            Console.WriteLine("[ACCOUNT-CS] phase=switch status=fail step=open_switch_account reason=template_not_found");
            _adb.ExecuteShell("input keyevent KEYCODE_BACK");
            return false;
        }
        Console.WriteLine($"[ACCOUNT-CS] phase=switch status=pending step=open_switch_account template=\"{switchTemplate}\"");
        if (InterruptibleSleep(1800, token)) return false;

        if (!TapAccountTemplate(account, out double accountScore))
        {
            Console.WriteLine($"[ACCOUNT-CS] phase=switch status=pending step=show_all_accounts account=\"{account.Name}\"");
            TryShowAllAccounts(token);
        }

        if (!TapAccountTemplate(account, out accountScore))
        {
            Console.WriteLine($"[ACCOUNT-CS] phase=switch status=fail step=select_account reason=account_template_not_found account=\"{account.Name}\" template=\"{account.TemplatePath}\"");
            _adb.ExecuteShell("input keyevent KEYCODE_BACK");
            return false;
        }
        Console.WriteLine($"[ACCOUNT-CS] phase=switch status=pending step=select_account account=\"{account.Name}\" score={accountScore:F2}");
        if (InterruptibleSleep(2500, token)) return false;

        TapFirstVisibleTemplate(new[] { @"ui\play_button", "play_button", @"ui\open_button", "open_button", @"ui\open_button_2", "open_button_2" }, 0.66, null, out _, tap: true);
        InterruptibleSleep(5000, token);

        bool loaded = _ensureHomeBase(45);
        if (loaded)
        {
            _activeAccountName = account.Name;
            if (!string.IsNullOrEmpty(account.ConfigPreset))
                ApplyPresetToProfile(account.ProfileVillage, account.ConfigPreset);
        }
        Console.WriteLine($"[ACCOUNT-CS] phase=switch status={(loaded ? "success" : "fail")} current=\"{previousAccount}\" target=\"{account.Name}\" village={account.ProfileVillage}");
        return loaded;
    }

    public bool ShouldSwitchAccount(
        DateTime slotStart,
        int slotBattleStart,
        int slotClanPointStart,
        int villageIdx,
        bool switchByMinutes,
        int intervalSecs,
        bool switchByBattles,
        int battleLimit,
        bool switchByClanPoints,
        int clanPointLimit,
        int sessionBattlesCompleted,
        out string reason)
    {
        if (switchByBattles && battleLimit > 0 && sessionBattlesCompleted - slotBattleStart >= battleLimit)
        { reason = "battle_limit"; return true; }

        if (switchByClanPoints && clanPointLimit > 0 && ConfigService.ReadClanGamesPoints(villageIdx) - slotClanPointStart >= clanPointLimit)
        { reason = "clan_games_points"; return true; }

        if (switchByMinutes && intervalSecs > 0 && (DateTime.Now - slotStart).TotalSeconds >= intervalSecs)
        { reason = "minute_limit"; return true; }

        reason = "none";
        return false;
    }

    private void TryShowAllAccounts(CancellationToken token)
    {
        if (TapFirstVisibleTemplate(new[] { @"ui\account_counter_2", "account_counter_2", @"ui\account_counter", "account_counter" }, 0.66, null, out string counterTemplate))
        {
            Console.WriteLine($"[ACCOUNT-CS] phase=switch status=pending step=show_all_accounts template=\"{counterTemplate}\"");
            InterruptibleSleep(1200, token);
            return;
        }
        _adb.Swipe(820, 720, 820, 260, 450);
        InterruptibleSleep(700, token);
    }

    private bool TapAccountTemplate(AccountConfig account, out double score)
    {
        score = 0;
        if (string.IsNullOrWhiteSpace(account.TemplatePath)) return false;

        using Mat? screenshot = _adb.TakeScreenshot();
        if (screenshot == null || screenshot.Empty()) return false;

        string? templatePath = ResolveAccountTemplatePath(account.TemplatePath);
        if (templatePath == null) return false;

        using Mat template = Cv2.ImRead(templatePath, ImreadModes.Grayscale);
        if (template.Empty()) return false;

        using Mat gray = new Mat();
        Cv2.CvtColor(screenshot, gray, ColorConversionCodes.BGR2GRAY);
        if (gray.Width < template.Width || gray.Height < template.Height) return false;

        using Mat result = new Mat();
        Cv2.MatchTemplate(gray, template, result, TemplateMatchModes.CCoeffNormed);
        Cv2.MinMaxLoc(result, out _, out score, out _, out Point maxLoc);
        if (score < 0.70) return false;

        int centerX = maxLoc.X + template.Width / 2;
        int centerY = maxLoc.Y + template.Height / 2;
        _adb.Tap(centerX, centerY);
        return true;
    }

    private string? ResolveAccountTemplatePath(string templatePath)
    {
        string trimmed = templatePath.Trim();
        string[] candidates = Path.IsPathRooted(trimmed)
            ? [trimmed]
            : [
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SimpliMixi", "account_templates", trimmed),
                Path.Combine(Directory.GetCurrentDirectory(), trimmed),
                Path.Combine(AppContext.BaseDirectory, trimmed),
                Path.Combine(_templatesPath, "accounts", trimmed),
                Path.Combine(_templatesPath, trimmed)
            ];
        return candidates.FirstOrDefault(File.Exists);
    }

    private static void ApplyPresetToProfile(int villageId, string presetIdOrName)
    {
        if (string.IsNullOrEmpty(presetIdOrName)) return;
        string userData = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SimpliMixi");
        string presetsPath = Path.Combine(userData, "Config", "presets.json");
        if (!File.Exists(presetsPath)) return;

        try
        {
            string presetsJson = File.ReadAllText(presetsPath);
            var presetsNode = JsonNode.Parse(presetsJson) as JsonArray;
            if (presetsNode == null) return;

            JsonObject? targetPresetConfig = null;
            foreach (var node in presetsNode)
            {
                if (node is JsonObject presetObj)
                {
                    string? id = presetObj["id"]?.ToString();
                    string? name = presetObj["name"]?.ToString();
                    if (string.Equals(id, presetIdOrName, StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(name, presetIdOrName, StringComparison.OrdinalIgnoreCase))
                    {
                        targetPresetConfig = presetObj["config"] as JsonObject;
                        break;
                    }
                }
            }
            if (targetPresetConfig == null) return;

            string profilePath = Path.Combine(userData, "profiles", $"Village_{villageId}.json");
            JsonObject profile;
            if (File.Exists(profilePath))
            {
                string profileJson = File.ReadAllText(profilePath);
                profile = JsonNode.Parse(profileJson) as JsonObject ?? new JsonObject();
            }
            else
            {
                profile = new JsonObject();
            }

            foreach (var kvp in targetPresetConfig)
                profile[kvp.Key] = kvp.Value?.DeepClone();

            Directory.CreateDirectory(Path.GetDirectoryName(profilePath)!);
            File.WriteAllText(profilePath, profile.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
        }
        catch { }
    }

    private bool TapFirstVisibleTemplate(string[] templates, double threshold, Rect? roi, out string matchedTemplate, bool tap = true)
    {
        matchedTemplate = string.Empty;
        using Mat? screenshot = _adb.TakeScreenshot();
        if (screenshot == null || screenshot.Empty()) return false;

        foreach (string template in templates)
        {
            Point? center = _vision.FindElement(screenshot, template, threshold, roi, out double score);
            if (center == null) continue;
            matchedTemplate = template;
            if (tap) _adb.Tap(center.Value.X, center.Value.Y);
            return true;
        }
        return false;
    }

    private bool InterruptibleSleep(int milliseconds, CancellationToken token)
        => ThreadingUtil.InterruptibleSleep(milliseconds, token);
}
