using System;
using System.Collections.Generic;
using System.Linq;
using OpenCvSharp;
using Point = OpenCvSharp.Point;

namespace CvAut
{
    internal partial class BuilderBaseAttacks
    {
        private List<BuilderBaseTroopSlot> ReadAttackBarSlots(bool remaining, bool secondAttack = false)
        {
            using Mat? screenshot = _adb.TakeScreenshot();
            if (screenshot == null || screenshot.Empty()) return new();

            var slots = ReadAttackBarSlotsByMbrGrid(screenshot, remaining, secondAttack);
            foreach (string template in BuilderTroopTemplates)
            {
                Point? center = _vision.FindElement(screenshot, template, TroopThreshold, DeployBarRoi, out double score);
                if (center == null || IsNearExisting(slots.Select(s => s.Center), center.Value)) continue;
                if (remaining && IsSlotAlreadyDeployedByBanner(screenshot, center.Value, secondAttack))
                {
                    Console.WriteLine($"[BB-ATTACK] phase=attack_bar status=skip reason=deployed_banner_direct remaining={remaining} troop_template=\"{template}\" center=({center.Value.X},{center.Value.Y})");
                    continue;
                }

                string name = TroopNamesByTemplate.TryGetValue(template, out string? mapped) ? mapped : template;
                int count = ReadSlotCount(screenshot, center.Value);
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

            Point? machine = GetMachinePos(screenshot, out string machineName);
            int machineSlotsFound = 0;
            if (machine != null)
            {
                string heroName = machineName.Contains("Copter", StringComparison.OrdinalIgnoreCase) ? "BattleCopter" : "BattleMachine";
                if (!remaining && !IsMachineDeadByMbrPixel(screenshot))
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
                    int slotX = (int)Math.Round(startSlot + k * mbrSlotOffset * ScaleX(screenshot));
                    int bannerX = slotX + (int)Math.Round(34 * ScaleX(screenshot));
                    int bannerY = ScaleY(screenshot, 585);
                    int selectY = ScaleY(screenshot, 610);

                    Rect slotRoi = Rect.FromLTRB(slotX, bannerY - ScaleYDistance(screenshot, 35), slotX + (int)Math.Round(70 * ScaleX(screenshot)), ScaleY(screenshot, 670));
                    slotRoi = ImageUtils.ClampRect(slotRoi, screenshot.Width, screenshot.Height);
                    if (slotRoi.Width <= 0 || slotRoi.Height <= 0) continue;

                    string? template = FindSlotTemplateInRoi(screenshot, slotRoi, out Point? found, out double score, remaining);
                    if (template == null || found == null) continue;
                    if (!TryReadMbrBannerState(screenshot, bannerX, bannerY, remaining, secondAttack, out bool readable, out int bannerCount, out string state) || !readable)
                    {
                        Console.WriteLine($"[BB-ATTACK] phase=attack_bar status=skip reason=banner_state remaining={remaining} slot={k + machineSlotsFound} state={state} x={bannerX} y={bannerY}");
                        continue;
                    }

                    if (result.Any(s => IsNearExisting(new[] { s.Center }, found.Value))) continue;
                    string name = TroopNamesByTemplate.TryGetValue(template, out string? mapped) ? mapped : template;
                    int count = bannerCount > 0 ? bannerCount : ReadSlotCountAtBanner(screenshot, bannerX, bannerY);
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
            const double slotOffset = 75.5;

            var refined = new List<BuilderBaseTroopSlot>(slots);
            foreach (int startSlot in EstimateAttackBarStartSlots(screenshot, remaining, secondAttack))
            {
                for (int i = 0; i < slotCount; i++)
                {
                    int slotX = (int)Math.Round(startSlot + (i * slotOffset));
                    Rect roi = Rect.FromLTRB(slotX, 550, slotX + 72, 670 + ScreenHeight - 900);
                    roi = ImageUtils.ClampRect(roi, screenshot.Width, screenshot.Height);
                    if (roi.Width <= 0 || roi.Height <= 0) continue;

                    string? template = FindSlotTemplateInRoi(screenshot, roi, out Point? center, out double score, remaining);
                    if (center == null || string.IsNullOrWhiteSpace(template)) continue;

                    if (remaining && IsSlotAlreadyDeployedByBanner(screenshot, center.Value, secondAttack))
                    {
                        Console.WriteLine($"[BB-ATTACK] phase=attack_bar status=skip reason=deployed_banner start_slot={startSlot} troop_template=\"{template}\" center=({center.Value.X},{center.Value.Y})");
                        continue;
                    }

                    if (refined.Any(s => IsNearExisting(new[] { s.Center }, center.Value))) continue;

                    string name = TroopNamesByTemplate.TryGetValue(template, out string? mapped) ? mapped : template;
                    int count = ReadSlotCount(screenshot, center.Value);
                    refined.Add(new BuilderBaseTroopSlot(name, center.Value, refined.Count, count, score));
                    Console.WriteLine($"[BB-ATTACK] phase=attack_bar status=slot_refined remaining={remaining} start_slot={startSlot} troop={name} count={count} score={score:F2} center=({center.Value.X},{center.Value.Y})");
                }
            }

            return refined;
        }

        private bool IsSlotAlreadyDeployedByBanner(Mat screenshot, Point center, bool secondAttack)
        {
            int bannerX = center.X + (int)Math.Round(34 * ScaleX(screenshot));
            int bannerY = ScaleY(screenshot, 585);
            if (!TryGetPixel(screenshot, bannerX, bannerY, out Vec3b topPixel)) return false;

            if (IsColorNear(topPixel, 0x7B7B7B, 12)) return true;
            if (IsColorNear(topPixel, 0xCA49FF, 30)) return true;
            if (IsColorNear(topPixel, 0x12244B, 30)) return true;

            if (secondAttack)
            {
                if (IsColorNear(topPixel, 0xD77AFF, 30)) return true;
                if (IsColorNear(topPixel, 0x15274A, 30)) return true;
            }

            if (!TryGetPixel(screenshot, bannerX, bannerY - 15, out Vec3b deployedPixel)) return false;
            return IsColorNear(deployedPixel, 0xCA49FF, 30)
                || IsColorNear(deployedPixel, 0x232323, 10)
                || IsColorNear(deployedPixel, 0x4482FE, 30)
                || IsColorNear(deployedPixel, 0x3E7BFF, 30);
        }

        private bool TryReadMbrBannerState(Mat screenshot, int bannerX, int bannerY, bool remaining, bool secondAttack, out bool readable, out int count, out string state)
        {
            readable = false;
            count = 0;
            state = "missing_pixel";
            if (!TryGetPixel(screenshot, bannerX, bannerY, out Vec3b pixel)) return false;
            TryGetPixel(screenshot, bannerX, bannerY - ScaleYDistance(screenshot, 15), out Vec3b deployedPixel);

            bool grey = IsColorNear(pixel, 0x7B7B7B, 10);
            bool violetDeployed = IsColorNear(deployedPixel, 0xCA49FF, 30);
            bool darkDeployed = IsColorNear(deployedPixel, 0x232323, 10);
            if (remaining && (grey || violetDeployed || darkDeployed))
            {
                if (grey && ReadSlotCountAtBanner(screenshot, bannerX, bannerY) > 0)
                {
                    state = "grey_with_count";
                }
                else
                {
                    state = grey ? "grey_deployed" : violetDeployed ? "violet_deployed" : "dark_deployed";
                    return true;
                }
            }

            bool violet = IsColorNear(pixel, 0xCA4AFF, 30) || IsColorNear(pixel, 0xD77AFF, 30);
            bool giantViolet = IsColorNear(pixel, 0x12244B, 30) || IsColorNear(pixel, 0x15274A, 30);
            bool blue = IsColorNear(pixel, remaining ? 0x4482FE : 0x3E7BFF, 30) || IsColorNear(pixel, 0x4482FE, 30);
            if (!violet && secondAttack) violet = giantViolet;

            if (blue)
            {
                count = ReadSlotCountAtBanner(screenshot, bannerX, bannerY);
                readable = count > 0;
                state = readable ? "blue_count" : "blue_ocr_empty";
                return true;
            }

            if (violet || giantViolet)
            {
                count = 1;
                readable = true;
                state = violet ? "violet_one" : "giant_violet_one";
                return true;
            }

            state = $"unknown_color_{pixel.Item2:X2}{pixel.Item1:X2}{pixel.Item0:X2}";
            return true;
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

            yield return 100;
            yield return rememberedScaledStart;
            yield return rememberedMbrStart;
            yield return scaledStart;
            yield return mbrStart;
        }

        private int CountTemplatesInRoi(Mat screenshot, Rect roi)
        {
            Rect safe = ImageUtils.ClampRect(roi, screenshot.Width, screenshot.Height);
            if (safe.Width <= 0 || safe.Height <= 0) return 0;
            int count = 0;
            foreach (string template in BuilderTroopTemplates)
            {
                if (_vision.FindElement(screenshot, template, 0.46, safe, out _) != null) count++;
            }

            return count;
        }

        private string? FindSlotTemplateInRoi(Mat screenshot, Rect roi, out Point? center, out double score, bool remaining)
        {
            center = null;
            score = 0;
            foreach (string template in BuilderTroopTemplates)
            {
                Point? found = _vision.FindElement(screenshot, template, remaining ? 0.46 : TroopThreshold, roi, out double s);
                if (found == null) continue;
                center = found;
                score = s;
                return template;
            }

            return null;
        }

        private int ReadSlotCount(Mat screenshot, Point center)
        {
            Rect roi = Rect.FromLTRB(Math.Max(0, center.X + 8), Math.Max(0, center.Y - 45), Math.Min(ScreenWidth, center.X + 48), Math.Min(ScreenHeight, center.Y - 8));
            if (_vision.TryExtractNumericalMetrics(screenshot, roi, out int value, out double confidence, useRgbThresh: true)
                && value > 0 && value <= 20)
            {
                return value;
            }

            if (_vision.TryExtractNumericalMetrics(screenshot, roi, out value, out confidence)
                && value > 0 && value <= 20)
            {
                return value;
            }

            return 1;
        }

        private int ReadSlotCountAtBanner(Mat screenshot, int bannerX, int bannerY)
        {
            Rect roi = Rect.FromLTRB(bannerX, bannerY - ScaleYDistance(screenshot, 14), bannerX + (int)Math.Round(31 * ScaleX(screenshot)), bannerY + ScaleYDistance(screenshot, 8));
            roi = ImageUtils.ClampRect(roi, screenshot.Width, screenshot.Height);
            if (roi.Width <= 0 || roi.Height <= 0) return 0;
            if (_vision.TryExtractNumericalMetrics(screenshot, roi, out int value, out _, useRgbThresh: true) && value > 0 && value <= 20) return value;
            if (_vision.TryExtractNumericalMetrics(screenshot, roi, out value, out _) && value > 0 && value <= 20) return value;
            return 0;
        }

        private static double ScaleX(Mat screenshot) => screenshot.Width / 860.0;
        private static int ScaleY(Mat screenshot, int mbrY) => (int)Math.Round(mbrY * (screenshot.Height / 732.0));
        private static int ScaleYDistance(Mat screenshot, int pixels) => Math.Max(1, (int)Math.Round(pixels * (screenshot.Height / 732.0)));
    }
}
