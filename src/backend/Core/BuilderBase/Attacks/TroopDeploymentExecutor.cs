using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using OpenCvSharp;
using Point = OpenCvSharp.Point;

namespace CvAut
{
    internal sealed class TroopDeploymentExecutor
    {
        private readonly IADBHelper _adb;
        private readonly IVisionEngine _vision;
        private readonly Random _random;
        private readonly AttackBarScanner _barScanner;
        private readonly HeroAbilityController _heroController;
        private readonly List<BuilderBaseTroopSlot> _activeBomberSlots = new();
        private BuilderBaseDropPlanner? _currentDropPlanner;

        public TroopDeploymentExecutor(IADBHelper adb, IVisionEngine vision, Random random, AttackBarScanner barScanner, HeroAbilityController heroController)
        {
            _adb = adb;
            _vision = vision;
            _random = random;
            _barScanner = barScanner;
            _heroController = heroController;
        }

        public List<BuilderBaseTroopSlot> ActiveBomberSlots => _activeBomberSlots;

        public void DeployAllVisibleTroops(BuilderBaseBattleOptions options, CancellationToken token, bool secondAttack)
        {
            string attackSide = _random.Next(2) == 0 ? "left" : "right";
            _currentDropPlanner = BuildDropPlanner();
            List<Point> previewDropPoints = _currentDropPlanner.ChooseDropPoints("default", attackSide, _random);
            if (previewDropPoints.Count == 0)
            {
                Console.WriteLine("[BB-ATTACK] phase=deploy status=fail reason=no_valid_drop_points");
                return;
            }
            Console.WriteLine($"[BB-ATTACK] phase=deploy status=start attack_side={attackSide} side_points={previewDropPoints.Count} source={_currentDropPlanner.Source} second_attack={secondAttack}");
            _activeBomberSlots.Clear();
            List<BuilderBaseTroopSlot> remaining = _barScanner.ReadAttackBarSlots(remaining: false, secondAttack: secondAttack);
            Console.WriteLine($"[BB-ATTACK] phase=deploy status=attack_bar_refresh slots={remaining.Count}");
            if (remaining.Count == 0)
            {
                if (Sleep(700, token)) return;
                remaining = _barScanner.ReadAttackBarSlots(remaining: false, secondAttack: secondAttack);
                Console.WriteLine($"[BB-ATTACK] phase=deploy status=attack_bar_retry slots={remaining.Count}");
                if (remaining.Count == 0)
                {
                    AttackDebugRecorder.CaptureDebugSnapshot(_adb, "attack_bar_empty_before_deploy");
                    Console.WriteLine("[BB-ATTACK] phase=deploy status=fail reason=attack_bar_empty_before_deploy");
                    return;
                }
            }

            for (int pass = 1; pass <= 4 && !token.IsCancellationRequested; pass++)
            {
                List<BuilderBaseTroopSlot> slots = pass == 1 ? remaining : _barScanner.ReadAttackBarSlots(remaining: true, secondAttack: secondAttack);
                if (slots.Count == 0) break;
                foreach (BuilderBaseTroopSlot slot in DropOrderPolicy.OrderSlots(slots, options))
                {
                    if (token.IsCancellationRequested) return;
                    DeploySlot(slot, options, attackSide, token);
                }
            }

            Console.WriteLine("[BB-ATTACK] phase=deploy status=done");
        }

        private void DeploySlot(BuilderBaseTroopSlot slot, BuilderBaseBattleOptions options, string attackSide, CancellationToken token)
        {
            List<Point> dropPoints = (_currentDropPlanner ?? BuildDropPlanner()).ChooseDropPoints(slot.Name, attackSide, _random);
            if (dropPoints.Count == 0)
            {
                Console.WriteLine($"[BB-ATTACK] phase=deploy status=skip troop={slot.Name} reason=no_drop_points_for_troop");
                return;
            }

            string displayName = slot.Name;
            if (slot.Name.Equals("BattleMachine", StringComparison.OrdinalIgnoreCase))
            {
                Point? machinePos = _heroController.GetMachinePos(out string machineName);
                displayName = string.IsNullOrWhiteSpace(machineName) ? slot.Name : machineName;
                Console.WriteLine($"[BB-ATTACK] phase=machine status=found name=\"{displayName}\" pos={(machinePos == null ? "unknown" : $"({machinePos.Value.X},{machinePos.Value.Y})")}");
            }

            _adb.Tap(slot.Center.X, slot.Center.Y);
            if (Sleep(_adb.FramePacer.AdjustDelay(Math.Clamp(options.SameTroopDelayMs, 50, 5000)), token)) return;

            int amount = Math.Clamp(slot.Count, 1, 12);
            Console.WriteLine($"[BB-ATTACK] phase=deploy status=slot troop={displayName} count={amount} slot={slot.Index} center=({slot.Center.X},{slot.Center.Y})");
            for (int i = 0; i < amount && !token.IsCancellationRequested; i++)
            {
                Point drop = dropPoints[i % dropPoints.Count];
                if (i == 0) drop = AvoidPotionArea(drop);
                _adb.Tap(drop.X, drop.Y);
                if (slot.Name.Contains("Bomber", StringComparison.OrdinalIgnoreCase) && options.HandleBomber)
                {
                    if (!_activeBomberSlots.Any(s => s.Index == slot.Index)) _activeBomberSlots.Add(slot);
                    Sleep(Math.Max(350, options.SameTroopDelayMs), token);
                    _heroController.TryActivateBomberAbility(slot);
                }

                if (slot.Name.Equals("BattleMachine", StringComparison.OrdinalIgnoreCase) || slot.Name.Equals("BattleCopter", StringComparison.OrdinalIgnoreCase))
                {
                    _heroController.ConfirmMachineDeployAndAbility(displayName, token);
                }

                if (Sleep(_adb.FramePacer.AdjustDelay(Math.Clamp(options.SameTroopDelayMs, 50, 5000)), token)) return;
            }

            Sleep(_adb.FramePacer.AdjustDelay(Math.Clamp(options.NextTroopDelayMs, 0, 10000)), token);
        }

        /// <summary>
        /// Vertical bound that keeps drop taps clear of the potion / spell area.
        /// The 500 constant belongs to the MBR 732-high coordinate space, so it must be
        /// scaled by height, never offset by a difference between two resolutions.
        /// </summary>
        public static int PotionCapY(int screenHeight) => (int)Math.Round(500 * (screenHeight / 732.0));

        public static Point AvoidPotionArea(Point point) => AvoidPotionArea(point, BuilderBaseAttackLayout.ScreenHeight);

        public static Point AvoidPotionArea(Point point, int screenHeight)
        {
            int potionCapY = PotionCapY(screenHeight);
            if (point.Y > potionCapY)
            {
                int x = point.X < 460 ? 460 : point.X;
                return new Point(x, potionCapY);
            }
            return point;
        }

        private BuilderBaseDropPlanner BuildDropPlanner()
        {
            using Mat? screenshot = _adb.TakeScreenshot();
            BuilderBaseDropPlanner planner = BuilderBaseDropPlanner.Build(screenshot, BuilderBaseAttackLayout.ScreenWidth, BuilderBaseAttackLayout.ScreenHeight);
            Console.WriteLine($"[BB-ATTACK] phase=red_area status={planner.Status} source={planner.Source} red_raw={planner.RawRedCount} red_clean={planner.CleanRedCount} tl={planner.SideCount(DropSide.TopLeft)} tr={planner.SideCount(DropSide.TopRight)} bl={planner.SideCount(DropSide.BottomLeft)} br={planner.SideCount(DropSide.BottomRight)} external={planner.ExternalArea}");
            SaveDropDebugOverlay(screenshot, planner);
            return planner;
        }

        private static void SaveDropDebugOverlay(Mat? screenshot, BuilderBaseDropPlanner planner)
        {
            if (!string.Equals(Environment.GetEnvironmentVariable("CVAUT_BB_DROP_DEBUG"), "1", StringComparison.OrdinalIgnoreCase)) return;
            try
            {
                using Mat canvas = screenshot == null || screenshot.Empty() ? new Mat(BuilderBaseAttackLayout.ScreenHeight, BuilderBaseAttackLayout.ScreenWidth, MatType.CV_8UC3, Scalar.Black) : screenshot.Clone();
                foreach (Point p in planner.RawRedPoints) Cv2.Circle(canvas, p, 1, Scalar.Red, -1);
                foreach (Point p in planner.AllCleanPoints) Cv2.Circle(canvas, p, 2, Scalar.Yellow, -1);
                foreach (Point p in planner.LastChosenDropPoints) Cv2.Circle(canvas, p, 4, Scalar.Lime, -1);
                Cv2.Polylines(canvas, new[] { planner.ExternalArea.Diamond }, true, Scalar.Cyan, 2);
                string dir = Path.Combine(AppContext.BaseDirectory, "logs");
                Directory.CreateDirectory(dir);
                string path = Path.Combine(dir, $"bb_drop_debug_{DateTime.Now:yyyyMMdd_HHmmss_fff}.png");
                Cv2.ImWrite(path, canvas);
                Console.WriteLine($"[BB-ATTACK] phase=drop_debug status=saved path=\"{path}\"");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[BB-ATTACK] phase=drop_debug status=fail reason={ex.Message}");
            }
        }

        private static bool Sleep(int milliseconds, CancellationToken token) => token.WaitHandle.WaitOne(milliseconds);
    }
}
