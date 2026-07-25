using System;
using System.Threading;
using OpenCvSharp;
using Point = OpenCvSharp.Point;

namespace CvAut
{
    internal sealed class AttackEntryFlow
    {
        private readonly IADBHelper _adb;
        private readonly IVisionEngine _vision;
        private readonly BuilderBaseNavigator _navigator;

        public AttackEntryFlow(IADBHelper adb, IVisionEngine vision, BuilderBaseNavigator navigator)
        {
            _adb = adb;
            _vision = vision;
            _navigator = navigator;
        }

        public bool WaitForAttackReady(CancellationToken token, string phase, int retries)
        {
            bool requirePrepScreen = !string.Equals(phase, "attack_entry", StringComparison.Ordinal);
            for (int attempt = 1; attempt <= Math.Max(1, retries) && !token.IsCancellationRequested; attempt++)
            {
                bool ready = requirePrepScreen ? HasVisibleTroopsOnPrepScreen() : _navigator.IsOnBuilderBase();
                if (ready)
                {
                    Console.WriteLine($"[BB-ATTACK] phase={phase} status=ready attempt={attempt} reason={(requirePrepScreen ? "prep_screen_detected" : "builder_base_detected")}");
                    return true;
                }
                Console.WriteLine($"[BB-ATTACK] phase={phase} status=retry attempt={attempt} action=wait_ready");
                if (Sleep(1200, token)) return false;
            }

            return requirePrepScreen ? HasVisibleTroopsOnPrepScreen() : _navigator.IsOnBuilderBase();
        }

        public bool TapFirstVisible(string[] templates, double threshold, Rect? roi, CancellationToken token, out string matchedTemplate)
        {
            matchedTemplate = string.Empty;
            if (token.IsCancellationRequested) return false;

            using Mat? screenshot = _adb.TakeScreenshot();
            if (screenshot == null || screenshot.Empty()) return false;

            foreach (string template in templates)
            {
                Point? center = _vision.FindElement(screenshot, template, threshold, roi, out double score);
                if (center == null) continue;

                matchedTemplate = template;
                Console.WriteLine($"[BB-ATTACK] phase=template status=success template=\"{template}\" score={score:F2} center=({center.Value.X},{center.Value.Y})");
                _adb.Tap(center.Value.X, center.Value.Y);
                return true;
            }

            return false;
        }

        public bool HasVisibleTroopsOnPrepScreen()
        {
            using Mat? screenshot = _adb.TakeScreenshot();
            if (screenshot == null || screenshot.Empty()) return false;

            foreach (string template in BuilderBaseAttackLayout.BuilderTroopTemplates)
            {
                Point? center = _vision.FindElement(screenshot, template, BuilderBaseAttackLayout.TroopThreshold, BuilderBaseAttackLayout.AttackPrepTroopRoi, out double score)
                    ?? _vision.FindElement(screenshot, template, BuilderBaseAttackLayout.TroopThreshold, null, out score);
                if (center == null) continue;

                Console.WriteLine($"[BB-ATTACK] phase=army_ready status=found template=\"{template}\" score={score:F2} center=({center.Value.X},{center.Value.Y})");
                return true;
            }

            return false;
        }

        public void CloseAttackPrep(CancellationToken token)
        {
            if (TapFirstVisible(BuilderBaseAttackLayout.CloseTemplates, 0.55, BuilderBaseAttackLayout.CloseButtonRoi, token, out string matched))
            {
                Console.WriteLine($"[BB-ATTACK] phase=close_prep status=success template=\"{matched}\"");
                Sleep(800, token);
                return;
            }

            _adb.Tap(1450, 90);
            Sleep(800, token);
        }

        public bool WaitForBattleScreen(CancellationToken token)
        {
            for (int i = 1; i <= 20 && !token.IsCancellationRequested; i++)
            {
                using Mat? screenshot = _adb.TakeScreenshot();
                if (screenshot != null && !screenshot.Empty())
                {
                    foreach (string template in BuilderBaseAttackLayout.BuilderTroopTemplates)
                    {
                        Point? center = _vision.FindElement(screenshot, template, BuilderBaseAttackLayout.TroopThreshold, BuilderBaseAttackLayout.DeployBarRoi, out double score);
                        if (center != null)
                        {
                            Console.WriteLine($"[BB-ATTACK] phase=wait_battle status=success template=\"{template}\" score={score:F2}");
                            return true;
                        }
                    }
                }

                Console.WriteLine($"[BB-ATTACK] phase=wait_battle status=pending attempt={i}");
                if (Sleep(1500, token)) return false;
            }

            Console.WriteLine("[BB-ATTACK] phase=wait_battle status=fail reason=troop_bar_not_detected");
            return false;
        }

        public bool ClickFindNowIfRequired(CancellationToken token)
        {
            if (WaitForBattleScreenQuick(token))
            {
                Console.WriteLine("[BB-ATTACK] phase=find_now status=skip reason=already_in_battle");
                return true;
            }

            for (int attempt = 1; attempt <= 5 && !token.IsCancellationRequested; attempt++)
            {
                if (TapFirstVisible(BuilderBaseAttackLayout.FindNowTemplates, 0.56, BuilderBaseAttackLayout.FindNowButtonRoi, token, out string template))
                {
                    Console.WriteLine($"[BB-ATTACK] phase=find_now status=success template=\"{template}\" attempt={attempt}");
                    Sleep(1800, token);
                    return true;
                }

                Console.WriteLine($"[BB-ATTACK] phase=find_now status=retry attempt={attempt} reason=button_not_found");
                if (Sleep(700, token)) return false;
            }

            bool inBattle = WaitForBattleScreenQuick(token);
            Console.WriteLine($"[BB-ATTACK] phase=find_now status={(inBattle ? "skip" : "fail")} reason={(inBattle ? "battle_detected_after_wait" : "button_not_found_after_retry")}");
            return inBattle;
        }

        public bool WaitCloudsAndEnemyVillage(CancellationToken token)
        {
            int cloudTicks = 0;
            for (int attempt = 1; attempt <= 30 && !token.IsCancellationRequested; attempt++)
            {
                if (WaitForBattleScreenQuick(token) && IsEnemyVillageLoaded())
                {
                    Console.WriteLine($"[BB-ATTACK] phase=cloud status=success attempt={attempt} cloud_ticks={cloudTicks}");
                    return true;
                }

                cloudTicks++;
                if (attempt == 21)
                {
                    Console.WriteLine("[BB-ATTACK] phase=cloud status=retry attempt=21 action=android_back_and_find_now");
                    _adb.ExecuteShell("input keyevent 4");
                    Sleep(1000, token);
                    ClickFindNowIfRequired(token);
                }
                else if (attempt == 26)
                {
                    Console.WriteLine("[BB-ATTACK] phase=cloud status=retry attempt=26 action=android_back_and_find_now");
                    _adb.ExecuteShell("input keyevent 4");
                    Sleep(1000, token);
                    ClickFindNowIfRequired(token);
                }
                else
                {
                    Console.WriteLine($"[BB-ATTACK] phase=cloud status=pending attempt={attempt}");
                }

                if (Sleep(1000, token)) return false;
            }

            return false;
        }

        public bool WaitForEnemyVillageLoaded(CancellationToken token, string phase)
        {
            for (int attempt = 1; attempt <= 10 && !token.IsCancellationRequested; attempt++)
            {
                if (IsEnemyVillageLoaded())
                {
                    Console.WriteLine($"[BB-ATTACK] phase={phase} status=success attempt={attempt}");
                    return true;
                }

                Console.WriteLine($"[BB-ATTACK] phase={phase} status=pending attempt={attempt}");
                if (Sleep(1000, token)) return false;
            }

            return false;
        }

        public bool DetectObstructedLayout()
        {
            using Mat? screenshot = _adb.TakeScreenshot();
            if (screenshot == null || screenshot.Empty()) return false;
            foreach (string template in BuilderBaseAttackLayout.ObstructedTemplates)
            {
                if (_vision.FindElement(screenshot, template, 0.55, null, out double score) == null) continue;
                Console.WriteLine($"[BB-ATTACK] phase=obstructed status=detected template=\"{template}\" score={score:F2}");
                return true;
            }

            return false;
        }

        public bool WaitForBattleScreenQuick(CancellationToken token)
        {
            for (int i = 1; i <= 3 && !token.IsCancellationRequested; i++)
            {
                using Mat? screenshot = _adb.TakeScreenshot();
                if (screenshot != null && !screenshot.Empty())
                {
                    foreach (string template in BuilderBaseAttackLayout.BuilderTroopTemplates)
                    {
                        if (_vision.FindElement(screenshot, template, BuilderBaseAttackLayout.TroopThreshold, BuilderBaseAttackLayout.DeployBarRoi, out _) != null) return true;
                    }
                }

                if (Sleep(350, token)) return false;
            }

            return false;
        }

        public bool IsEnemyVillageLoaded()
        {
            using Mat? screenshot = _adb.TakeScreenshot();
            if (screenshot == null || screenshot.Empty()) return false;

            foreach (string template in BuilderBaseAttackLayout.BuilderTroopTemplates)
            {
                if (_vision.FindElement(screenshot, template, BuilderBaseAttackLayout.TroopThreshold, BuilderBaseAttackLayout.DeployBarRoi, out _) != null)
                {
                    return true;
                }
            }

            bool hasMapSignal = _vision.FindElement(screenshot, @"ui\surrender_button", 0.48, null, out _) != null
                || _vision.FindElement(screenshot, @"ui\surrender", 0.48, null, out _) != null;
            if (hasMapSignal) return true;

            Rect safe = ImageUtils.ClampRect(BuilderBaseAttackLayout.EnemyVillageRoi, screenshot.Width, screenshot.Height);
            if (safe.Width <= 0 || safe.Height <= 0) return false;
            using Mat roi = new(screenshot, safe);
            Scalar mean = Cv2.Mean(roi);
            return mean.Val0 + mean.Val1 + mean.Val2 > 45;
        }

        private static bool Sleep(int milliseconds, CancellationToken token) => token.WaitHandle.WaitOne(milliseconds);
    }
}
