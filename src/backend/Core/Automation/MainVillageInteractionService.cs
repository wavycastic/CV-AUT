using System;
using System.Threading;
using OpenCvSharp;

namespace CvAut.Automation;

internal sealed class MainVillageInteractionService
{
    private readonly IADBHelper _adb;
    private readonly IVisionEngine _vision;

    public MainVillageInteractionService(IADBHelper adb, IVisionEngine vision)
    {
        _adb = adb ?? throw new ArgumentNullException(nameof(adb));
        _vision = vision ?? throw new ArgumentNullException(nameof(vision));
    }

    public void RunDonateOnlyCycle(
        MainVillageConfig config,
        CancellationToken token,
        Func<CancellationToken, bool> shouldStop,
        Func<int, CancellationToken, bool> sleep)
    {
        Console.WriteLine("[DONATE-CS] phase=donate_only status=start");
        TryUseCake(config, token, shouldStop, sleep);
        TryRequestTroops(config, token, shouldStop);
        TryDonateOnce(token, shouldStop, sleep);
        sleep(5000, token);
        Console.WriteLine("[DONATE-CS] phase=donate_only status=success");
    }

    public void TryRequestTroops(
        MainVillageConfig config,
        CancellationToken token,
        Func<CancellationToken, bool> shouldStop)
    {
        if (!config.RequestTroops || shouldStop(token)) return;

        Console.WriteLine("[REQUEST-CS] phase=request_troops status=start");
        if (TapFirstVisibleTemplate(
            new[] { @"ui\request_button_unavailable", "request_button_unavailable" },
            0.78,
            null,
            out _,
            tap: false))
        {
            Console.WriteLine("[REQUEST-CS] phase=request_troops status=skip reason=cooldown_or_unavailable");
            return;
        }

        if (!TapFirstVisibleTemplate(
            new[] { @"ui\request_troops", @"ui\request_button", "request_button" },
            0.70,
            null,
            out string matched))
        {
            Console.WriteLine("[REQUEST-CS] phase=request_troops status=fail reason=request_button_not_found");
            return;
        }

        TapFirstVisibleTemplate(
            new[] { @"ui\request_button", "request_button" },
            0.70,
            null,
            out _,
            tap: true);
        Console.WriteLine($"[REQUEST-CS] phase=request_troops status=success template=\"{matched}\"");
    }

    public void TryUseCake(
        MainVillageConfig config,
        CancellationToken token,
        Func<CancellationToken, bool> shouldStop,
        Func<int, CancellationToken, bool> sleep)
    {
        if (!config.UseCake || shouldStop(token)) return;

        Console.WriteLine("[EVENT-CS] phase=use_cake status=start");
        if (TapFirstVisibleTemplate(
            new[] { @"ui\clan_castle_cake", "clan_castle_cake" },
            0.72,
            null,
            out string matched))
        {
            Console.WriteLine($"[EVENT-CS] phase=use_cake status=success template=\"{matched}\"");
            sleep(1000, token);
            return;
        }

        Console.WriteLine("[EVENT-CS] phase=use_cake status=skip reason=item_not_found");
    }

    public void TryDonateOnce(
        CancellationToken token,
        Func<CancellationToken, bool> shouldStop,
        Func<int, CancellationToken, bool> sleep)
    {
        if (shouldStop(token)) return;

        Console.WriteLine("[DONATE-CS] phase=scan_chat status=start");
        if (!TapFirstVisibleTemplate(
            new[] { @"ui\donate_button", "donate_button" },
            0.72,
            null,
            out string matched))
        {
            Console.WriteLine("[DONATE-CS] phase=scan_chat status=skip reason=donate_button_not_found");
            return;
        }

        Console.WriteLine($"[DONATE-CS] phase=donate status=pending template=\"{matched}\" details=\"donate_panel_opened\"");
        sleep(700, token);
    }

    public bool TapFirstVisibleTemplate(
        string[] templates,
        double threshold,
        Rect? roi,
        out string matchedTemplate,
        bool tap = true)
    {
        matchedTemplate = string.Empty;
        using Mat? screenshot = _adb.TakeScreenshot();
        if (screenshot == null || screenshot.Empty()) return false;

        foreach (string template in templates)
        {
            Point? center = _vision.FindElement(
                screenshot,
                template,
                threshold,
                roi,
                out double score);
            if (center == null) continue;

            matchedTemplate = template;
            Console.WriteLine($"[VISION] phase=template_match status=success template=\"{template}\" score={score:F2} center=({center.Value.X},{center.Value.Y})");
            if (tap)
                _adb.Tap(center.Value.X, center.Value.Y);
            return true;
        }

        return false;
    }

    public bool ShouldSmartSurrender(
        DateTime battleStart,
        SmartSurrenderConfig config,
        out string reason)
    {
        reason = "none";
        double elapsedSeconds = (DateTime.Now - battleStart).TotalSeconds;
        if (config.AfterSecondsEnabled
            && config.AfterSeconds > 0
            && elapsedSeconds >= config.AfterSeconds)
        {
            reason = "time_limit";
            return true;
        }

        if (config.LowResourcesEnabled && config.LowResourcesThreshold > 0)
        {
            var resources = IsTarget.ExtractResources(_adb, _vision);
            int remainingTotal = resources.Gold + resources.Elixir;
            if (remainingTotal > 0 && remainingTotal <= config.LowResourcesThreshold)
            {
                reason = "low_resources";
                return true;
            }
        }

        return false;
    }

    public void ExecuteSurrender(
        string reason,
        CancellationToken token,
        Func<int, CancellationToken, bool> sleep)
    {
        _adb.Tap(80, 780);
        if (sleep(1000, token)) return;
        _adb.Tap(960, 560);
        Console.WriteLine($"[ATTACK-CS] phase=surrender status=success reason={reason}");
        sleep(2000, token);
    }
}
