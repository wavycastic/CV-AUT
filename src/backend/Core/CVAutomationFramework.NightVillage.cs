using System;
using System.Text.Json;
using System.Threading;
using CvAut.Automation;
using OpenCvSharp;

namespace CvAut
{
    internal partial class CVAutomationFramework
    {
        private bool IsNightVillageMode(JsonElement cfg, int villageIdx)
            => VillageModeResolver.IsNightVillage(
                cfg,
                _configService.RunSession,
                villageIdx);

        private void DismissBuilderBasePopups(CancellationToken token)
        {
            string[] popupTemplates =
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

            for (int attempt = 1; attempt <= 3 && !CheckStop(token); attempt++)
            {
                using Mat? screenshot = _adb.TakeScreenshot();
                if (screenshot == null || screenshot.Empty()) return;
                bool tapped = false;
                foreach (string template in popupTemplates)
                {
                    Point? center = _vision.FindElement(screenshot, template, 0.50, null, out double score);
                    if (center == null) continue;
                    Console.WriteLine($"[BB-CS] phase=post_attack status=pending step=clear_popup attempt={attempt} template=\"{template}\" score={score:F2}");
                    _adb.Tap(center.Value.X, center.Value.Y);
                    InterruptibleSleep(900, token);
                    tapped = true;
                    break;
                }
                if (!tapped) return;
            }
        }

        private bool EnsureBuilderBaseEntry(CancellationToken token)
        {
            Console.WriteLine("[BB-CS] phase=entry status=start target=builder_base");

            if (_builderBaseNavigator.IsOnBuilderBase())
            {
                Console.WriteLine("[BB-CS] phase=entry status=success target=builder_base reason=already_there");
                return true;
            }

            Console.WriteLine("[BB-CS] phase=entry status=pending step=detect_current_village");
            DateTime detectDeadline = DateTime.Now.AddSeconds(50);
            bool onMainVillage = false;
            while (DateTime.Now < detectDeadline && !CheckStop(token))
            {
                if (_builderBaseNavigator.IsOnBuilderBase())
                {
                    Console.WriteLine("[BB-CS] phase=entry status=success target=builder_base reason=already_there_after_wait");
                    return true;
                }

                if (_builderBaseNavigator.IsOnMainVillage())
                {
                    onMainVillage = true;
                    break;
                }

                if (InterruptibleSleep(1000, token)) return false;
            }

            if (!onMainVillage)
            {
                Console.WriteLine("[BB-CS WARNING] phase=entry status=pending action=recover reason=unknown_village_state");
                BootRecovery();
            }

            if (!_builderBaseNavigator.SwitchToBuilderBase(token))
            {
                Console.WriteLine("[BB-CS ERROR] phase=entry status=fail target=builder_base reason=switch_failed");
                return false;
            }

            Console.WriteLine("[BB-CS] phase=entry status=success target=builder_base");
            return true;
        }
    }
}
