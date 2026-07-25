using System;
using System.Collections.Generic;
using System.Linq;
using OpenCvSharp;
using Point = OpenCvSharp.Point;

namespace CvAut
{
    internal sealed class AttackBarScanner
    {
        private readonly IADBHelper _adb;
        private readonly IVisionEngine _vision;
        private readonly HeroAbilityController _heroController;

        private int _startSlotMem = 21;
        private int _startSlotMem2 = 21;

        public AttackBarScanner(IADBHelper adb, IVisionEngine vision, HeroAbilityController heroController)
        {
            _adb = adb;
            _vision = vision;
            _heroController = heroController;
        }

        public List<BuilderBaseTroopSlot> ReadAttackBarSlots(bool remaining, bool secondAttack = false)
        {
            using Mat? screenshot = _adb.TakeScreenshot();
            if (screenshot == null || screenshot.Empty()) return new();

            var slots = ReadAttackBarSlotsByMbrGrid(screenshot, remaining, secondAttack);
            foreach (string template in BuilderBaseAttackLayout.BuilderTroopTemplates)
            {
                Point? center = _vision.FindElement(screenshot, template, BuilderBaseAttackLayout.TroopThreshold, BuilderBaseAttackLayout.DeployBarRoi, out double score);
                if (center == null || IsNearExisting(slots.Select(s => s.Center), center.Value)) continue;
                if (remaining && AttackBarBannerReader.IsSlotAlreadyDeployedByBanner(screenshot, center.Value, secondAttack))
                {
                    Console.WriteLine($"[BB-ATTACK] phase=attack_bar status=skip reason=deployed_banner_direct remaining={remaining} troop_template=\"{template}\" center=({center.Value.X},{center.Value.Y})");
                    continue;
                }

                string name = BuilderBaseAttackLayout.TroopNamesByTemplate.TryGetValue(template, out string? mapped) ? mapped : template;
                int count = AttackBarBannerReader.ReadSlotCount(screenshot, center.Value, _vision);
                slots.Add(new BuilderBaseTroopSlot(name, center.Value, slots.Count, count, score));
                Console.WriteLine($"[BB-ATTACK] phase=attack_bar status=slot remaining={remaining} troop={name} count={count} score={score:F2} center=({center.Value.X},{center.Value.Y})");
            }

            slots = RefineAttackBarSlotsWithBannerScan(screenshot, slots, remaining, secondAttack);

            slots.Sort((a, b) => a.Center.X.CompareTo(b.Center.X));
            for (int i = 0; i < slots.Count; i++) slots[i] = slots[i] with { Index = i };
            return slots;
        }

        private List<BuilderBaseTroopSlot> ReadAttackBarSlotsByMbrGrid(Mat screenshot, bool remaining, bool secondAttack)
        {
            const int slotCount = 9;
            const double mbrSlotOffset = 75.5;
            var result = new List<BuilderBaseTroopSlot>();

            Point? machine = _heroController.GetMachinePos(screenshot, out string machineName);
            int machineSlotsFound = 0;
            if (machine != null)
            {
                string heroName = machineName.Contains("Copter", StringComparison.OrdinalIgnoreCase) ? "BattleCopter" : "BattleMachine";
                if (!remaining && !_heroController.IsMachineDeadByMbrPixel(screenshot))
                {
                    result.Add(new BuilderBaseTroopSlot(heroName, machine.Value, 0, 1, 1.0));
                    Console.WriteLine($"[BB-ATTACK] phase=attack_bar status=machine_slot remaining={remaining} second_attack={secondAttack} troop={heroName} center=({machine.Value.X},{machine.Value.Y})");
                }
                machineSlotsFound = 1;
            }

            foreach (int startSlot in EstimateAttackBarStartSlots(screenshot, remaining, secondAttack).Distinct())
            {
                for (int k = 0; k < slotCount; k++)
                {
                    int slotX = (int)Math.Round(startSlot + k * mbrSlotOffset * MbrScreenScaling.ScaleX(screenshot));
                    int bannerX = slotX + (int)Math.Round(34 * MbrScreenScaling.ScaleX(screenshot));
                    int bannerY = MbrScreenScaling.ScaleY(screenshot, 585);
                    int selectY = MbrScreenScaling.ScaleY(screenshot, 610);

                    Rect slotRoi = Rect.FromLTRB(slotX, bannerY - MbrScreenScaling.ScaleYDistance(screenshot, 35), slotX + (int)Math.Round(70 * MbrScreenScaling.ScaleX(screenshot)), MbrScreenScaling.ScaleY(screenshot, 670));
                    slotRoi = ImageUtils.ClampRect(slotRoi, screenshot.Width, screenshot.Height);
                    if (slotRoi.Width <= 0 || slotRoi.Height <= 0) continue;

                    string? template = FindSlotTemplateInRoi(screenshot, slotRoi, out Point? found, out double score, remaining);
                    if (template == null || found == null) continue;
                    if (!AttackBarBannerReader.TryReadMbrBannerState(screenshot, bannerX, bannerY, remaining, secondAttack, _vision, out bool readable, out int bannerCount, out string state) || !readable)
                    {
                        Console.WriteLine($"[BB-ATTACK] phase=attack_bar status=skip reason=banner_state remaining={remaining} slot={k + machineSlotsFound} state={state} x={bannerX} y={bannerY}");
                        continue;
                    }

                    if (result.Any(s => IsNearExisting(new[] { s.Center }, found.Value))) continue;
                    string name = BuilderBaseAttackLayout.TroopNamesByTemplate.TryGetValue(template, out string? mapped) ? mapped : template;
                    int count = bannerCount > 0 ? bannerCount : AttackBarBannerReader.ReadSlotCountAtBanner(screenshot, bannerX, bannerY, _vision);
                    Point center = new(slotX + slotRoi.Width / 2, selectY);
                    result.Add(new BuilderBaseTroopSlot(name, center, k + machineSlotsFound, Math.Clamp(count, 1, 20), score));
                    Console.WriteLine($"[BB-ATTACK] phase=attack_bar status=mbr_slot remaining={remaining} second_attack={secondAttack} slot={k + machineSlotsFound} troop={name} count={count} state={state} score={score:F2} center=({center.X},{center.Y})");
                }
            }

            return result;
        }

        private List<BuilderBaseTroopSlot> RefineAttackBarSlotsWithBannerScan(Mat screenshot, List<BuilderBaseTroopSlot> slots, bool remaining, bool secondAttack)
        {
            const int slotCount = 9;
            const double mbrSlotOffset = 75.5;

            var refined = new List<BuilderBaseTroopSlot>(slots);
            foreach (int startSlot in EstimateAttackBarStartSlots(screenshot, remaining, secondAttack))
            {
                for (int i = 0; i < slotCount; i++)
                {
                    int slotX = (int)Math.Round(startSlot + i * mbrSlotOffset * MbrScreenScaling.ScaleX(screenshot));
                    Rect roi = Rect.FromLTRB(
                        slotX,
                        MbrScreenScaling.ScaleY(screenshot, 550),
                        slotX + (int)Math.Round(72 * MbrScreenScaling.ScaleX(screenshot)),
                        MbrScreenScaling.ScaleY(screenshot, 670));
                    roi = ImageUtils.ClampRect(roi, screenshot.Width, screenshot.Height);
                    if (roi.Width <= 0 || roi.Height <= 0) continue;

                    string? template = FindSlotTemplateInRoi(screenshot, roi, out Point? center, out double score, remaining);
                    if (center == null || string.IsNullOrWhiteSpace(template)) continue;

                    if (remaining && AttackBarBannerReader.IsSlotAlreadyDeployedByBanner(screenshot, center.Value, secondAttack))
                    {
                        Console.WriteLine($"[BB-ATTACK] phase=attack_bar status=skip reason=deployed_banner start_slot={startSlot} troop_template=\"{template}\" center=({center.Value.X},{center.Value.Y})");
                        continue;
                    }

                    if (refined.Any(s => IsNearExisting(new[] { s.Center }, center.Value))) continue;

                    string name = BuilderBaseAttackLayout.TroopNamesByTemplate.TryGetValue(template, out string? mapped) ? mapped : template;
                    int count = AttackBarBannerReader.ReadSlotCount(screenshot, center.Value, _vision);
                    refined.Add(new BuilderBaseTroopSlot(name, center.Value, refined.Count, count, score));
                    Console.WriteLine($"[BB-ATTACK] phase=attack_bar status=slot_refined remaining={remaining} start_slot={startSlot} troop={name} count={count} score={score:F2} center=({center.Value.X},{center.Value.Y})");
                }
            }

            return refined;
        }

        private IEnumerable<int> EstimateAttackBarStartSlots(Mat screenshot, bool remaining, bool secondAttack)
        {
            int detectedLeft = CountTemplatesInRoi(screenshot, Rect.FromLTRB(45, 220, 608, 310));
            int detectedRight = CountTemplatesInRoi(screenshot, Rect.FromLTRB(608, 220, 815, 310));
            int mbrStart = detectedLeft + detectedRight > 0 && detectedLeft + detectedRight < 5 ? 27 : 21;
            if (!remaining)
            {
                if (secondAttack)
                {
                    _startSlotMem2 = mbrStart;
                }
                else
                {
                    _startSlotMem = mbrStart;
                    _startSlotMem2 = detectedRight > 0 ? mbrStart : _startSlotMem;
                }
            }

            int rememberedMbrStart = secondAttack ? _startSlotMem2 : _startSlotMem;
            int scaledStart = (int)Math.Round(mbrStart * (screenshot.Width / 860.0));
            int rememberedScaledStart = (int)Math.Round(rememberedMbrStart * (screenshot.Width / 860.0));
            Console.WriteLine($"[BB-ATTACK] phase=attack_bar status=slot_memory remaining={remaining} second_attack={secondAttack} detected_left={detectedLeft} detected_right={detectedRight} mbr_start={mbrStart} remembered={rememberedMbrStart} scaled_start={scaledStart} remembered_scaled={rememberedScaledStart}");

            yield return rememberedScaledStart;
            if (scaledStart != rememberedScaledStart) yield return scaledStart;
        }

        private int CountTemplatesInRoi(Mat screenshot, Rect roi)
        {
            Rect safe = ImageUtils.ClampRect(roi, screenshot.Width, screenshot.Height);
            if (safe.Width <= 0 || safe.Height <= 0) return 0;
            int count = 0;
            foreach (string template in BuilderBaseAttackLayout.BuilderTroopTemplates)
            {
                if (_vision.FindElement(screenshot, template, 0.46, safe, out _) != null) count++;
            }

            return count;
        }

        private string? FindSlotTemplateInRoi(Mat screenshot, Rect roi, out Point? center, out double score, bool remaining)
        {
            center = null;
            score = 0;
            foreach (string template in BuilderBaseAttackLayout.BuilderTroopTemplates)
            {
                Point? found = _vision.FindElement(screenshot, template, remaining ? 0.46 : BuilderBaseAttackLayout.TroopThreshold, roi, out double s);
                if (found == null) continue;
                center = found;
                score = s;
                return template;
            }

            return null;
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
    }
}
