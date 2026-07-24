using System;
using System.Text.Json;
using System.Threading;
using OpenCvSharp;
using static CvAut.ConfigManager;

namespace CvAut
{
    internal partial class CVAutomationFramework
    {
        private bool IsNightVillageMode(JsonElement cfg, int villageIdx)
        {
            string rootPlayMode = GetStringOrDefault(cfg, "play_mode", string.Empty);
            if (rootPlayMode.Equals("night_village", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            JsonElement session = GetObjectOrDefault(cfg, "run_session");
            string playMode = GetStringOrDefault(session, "play_mode", string.Empty);
            if (playMode.Equals("night_village", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            JsonElement multi = GetObjectOrDefault(cfg, "multi_account");
            if (multi.ValueKind == JsonValueKind.Object
                && multi.TryGetProperty("accounts", out JsonElement accounts)
                && accounts.ValueKind == JsonValueKind.Array)
            {
                foreach (JsonElement account in accounts.EnumerateArray())
                {
                    if (GetIntOrDefault(account, "profileVillage", 0) == villageIdx
                        && GetStringOrDefault(account, "targetVillage", "main_village").Equals("night_village", StringComparison.OrdinalIgnoreCase))
                    {
                        return true;
                    }
                }
            }

            return false;
        }

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
