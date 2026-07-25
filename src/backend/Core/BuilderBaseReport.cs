using System;
using OpenCvSharp;

namespace CvAut
{
    internal sealed record BuilderBaseReportSnapshot(
        int Gold,
        int Elixir,
        int Trophy,
        int FreeBuilders,
        int TotalBuilders,
        int BuilderHallLevel,
        bool AttackAvailable,
        bool AttackAvailabilityKnown,
        bool StarBonusKnown,
        bool StarBonusAvailable,
        int RemainingStars,
        int MaxStars,
        bool GoldStorageFull,
        bool ElixirStorageFull,
        bool Reliable = true)
    {
        public bool LootAvailable => (StarBonusKnown && StarBonusAvailable) || AttackAvailable;

        public static BuilderBaseReportSnapshot UnknownSnapshot() =>
            new(0, 0, 0, 0, 0, 0, false, false, false, false, 0, 0, false, false, Reliable: false);
    }

    /// <summary>
    /// Đọc thông tin Builder Base không gây tác động game. Các ROI bám theo MBR,
    /// scale lên layout chuẩn 1600x900 của dự án.
    /// </summary>
    internal sealed class BuilderBaseReport
    {
        private readonly IADBHelper _adb;
        private readonly IVisionEngine _vision;
        private readonly BuilderBaseNavigator _navigator;

        private static readonly Rect TrophyRoi = new(92, 92, 135, 42);
        private static readonly Rect GoldRoi = new(1290, 24, 230, 42);
        private static readonly Rect ElixirRoi = new(1290, 86, 230, 42);
        private static readonly Rect BuilderCountRoi = new(700, 18, 100, 40);
        private static readonly Rect BuilderHallLevelRoi = new(610, 70, 150, 80);
        private static readonly Rect LootAvailabilityRoi = new(50, 610, 145, 90);
        private static readonly Rect TopStorageRoi = Rect.FromLTRB(980, 0, 1600, 170);

        private static readonly string[] FullGoldTemplates =
        {
            @"resources\full_gold_builder"
        };

        private static readonly string[] FullElixirTemplates =
        {
            @"resources\full_elixir_builder"
        };

        public BuilderBaseReport(IADBHelper adb, IVisionEngine vision, BuilderBaseNavigator navigator)
        {
            _adb = adb;
            _vision = vision;
            _navigator = navigator;
        }

        public BuilderBaseReportSnapshot Read()
        {
            Console.WriteLine("[BB-REPORT] phase=report status=start");
            if (!_navigator.IsOnBuilderBase())
            {
                Console.WriteLine("[BB-REPORT] phase=report status=skip reason=not_on_builder_base");
                return Unknown();
            }

            using Mat? screenshot = _adb.TakeScreenshot();
            if (screenshot == null || screenshot.Empty())
            {
                Console.WriteLine("[BB-REPORT] phase=report status=fail reason=screenshot_failed");
                return Unknown();
            }

            int trophy = ReadNumber(screenshot, TrophyRoi, "trophy", maxPlausible: 10000);
            int gold = ReadNumber(screenshot, GoldRoi, "gold", maxPlausible: 100_000_000);
            int elixir = ReadNumber(screenshot, ElixirRoi, "elixir", maxPlausible: 100_000_000);
            int builderRaw = ReadNumber(screenshot, BuilderCountRoi, "builders", maxPlausible: 99);
            (int freeBuilders, int totalBuilders) = ParseBuilderCount(builderRaw);
            int builderHallLevel = ReadNumber(screenshot, BuilderHallLevelRoi, "builder_hall_level", maxPlausible: 20);
            DetectLootAvailability(screenshot, out bool attackAvailable, out bool attackAvailabilityKnown, out bool starBonusKnown, out bool starBonusAvailable, out int remainingStars, out int maxStars);
            bool goldStorageFull = DetectStorageFull(screenshot, FullGoldTemplates, "gold");
            bool elixirStorageFull = DetectStorageFull(screenshot, FullElixirTemplates, "elixir");

            bool reliable = trophy > 0 || gold > 0 || elixir > 0 || totalBuilders > 0 || starBonusKnown || attackAvailable;
            var report = new BuilderBaseReportSnapshot(gold, elixir, trophy, freeBuilders, totalBuilders, builderHallLevel, attackAvailable, attackAvailabilityKnown, starBonusKnown, starBonusAvailable, remainingStars, maxStars, goldStorageFull, elixirStorageFull, Reliable: reliable);
            Console.WriteLine($"[BB-REPORT] phase=report status=success gold={report.Gold} elixir={report.Elixir} trophy={report.Trophy} free_builders={report.FreeBuilders} total_builders={report.TotalBuilders} builder_hall_level={report.BuilderHallLevel} attack_available={report.AttackAvailable} attack_known={report.AttackAvailabilityKnown} star_bonus_known={report.StarBonusKnown} star_bonus_avail={report.StarBonusAvailable} remaining_stars={report.RemainingStars} max_stars={report.MaxStars} gold_storage_full={report.GoldStorageFull} elixir_storage_full={report.ElixirStorageFull} report_reliable={report.Reliable}");
            return report;
        }

        private int ReadNumber(Mat screenshot, Rect roi, string label, int maxPlausible)
        {
            Rect safe = ImageUtils.ClampRect(roi, screenshot.Width, screenshot.Height);
            if (safe.Width <= 0 || safe.Height <= 0) return 0;

            if (!_vision.TryExtractNumericalMetrics(screenshot, safe, out int value, out double confidence, useRgbThresh: true)
                && !_vision.TryExtractNumericalMetrics(screenshot, safe, out value, out confidence))
            {
                Console.WriteLine($"[BB-REPORT] phase=ocr status=skip item={label} reason=unreadable");
                return 0;
            }

            if (value < 0 || value > maxPlausible)
            {
                Console.WriteLine($"[BB-REPORT] phase=ocr status=skip item={label} value={value} confidence={confidence:F2} reason=implausible");
                return 0;
            }

            Console.WriteLine($"[BB-REPORT] phase=ocr status=success item={label} value={value} confidence={confidence:F2}");
            return value;
        }

        private static (int Free, int Total) ParseBuilderCount(int raw)
        {
            if (raw <= 0) return (0, 0);
            if (raw < 10) return (raw, 0);

            int free = raw / 10;
            int total = raw % 10;
            if (free > total && total > 0) return (0, 0);
            return (free, total);
        }

        private void DetectLootAvailability(
            Mat screenshot,
            out bool attackAvailable,
            out bool attackAvailabilityKnown,
            out bool starBonusKnown,
            out bool starBonusAvailable,
            out int remainingStars,
            out int maxStars)
        {
            remainingStars = 0;
            maxStars = 0;

            Rect starsRoi = ImageUtils.ClampRect(new Rect(40, 568, 92, 24), screenshot.Width, screenshot.Height);
            if (starsRoi.Width > 0 && starsRoi.Height > 0
                && (_vision.TryExtractNumericalMetrics(screenshot, starsRoi, out int starsRaw, out double confidence, useRgbThresh: true)
                    || _vision.TryExtractNumericalMetrics(screenshot, starsRoi, out starsRaw, out confidence)))
            {
                // In this dedicated ROI, OCR commonly drops the slash and leading zero:
                // "0/12" becomes 12, "0/10" becomes 10, and "0/6" becomes 6.
                (remainingStars, maxStars) = ParseStarPair(starsRaw, allowCompletedShorthand: true);
                Console.WriteLine($"[BB-REPORT] phase=loot status=stars raw={starsRaw} remaining={remainingStars} max={maxStars} confidence={confidence:F2}");
            }

            starBonusKnown = maxStars > 0;
            starBonusAvailable = starBonusKnown && remainingStars > 0;

            bool byStars = starBonusAvailable;
            bool byButton = _vision.FindElement(screenshot, @"ui\attack_button", 0.55, LootAvailabilityRoi, out double score) != null
                || _vision.FindElement(screenshot, @"ui\icon_attack", 0.55, LootAvailabilityRoi, out score) != null
                || _vision.FindElement(screenshot, @"ui\battle", 0.55, LootAvailabilityRoi, out score) != null;

            attackAvailable = byButton;
            attackAvailabilityKnown = IsAttackAvailabilityKnown(byButton, starBonusKnown);
            bool available = byStars || byButton;
            Console.WriteLine($"[BB-REPORT] phase=loot status={(available ? "success" : "skip")} available={available} by_stars={byStars} by_button={byButton} attack_known={attackAvailabilityKnown} star_bonus_known={starBonusKnown} star_bonus_avail={starBonusAvailable}");
        }

        internal static bool IsAttackAvailabilityKnown(bool attackButtonDetected, bool starBonusKnown)
        {
            // Trophy OCR only proves that the home header is readable. It does not prove
            // that a missing Attack button is a reliable negative detection.
            return attackButtonDetected || starBonusKnown;
        }

        private static readonly int[] ValidStarMaximums = { 12, 10, 6 };

        internal static (int Remaining, int Max) ParseStarPair(int raw, bool allowCompletedShorthand = false)
        {
            if (raw <= 0) return (0, 0);

            if (allowCompletedShorthand && Array.IndexOf(ValidStarMaximums, raw) >= 0)
            {
                return (0, raw);
            }

            string digits = raw.ToString();

            foreach (int max in ValidStarMaximums)
            {
                string maxText = max.ToString();
                if (!digits.EndsWith(maxText, StringComparison.Ordinal)) continue;

                string remainingText = digits[..^maxText.Length];
                if (string.IsNullOrEmpty(remainingText)) continue;

                if (int.TryParse(remainingText, out int remaining)
                    && remaining >= 0
                    && remaining <= max)
                {
                    return (remaining, max);
                }
            }

            return (0, 0);
        }

        private bool DetectStorageFull(Mat screenshot, string[] templates, string resource)
        {
            foreach (string template in templates)
            {
                Point? center = _vision.FindElement(screenshot, template, 0.72, TopStorageRoi, out double score);
                if (center == null) continue;

                Console.WriteLine($"[BB-REPORT] phase=storage_full status=success resource={resource} template=\"{template}\" score={score:F2} center=({center.Value.X},{center.Value.Y})");
                return true;
            }

            Console.WriteLine($"[BB-REPORT] phase=storage_full status=skip resource={resource}");
            return false;
        }

        private static BuilderBaseReportSnapshot Unknown()
        {
            return new BuilderBaseReportSnapshot(0, 0, 0, 0, 0, 0, false, false, false, false, 0, 0, false, false, Reliable: false);
        }
    }
}
