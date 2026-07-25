using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using OpenCvSharp;
using Point = OpenCvSharp.Point;

namespace CvAut
{
    internal sealed class HeroAbilityController
    {
        private readonly IADBHelper _adb;
        private readonly IVisionEngine _vision;

        private int _machineLoopWaitCount;
        private int _machineLoopAbilityCount;

        public HeroAbilityController(IADBHelper adb, IVisionEngine vision)
        {
            _adb = adb;
            _vision = vision;
        }

        public void ResetCounters()
        {
            _machineLoopWaitCount = 0;
            _machineLoopAbilityCount = 0;
        }

        public Point? GetMachinePos(out string machineName)
        {
            machineName = string.Empty;
            using Mat? screenshot = _adb.TakeScreenshot();
            if (screenshot == null || screenshot.Empty()) return null;
            return GetMachinePos(screenshot, out machineName);
        }

        public Point? GetMachinePos(Mat screenshot, out string machineName)
        {
            machineName = string.Empty;
            foreach ((string Template, string Name) candidate in new[]
            {
                (@"heroes\battle_copter", "Battle Copter"),
                (@"heroes\battle_copter_a", "Battle Copter"),
                (@"heroes\battle_machine", "Battle Machine"),
                (@"heroes\battle_machine2", "Battle Machine"),
                (@"heroes\battle_machine_a", "Battle Machine")
            })
            {
                Point? center = _vision.FindElement(screenshot, candidate.Template, 0.48, BuilderBaseAttackLayout.HeroBarRoi, out double score);
                if (center == null) continue;
                machineName = candidate.Name;
                Console.WriteLine($"[BB-ATTACK] phase=machine status=detect template=\"{candidate.Template}\" score={score:F2} center=({center.Value.X},{center.Value.Y})");
                return center;
            }

            return null;
        }

        public void ActivateHeroAbility(CancellationToken token)
        {
            for (int attempt = 1; attempt <= 3 && !token.IsCancellationRequested; attempt++)
            {
                if (Sleep(1800, token)) return;

                using Mat? screenshot = _adb.TakeScreenshot();
                if (screenshot == null || screenshot.Empty()) return;

                foreach (string template in BuilderBaseAttackLayout.ActiveHeroTemplates)
                {
                    Point? center = _vision.FindElement(screenshot, template, 0.55, BuilderBaseAttackLayout.HeroBarRoi, out double score);
                    if (center == null) continue;

                    Console.WriteLine($"[BB-ATTACK] phase=hero_ability status=success template=\"{template}\" score={score:F2} attempt={attempt}");
                    _adb.Tap(center.Value.X, center.Value.Y);
                    return;
                }
            }

            Console.WriteLine("[BB-ATTACK] phase=hero_ability status=skip reason=active_hero_not_found");
        }

        public bool TryActivateHeroAbilityOnce()
        {
            using Mat? screenshot = _adb.TakeScreenshot();
            if (screenshot == null || screenshot.Empty()) return false;

            foreach (string template in BuilderBaseAttackLayout.ActiveHeroTemplates)
            {
                Point? center = _vision.FindElement(screenshot, template, 0.55, BuilderBaseAttackLayout.HeroBarRoi, out double score);
                if (center == null) continue;

                Console.WriteLine($"[BB-ATTACK] phase=hero_ability status=success template=\"{template}\" score={score:F2}");
                _adb.Tap(center.Value.X, center.Value.Y);
                return true;
            }

            return false;
        }

        public void TryActivateBomberAbility(BuilderBaseTroopSlot slot)
        {
            using Mat? screenshot = _adb.TakeScreenshot();
            if (screenshot == null || screenshot.Empty()) return;

            Rect roi = Rect.FromLTRB(Math.Max(0, slot.Center.X - 45), Math.Max(0, slot.Center.Y - 60), Math.Min(BuilderBaseAttackLayout.ScreenWidth, slot.Center.X + 95), Math.Min(BuilderBaseAttackLayout.ScreenHeight, slot.Center.Y + 50));
            Point? ability = null;
            double score = 0;
            foreach (string template in BuilderBaseAttackLayout.BomberAbilityTemplates)
            {
                ability = _vision.FindElement(screenshot, template, 0.45, roi, out score);
                if (ability != null) break;
            }
            if (ability == null)
            {
                ability = FindMbrReadyAbilityPixel(screenshot, roi);
                if (ability == null) return;
            }

            Console.WriteLine($"[BB-ATTACK] phase=bomber_ability status=success score={score:F2} slot={slot.Index} reason={(score > 0 ? "template" : "mbr_ready_pixel")}");
            _adb.Tap(ability.Value.X, ability.Value.Y);
        }

        public void ConfirmMachineDeployAndAbility(string machineName, CancellationToken token)
        {
            for (int attempt = 1; attempt <= 16 && !token.IsCancellationRequested; attempt++)
            {
                if (Sleep(250, token)) return;
                if (TryActivateHeroAbilityOnce())
                {
                    Console.WriteLine($"[BB-ATTACK] phase=machine status=deployed action=ability name=\"{machineName}\" attempt={attempt}");
                    return;
                }
            }

            Console.WriteLine($"[BB-ATTACK] phase=machine status=deployed action=ability_not_ready name=\"{machineName}\"");
        }

        public static Point? FindMbrReadyAbilityPixel(Mat screenshot, Rect roi)
        {
            Rect safe = ImageUtils.ClampRect(roi, screenshot.Width, screenshot.Height);
            if (safe.Width <= 0 || safe.Height <= 0) return null;
            int step = Math.Max(2, safe.Width / 55);
            for (int y = safe.Top; y < safe.Bottom; y += step)
            {
                for (int x = safe.Left; x < safe.Right; x += step)
                {
                    Vec3b pixel = screenshot.At<Vec3b>(y, x);
                    int b = pixel.Item0, g = pixel.Item1, r = pixel.Item2;
                    bool violetReady = r >= 165 && b >= 165 && g <= 120 && Math.Abs(r - b) <= 90;
                    bool electricBlue = b >= 170 && g >= 120 && r <= 130;
                    if (violetReady || electricBlue) return new Point(x, y);
                }
            }
            return null;
        }

        public bool CheckMachineAbilityLoop()
        {
            Point? machine = GetMachinePos(out string machineName);
            if (IsMachineDeadByMbrPixel())
            {
                Console.WriteLine($"[BB-ATTACK] phase=machine_loop status=dead name=\"{(string.IsNullOrWhiteSpace(machineName) ? "machine" : machineName)}\" reason=mbr_dead_pixel");
                return false;
            }

            if (machine == null)
            {
                Console.WriteLine("[BB-ATTACK] phase=machine_loop status=skip reason=machine_not_on_bar_or_dead");
                return false;
            }

            if (IsMachineAbilityWaiting(machine.Value))
            {
                _machineLoopWaitCount++;
                Console.WriteLine($"[BB-ATTACK] phase=machine_loop status=wait name=\"{machineName}\" reason=ability_wait_state count={_machineLoopWaitCount}");
                return true;
            }

            bool activated = TryActivateHeroAbilityOnce();
            if (activated) _machineLoopAbilityCount++;
            _machineLoopWaitCount = 0;
            Console.WriteLine($"[BB-ATTACK] phase=machine_loop status={(activated ? "ability" : "alive")} name=\"{machineName}\" ability_count={_machineLoopAbilityCount}");
            return true;
        }

        public bool CheckBomberAbilityLoop(List<BuilderBaseTroopSlot> activeBomberSlots)
        {
            if (activeBomberSlots.Count == 0) return false;
            int aliveOrUnknown = 0;
            foreach (BuilderBaseTroopSlot bomber in activeBomberSlots.ToArray())
            {
                if (IsSlotBannerGrey(bomber))
                {
                    Console.WriteLine($"[BB-ATTACK] phase=bomber_loop status=dead slot={bomber.Index}");
                    activeBomberSlots.RemoveAll(s => s.Index == bomber.Index);
                    continue;
                }

                aliveOrUnknown++;
                TryActivateBomberAbility(bomber);
            }

            return aliveOrUnknown > 0;
        }

        public bool IsMachineDeadByMbrPixel()
        {
            using Mat? screenshot = _adb.TakeScreenshot();
            if (screenshot == null || screenshot.Empty()) return false;
            return IsMachineDeadByMbrPixel(screenshot);
        }

        public bool IsMachineDeadByMbrPixel(Mat screenshot)
        {
            int x = 71;
            int y = 663 + (BuilderBaseAttackLayout.ScreenHeight - 900);
            if (TryGetPixel(screenshot, x, y, out Vec3b pixel)
                && IsColorNear(pixel, 0x4E4E4E, 20))
            {
                return true;
            }

            int scaledX = (int)Math.Round(71 * (screenshot.Width / 860.0));
            int scaledY = (int)Math.Round(663 * (screenshot.Height / 732.0));
            return TryGetPixel(screenshot, scaledX, scaledY, out pixel) && IsColorNear(pixel, 0x4E4E4E, 20);
        }

        private bool IsMachineAbilityWaiting(Point machine)
        {
            using Mat? screenshot = _adb.TakeScreenshot();
            if (screenshot == null || screenshot.Empty()) return false;

            Rect roi = ImageUtils.ClampRect(Rect.FromLTRB(machine.X - 35, machine.Y - 40, machine.X + 35, machine.Y + 40), screenshot.Width, screenshot.Height);
            if (roi.Width <= 0 || roi.Height <= 0) return false;
            using Mat sample = new(screenshot, roi);
            Scalar mean = Cv2.Mean(sample);
            double spread = Math.Abs(mean.Val0 - mean.Val1) + Math.Abs(mean.Val1 - mean.Val2) + Math.Abs(mean.Val0 - mean.Val2);
            return spread < 18 && mean.Val0 is > 45 and < 120;
        }

        private bool IsSlotBannerGrey(BuilderBaseTroopSlot slot)
        {
            using Mat? screenshot = _adb.TakeScreenshot();
            if (screenshot == null || screenshot.Empty()) return false;
            int bannerX = slot.Center.X + 37;
            int bannerY = 583 + (BuilderBaseAttackLayout.ScreenHeight - 900);
            if (TryGetPixel(screenshot, bannerX, bannerY, out Vec3b mbrPixel)
                && IsColorNear(mbrPixel, 0x707070, 10))
            {
                return true;
            }

            Rect roi = ImageUtils.ClampRect(Rect.FromLTRB(slot.Center.X + 25, slot.Center.Y - 5, slot.Center.X + 55, slot.Center.Y + 25), screenshot.Width, screenshot.Height);
            if (roi.Width <= 0 || roi.Height <= 0) return false;
            using Mat sample = new(screenshot, roi);
            Scalar mean = Cv2.Mean(sample);
            double spread = Math.Abs(mean.Val0 - mean.Val1) + Math.Abs(mean.Val1 - mean.Val2) + Math.Abs(mean.Val0 - mean.Val2);
            return spread < 24 && mean.Val0 is > 60 and < 145;
        }

        private static bool TryGetPixel(Mat image, int x, int y, out Vec3b pixel)
        {
            pixel = default;
            if (x < 0 || y < 0 || x >= image.Width || y >= image.Height) return false;
            pixel = image.At<Vec3b>(y, x);
            return true;
        }

        private static bool IsColorNear(Vec3b pixel, int rgb, int tolerance)
        {
            int r = (rgb >> 16) & 0xFF;
            int g = (rgb >> 8) & 0xFF;
            int b = rgb & 0xFF;
            return Math.Abs(pixel.Item2 - r) <= tolerance
                && Math.Abs(pixel.Item1 - g) <= tolerance
                && Math.Abs(pixel.Item0 - b) <= tolerance;
        }

        private static bool Sleep(int milliseconds, CancellationToken token) => token.WaitHandle.WaitOne(milliseconds);
    }
}
