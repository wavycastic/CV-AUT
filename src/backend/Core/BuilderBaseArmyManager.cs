using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using OpenCvSharp;
using Point = OpenCvSharp.Point;

namespace CvAut
{
    internal sealed record BuilderBaseArmyOptions(
        bool Enabled,
        string Formation);

    /// <summary>
    /// Quản lý army Builder Base theo kiểu an toàn:
    /// - Xác nhận camp/army bằng template troop trên màn prep.
    /// - Nếu thiếu, mở Army/Train UI và chọn đội hình bằng các template *_click hiện có.
    /// - Nếu hero chưa sẵn sàng, chờ có giới hạn rồi skip thay vì vào trận mù.
    /// </summary>
    internal sealed class BuilderBaseArmyManager
    {
        private readonly ADBHelper _adb;
        private readonly VisionEngine _vision;
        private readonly BuilderBaseNavigator _navigator;

        private const double ButtonThreshold = 0.60;
        private const double TroopThreshold = 0.50;
        private const double TrainTroopThreshold = 0.54;
        private static readonly Rect HomeAttackButtonRoi = Rect.FromLTRB(0, 560, 230, 850);
        private static readonly Rect AttackPrepTroopRoi = Rect.FromLTRB(30, 150, 420, 460);
        private static readonly Rect HeroPrepRoi = Rect.FromLTRB(20, 120, 430, 520);
        private static readonly Rect SlotScanLeftRoi = Rect.FromLTRB(45, 220, 608, 310);
        private static readonly Rect SlotScanRightRoi = Rect.FromLTRB(608, 220, 815, 310);
        private static readonly Rect CloseButtonRoi = Rect.FromLTRB(1320, 20, 1590, 220);
        private static readonly Rect TrainWindowRoi = Rect.FromLTRB(0, 0, 1600, 900);

        private static readonly string[] OpenAttackTemplates =
        {
            @"ui\attack_button",
            @"ui\icon_attack",
            @"ui\battle"
        };

        private static readonly string[] CloseTemplates =
        {
            @"ui\x_night",
            @"ui\close",
            "close"
        };

        private static readonly string[] ArmyWindowTemplates =
        {
            @"Smart_Auto_train\army_window",
            @"ui\army_window_crop"
        };

        private static readonly string[] BuilderTroopTemplates =
        {
            @"troops\builder_base\raged_barbarian",
            @"troops\builder_base\sneaky_archer",
            @"troops\builder_base\boxer_giant",
            @"troops\builder_base\beta_minion",
            @"troops\builder_base\bomber",
            @"troops\builder_base\baby_dragon_builder",
            @"troops\builder_base\cannon_cart",
            @"troops\builder_base\night_witch",
            @"troops\builder_base\drop_ship",
            @"troops\builder_base\power_pekka",
            @"troops\builder_base\hog_glider",
            @"troops\builder_base\electrofire_wizard"
        };

        private static readonly Dictionary<string, string[]> FormationTemplates = new(StringComparer.OrdinalIgnoreCase)
        {
            ["auto"] = new[] { @"troops\builder_base\power_pekka_click", @"troops\builder_base\baby_dragon_builder_click", @"troops\builder_base\cannon_cart_click", @"troops\builder_base\raged_barbarian_click" },
            ["power_pekka"] = new[] { @"troops\builder_base\power_pekka_click" },
            ["baby_dragon"] = new[] { @"troops\builder_base\baby_dragon_builder_click" },
            ["cannon_cart"] = new[] { @"troops\builder_base\cannon_cart_click" },
            ["night_witch"] = new[] { @"troops\builder_base\night_witch_click" },
            ["raged_barbarian"] = new[] { @"troops\builder_base\raged_barbarian_click" },
            ["sneaky_archer"] = new[] { @"troops\builder_base\sneaky_archer_click" },
            ["boxer_giant"] = new[] { @"troops\builder_base\boxer_giant_click" },
            ["beta_minion"] = new[] { @"troops\builder_base\beta_minion_click" },
            ["bomber"] = new[] { @"troops\builder_base\bomber_click" },
            ["drop_ship"] = new[] { @"troops\builder_base\drop_ship_click" },
            ["hog_glider"] = new[] { @"troops\builder_base\hog_glider_click" },
            ["electrofire_wizard"] = new[] { @"troops\builder_base\electrofire_wizard_click" }
        };

        public BuilderBaseArmyManager(ADBHelper adb, VisionEngine vision, BuilderBaseNavigator navigator)
        {
            _adb = adb;
            _vision = vision;
            _navigator = navigator;
        }

        public bool EnsureReadyForAttack(BuilderBaseArmyOptions options, CancellationToken token)
        {
            if (!options.Enabled)
            {
                Console.WriteLine("[BB-ARMY] phase=ensure status=skip reason=disabled");
                return true;
            }

            if (!_navigator.IsOnBuilderBase())
            {
                Console.WriteLine("[BB-ARMY] phase=ensure status=skip reason=not_on_builder_base");
                return false;
            }

            if (!OpenAttackPrep(token)) return false;
            bool ready = IsArmyReadyOnPrep(out int visibleTroops, out bool heroReady);
            Console.WriteLine($"[BB-ARMY] phase=prep_check status=pending visible_troops={visibleTroops} hero_ready={heroReady}");

            if (ready)
            {
                ClosePrep(token);
                Console.WriteLine("[BB-ARMY] phase=ensure status=success reason=already_ready");
                return true;
            }

            ClosePrep(token);

            if (!ready)
            {
                Console.WriteLine("[BB-ARMY] phase=prep_check status=action fill_army=true reason=visible_troops_missing");
                FillArmy(options.Formation, token);

                if (!OpenAttackPrep(token)) return false;
                bool nowReady = IsArmyReadyOnPrep(out visibleTroops, out heroReady);
                ClosePrep(token);

                if (nowReady)
                {
                    Console.WriteLine("[BB-ARMY] phase=ensure status=success reason=ready_after_fill");
                    return true;
                }
            }

            Console.WriteLine("[BB-ARMY] phase=ensure status=skip reason=army_or_hero_not_ready");
            return false;
        }

        private bool FillArmy(string formation, CancellationToken token)
        {
            Console.WriteLine($"[BB-ARMY] phase=fill status=start formation={formation}");
            if (!OpenArmyWindow(token))
            {
                Console.WriteLine("[BB-ARMY] phase=fill status=skip reason=army_window_not_detected");
                return false;
            }

            string[] templates = FormationTemplates.TryGetValue(formation, out string[]? selected) ? selected : FormationTemplates["auto"];
            int taps = 0;
            for (int round = 1; round <= 2 && !token.IsCancellationRequested; round++)
            {
                using Mat? screenshot = _adb.TakeScreenshot();
                if (screenshot == null || screenshot.Empty()) break;

                foreach (string template in templates)
                {
                    Point? center = _vision.FindElement(screenshot, template, TrainTroopThreshold, TrainWindowRoi, out double score);
                    if (center == null) continue;

                    _adb.Tap(center.Value.X, center.Value.Y);
                    taps++;
                    Console.WriteLine($"[BB-ARMY] phase=fill status=tap template=\"{template}\" score={score:F2} round={round}");
                    if (Sleep(220, token)) break;
                }
            }

            CloseArmyWindow(token);
            Console.WriteLine($"[BB-ARMY] phase=fill status=done taps={taps}");
            return taps > 0;
        }

        private bool IsArmyReadyOnPrep(out int visibleTroops, out bool heroReady)
        {
            visibleTroops = 0;
            heroReady = false;
            using Mat? screenshot = _adb.TakeScreenshot();
            if (screenshot == null || screenshot.Empty()) return false;

            if (IsArmyNotReadyByMbrPixel(screenshot))
            {
                Console.WriteLine("[BB-ARMY] phase=prep_check status=not_ready reason=mbr_red_army_pixel");
                return false;
            }

            var troopCenters = new List<Point>();
            foreach (string template in BuilderTroopTemplates)
            {
                Point? center = _vision.FindElement(screenshot, template, TroopThreshold, AttackPrepTroopRoi, out _)
                    ?? _vision.FindElement(screenshot, template, TroopThreshold, null, out _);
                if (center != null && !IsNearExisting(troopCenters, center.Value)) troopCenters.Add(center.Value);
            }

            visibleTroops = troopCenters.Count;
            int leftSlots = CountPrepSlots(screenshot, SlotScanLeftRoi);
            int rightSlots = CountPrepSlots(screenshot, SlotScanRightRoi);
            int totalSlots = leftSlots + rightSlots;
            int startSlotMem = totalSlots > 0 && totalSlots < 5 ? 27 : 21;
            int startSlotMem2 = rightSlots > 0 && totalSlots < 5 ? 27 : startSlotMem;
            Console.WriteLine($"[BB-ARMY] phase=slot_scan status=success left={leftSlots} right={rightSlots} total={totalSlots} mbr_start_slot={startSlotMem} mbr_start_slot2={startSlotMem2}");

            bool battleMachineReady = FindAny(screenshot, new[] { @"heroes\battle_machine", @"heroes\battle_machine2" }, 0.50, HeroPrepRoi);
            bool battleCopterReady = FindAny(screenshot, new[] { @"heroes\battle_copter" }, 0.50, HeroPrepRoi);
            heroReady = battleMachineReady || battleCopterReady;

            if (battleMachineReady)
            {
                Console.WriteLine("[BB-ARMY] phase=hero_ready status=success hero=battle_machine");
            }
            else if (battleCopterReady)
            {
                Console.WriteLine("[BB-ARMY] phase=hero_ready status=success hero=battle_copter");
            }
            else
            {
                Console.WriteLine("[BB-ARMY] phase=hero_ready status=skip reason=not_found");
            }

            // Builder Base không có hàng chờ train dài như main village; nếu prep có ít nhất một troop tab là có army để đánh.
            return visibleTroops > 0;
        }

        private int CountPrepSlots(Mat screenshot, Rect roi)
        {
            Rect safe = ImageUtils.ClampRect(roi, screenshot.Width, screenshot.Height);
            if (safe.Width <= 0 || safe.Height <= 0) return 0;

            var centers = new List<Point>();
            foreach (string template in BuilderTroopTemplates)
            {
                Point? center = _vision.FindElement(screenshot, template, 0.46, safe, out _);
                if (center != null && !IsNearExisting(centers, center.Value)) centers.Add(center.Value);
            }

            return centers.Count;
        }

        private static bool IsArmyNotReadyByMbrPixel(Mat screenshot)
        {
            // MBR CheckArmyReady samples around (123,245) for red 0xE84E52 on 860x732.
            if (TryGetPixel(screenshot, 123, 245, out Vec3b pixel) && IsColorNear(pixel, 0xE84E52, 20)) return true;

            int scaledX = (int)Math.Round(123 * (screenshot.Width / 860.0));
            int scaledY = (int)Math.Round(245 * (screenshot.Height / 732.0));
            return TryGetPixel(screenshot, scaledX, scaledY, out pixel) && IsColorNear(pixel, 0xE84E52, 20);
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

        private bool OpenAttackPrep(CancellationToken token)
        {
            if (TapFirstVisible(OpenAttackTemplates, ButtonThreshold, HomeAttackButtonRoi, token, "open_prep", out string matched))
            {
                Console.WriteLine($"[BB-ARMY] phase=open_prep status=success template=\"{matched}\"");
                return !Sleep(1400, token);
            }

            Console.WriteLine("[BB-ARMY] phase=open_prep status=fail reason=button_not_found");
            return false;
        }

        private bool OpenArmyWindow(CancellationToken token)
        {
            _adb.Tap(62, 658);
            if (Sleep(1200, token)) return false;

            using Mat? screenshot = _adb.TakeScreenshot();
            if (screenshot == null || screenshot.Empty()) return false;
            return ArmyWindowTemplates.Any(template => _vision.FindElement(screenshot, template, 0.55, null, out _) != null);
        }

        private void ClosePrep(CancellationToken token)
        {
            if (!TapFirstVisible(CloseTemplates, 0.55, CloseButtonRoi, token, "close_prep", out _))
            {
                _adb.Tap(1450, 90);
            }
            Sleep(800, token);
        }

        private void CloseArmyWindow(CancellationToken token)
        {
            _adb.Tap(1545, 81);
            Sleep(700, token);
        }

        private bool TapFirstVisible(string[] templates, double threshold, Rect? roi, CancellationToken token, string phase, out string matchedTemplate)
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
                Console.WriteLine($"[BB-ARMY] phase={phase} status=found template=\"{template}\" score={score:F2}");
                _adb.Tap(center.Value.X, center.Value.Y);
                return true;
            }

            return false;
        }

        private bool FindAny(Mat screenshot, string[] templates, double threshold, Rect? roi)
        {
            foreach (string template in templates)
            {
                if (_vision.FindElement(screenshot, template, threshold, roi, out _) != null) return true;
            }
            return false;
        }

        private static bool IsNearExisting(IEnumerable<Point> points, Point candidate)
        {
            foreach (Point point in points)
            {
                int dx = point.X - candidate.X;
                int dy = point.Y - candidate.Y;
                if (dx * dx + dy * dy <= 55 * 55) return true;
            }
            return false;
        }

        private static bool Sleep(int milliseconds, CancellationToken token) => token.WaitHandle.WaitOne(milliseconds);
    }
}
