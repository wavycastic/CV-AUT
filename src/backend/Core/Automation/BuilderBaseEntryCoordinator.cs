using System;
using System.Threading;
using OpenCvSharp;

namespace CvAut.Automation;

internal sealed class BuilderBaseEntryCoordinator
{
    private static readonly string[] PopupTemplates =
    {
        @"ui\okay_battle_rank",
        @"ui\okay_star",
        @"ui\okay",
        @"ui\okay_n",
        @"ui\okay_n2",
        @"ui\bonus",
        @"ui\challenge_complete",
        @"ui\star_bonus_received",
        @"ui\close",
        @"ui\x_night"
    };

    private readonly IADBHelper _adb;
    private readonly VisionEngine _vision;
    private readonly BuilderBaseNavigator _navigator;

    public BuilderBaseEntryCoordinator(
        IADBHelper adb,
        VisionEngine vision,
        BuilderBaseNavigator navigator)
    {
        _adb = adb ?? throw new ArgumentNullException(nameof(adb));
        _vision = vision ?? throw new ArgumentNullException(nameof(vision));
        _navigator = navigator ?? throw new ArgumentNullException(nameof(navigator));
    }

    public void DismissPopups(
        CancellationToken token,
        Func<CancellationToken, bool> shouldStop,
        Func<int, CancellationToken, bool> sleep)
    {
        for (int attempt = 1; attempt <= 3 && !shouldStop(token); attempt++)
        {
            using Mat? screenshot = _adb.TakeScreenshot();
            if (screenshot == null || screenshot.Empty()) return;

            bool tapped = false;
            foreach (string template in PopupTemplates)
            {
                Point? center = _vision.FindElement(
                    screenshot,
                    template,
                    0.50,
                    null,
                    out double score);
                if (center == null) continue;

                Console.WriteLine($"[BB-CS] phase=post_attack status=pending step=clear_popup attempt={attempt} template=\"{template}\" score={score:F2}");
                _adb.Tap(center.Value.X, center.Value.Y);
                sleep(900, token);
                tapped = true;
                break;
            }

            if (!tapped) return;
        }
    }

    public bool EnsureEntry(
        CancellationToken token,
        Func<CancellationToken, bool> shouldStop,
        Func<int, CancellationToken, bool> sleep,
        Action recover)
    {
        Console.WriteLine("[BB-CS] phase=entry status=start target=builder_base");

        if (_navigator.IsOnBuilderBase())
        {
            Console.WriteLine("[BB-CS] phase=entry status=success target=builder_base reason=already_there");
            return true;
        }

        Console.WriteLine("[BB-CS] phase=entry status=pending step=detect_current_village");
        DateTime detectDeadline = DateTime.Now.AddSeconds(50);
        bool onMainVillage = false;
        while (DateTime.Now < detectDeadline && !shouldStop(token))
        {
            if (_navigator.IsOnBuilderBase())
            {
                Console.WriteLine("[BB-CS] phase=entry status=success target=builder_base reason=already_there_after_wait");
                return true;
            }

            if (_navigator.IsOnMainVillage())
            {
                onMainVillage = true;
                break;
            }

            if (sleep(1000, token)) return false;
        }

        if (!onMainVillage)
        {
            Console.WriteLine("[BB-CS WARNING] phase=entry status=pending action=recover reason=unknown_village_state");
            recover();
        }

        if (!_navigator.SwitchToBuilderBase(token))
        {
            Console.WriteLine("[BB-CS ERROR] phase=entry status=fail target=builder_base reason=switch_failed");
            return false;
        }

        Console.WriteLine("[BB-CS] phase=entry status=success target=builder_base");
        return true;
    }
}
