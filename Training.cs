using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using OpenCvSharp;
using Point = OpenCvSharp.Point;

namespace CvAut
{
    public class Training
    {
        private readonly ADBHelper _adb;
        private readonly VisionEngine _vision;
        private readonly string _templateRoot;

        private static readonly Point OpenArmyWindow = new(62, 658);
        private static readonly Point CloseArmyWindow = new(1545, 81);
        private static readonly Point ArmyRecipePane = new(777, 90);
        private static readonly Point ConfirmRecipeUse = new(972, 584);

        private static readonly Rect ArmyWindowRoi = new(76, 57, 489, 99);
        private static readonly Rect QuickSlot1Roi = Rect.FromLTRB(1364, 189, 1574, 425);
        private static readonly Rect QuickSlot2Roi = Rect.FromLTRB(1368, 486, 1572, 735);

        private static readonly Rect ArmyRoi = Rect.FromLTRB(682, 228, 1573, 383);
        private static readonly Rect SpellRoi = Rect.FromLTRB(689, 461, 1250, 600);
        private static readonly Rect SiegeRoi = Rect.FromLTRB(1256, 457, 1554, 608);
        private static readonly Rect ArmySpaceSecondaryRoi = Rect.FromLTRB(751, 183, 858, 230);
        private static readonly Rect SpellSpaceRoi = Rect.FromLTRB(731, 398, 810, 464);

        private static readonly Rect TrashArmyRoi = Rect.FromLTRB(1519, 184, 1570, 231);
        private static readonly Rect TrashSpellRoi = Rect.FromLTRB(1197, 408, 1250, 455);
        private static readonly Rect TrashSiegeRoi = Rect.FromLTRB(1511, 406, 1577, 458);

        private const double ValidationIconThreshold = 0.84;

        private static readonly Point TapClearArmy = new(1546, 209);
        private static readonly Point TapClearSpell = new(1225, 429);
        private static readonly Point TapClearSiege = new(1545, 427);
        private static readonly Point ConfirmTapArmy = new(969, 579);
        private static readonly Point ConfirmTapSpell = new(978, 583);
        private static readonly Point ConfirmTapSiege = new(966, 581);

        private static readonly Point OpenArmyTab = new(1063, 305);
        private static readonly Point CloseArmyTab = new(47, 85);
        private static readonly Point OpenSpellTab = new(1008, 531);
        private static readonly Point CloseSpellTab = new(59, 52);
        private static readonly Point OpenSiegeTab = new(1398, 533);
        private static readonly Point CloseSiegeTab = new(27, 85);

        private static readonly Dictionary<string, int> SpaceCost = new(StringComparer.OrdinalIgnoreCase)
        {
            ["dragon"] = 20,
            ["electro_dragon"] = 30,
            ["balloon"] = 5
        };

        private static readonly Dictionary<string, ArmySpec> ArmySets = new(StringComparer.OrdinalIgnoreCase)
        {
            ["Dragon_Attack"] = new("dragon", new[] { "dragon", "balloon" }, new[] { "rage", "freeze" }, "slammer"),
            ["Dragon attack"] = new("dragon", new[] { "dragon", "balloon" }, new[] { "rage", "freeze" }, "slammer"),
            ["ElectroDragon_Attack"] = new("electro_dragon", new[] { "electro_dragon", "balloon" }, new[] { "rage", "freeze" }, "slammer"),
            ["Electro Dragon attack"] = new("electro_dragon", new[] { "electro_dragon", "balloon" }, new[] { "rage", "freeze" }, "slammer")
        };

        public Training(ADBHelper adb, string templatesPath, VisionEngine vision)
        {
            _adb = adb;
            _vision = vision;
            _templateRoot = Path.Combine(templatesPath, "Smart_Auto_train");
        }

        public bool QuickTrain(int quickSlot = 1)
        {
            Console.WriteLine($"[quick_train] Bắt đầu dùng Army Recipe slot {quickSlot}...");

            if (!ValidateArmyWindow())
            {
                Console.WriteLine("[quick_train] Army window not detected - aborting");
                return false;
            }

            _adb.Tap(ArmyRecipePane.X, ArmyRecipePane.Y);
            Thread.Sleep(500);

            using Mat? shot = _adb.TakeScreenshot();
            if (shot == null || shot.Empty())
            {
                Console.WriteLine("[quick_train] failed to capture screenshot after opening recipes");
                CloseArmyWindowIfPossible();
                return false;
            }

            Rect slotRoi = quickSlot == 1 ? QuickSlot1Roi : QuickSlot2Roi;
            if (!TryFindTemplate(shot, "use_button.png", slotRoi, out Point useButton, out double useScore))
            {
                Console.WriteLine("[quick_train] use_button template missing or invalid");
                CloseArmyWindowIfPossible();
                return false;
            }

            Console.WriteLine($"[quick_train] use_button match score = {useScore:F3}");
            if (useScore >= 0.90)
            {
                _adb.Tap(useButton.X, useButton.Y);
                Thread.Sleep(600);

                using Mat? confirmShot = _adb.TakeScreenshot();
                if (confirmShot != null && !confirmShot.Empty()
                    && TryFindTemplate(confirmShot, "use_army_recipe_window.png", null, out _, out double confirmScore))
                {
                    Console.WriteLine($"[quick_train] recipe window match score = {confirmScore:F3}");
                    if (confirmScore >= 0.90)
                    {
                        _adb.Tap(ConfirmRecipeUse.X, ConfirmRecipeUse.Y);
                    }
                }
            }

            Thread.Sleep(500);
            CloseArmyWindowIfPossible();
            Thread.Sleep(500);
            return true;
        }

        public void SmartTrain(JsonElement cfg)
        {
            Console.WriteLine("\n--- [SMART] Starting Smart Train Sequence ---");

            if (!ValidateArmyWindow())
            {
                Console.WriteLine("[SMART] Army window not detected - skipping Army training");
                return;
            }

            Console.WriteLine("Army window detected");

            bool armyOk = ValidateTroops(cfg);
            bool spellOk = ValidateSpells();
            bool siegeOk = ValidateSiege();

            if (armyOk && spellOk && siegeOk)
            {
                Console.WriteLine("[SMART] All valid - no training needed");
                CloseArmyWindowIfPossible();
                Thread.Sleep(1000);
                return;
            }

            if (!armyOk)
            {
                TrainTroops(cfg);
            }

            if (!spellOk)
            {
                TrainSpells();
            }

            if (!siegeOk)
            {
                TrainSlammer();
            }

            Console.WriteLine("[SMART] Training complete - closing Army tab");
            CloseArmyWindowIfPossible();
            Thread.Sleep(1000);
        }

        private bool ValidateArmyWindow()
        {
            _adb.Tap(OpenArmyWindow.X, OpenArmyWindow.Y);
            Thread.Sleep(1000);

            using Mat? shot = _adb.TakeScreenshot();
            if (shot == null || shot.Empty())
            {
                Console.WriteLine("[WINDOW CHECK] failed to capture screenshot");
                return false;
            }

            if (!TryFindTemplate(shot, "army_window.png", ArmyWindowRoi, out _, out double score))
            {
                Console.WriteLine("[WINDOW CHECK] missing template or invalid army ROI");
                return false;
            }

            Console.WriteLine($"[WINDOW CHECK] army window match score = {score:F3}");
            return score >= 0.60;
        }

        private void CloseArmyWindowIfPossible()
        {
            _adb.Tap(CloseArmyWindow.X, CloseArmyWindow.Y);
        }

        private bool ValidateTroops(JsonElement cfg)
        {
            ArmySpec spec = GetArmySpec(cfg);

            using Mat? shot = _adb.TakeScreenshot();
            if (shot == null || shot.Empty())
            {
                return false;
            }

            using Mat roi = Crop(shot, ArmyRoi);
            bool mainOk = TryMatch("Army Troops", spec.Main, roi, ValidationIconThreshold, out Point mainCenter, out _);
            if (!mainOk)
            {
                mainOk = TryMatch("s_troops", $"s_{spec.Main}", roi, ValidationIconThreshold, out mainCenter, out _);
            }

            bool balloonOk = TryMatch("Army Troops", "balloon", roi, ValidationIconThreshold, out Point balloonCenter, out _);
            if (!mainOk || !balloonOk)
            {
                Console.WriteLine("[VALIDATION] will train fresh load");
                return false;
            }

            Console.WriteLine("[VALIDATION] composition ok");

            int? armySpace = MeasureArmySpaceSecondary(shot);
            if (armySpace == null)
            {
                Console.WriteLine("[SPACE CHECK] Secondary not confident; icon validation passed.");
                return true;
            }

            Console.WriteLine($"[SPACE CHECK] Available space = {armySpace.Value}");

            var expected = GetExpectedTroopCounts(spec, armySpace.Value);
            bool mainCountOk = ValidateIconCount(shot, ArmyRoi, spec.Main, mainCenter, expected.MainCount);
            bool balloonCountOk = ValidateIconCount(shot, ArmyRoi, "balloon", balloonCenter, expected.BalloonCount);

            if (!mainCountOk || !balloonCountOk)
            {
                return false;
            }

            return armySpace.Value >= 120;
        }

        private bool ValidateSpells()
        {
            using Mat? shot = _adb.TakeScreenshot();
            if (shot == null || shot.Empty())
            {
                return false;
            }

            using Mat roi = Crop(shot, SpellRoi);
            bool rageOk = TryMatch("Spells", "rage", roi, ValidationIconThreshold, out Point rageCenter, out _);
            bool freezeOk = TryMatch("Spells", "freeze", roi, ValidationIconThreshold, out Point freezeCenter, out _);
            if (!rageOk || !freezeOk)
            {
                Console.WriteLine("[SPELL VALIDATION] will train fresh load");
                return false;
            }

            int limit = MeasureSpellSpaceFromShot(shot) ?? 11;
            var expected = GetExpectedSpellCounts(limit);
            bool rageCountOk = ValidateIconCount(shot, SpellRoi, "rage", rageCenter, expected.RageCount);
            bool freezeCountOk = ValidateIconCount(shot, SpellRoi, "freeze", freezeCenter, expected.FreezeCount);
            if (!rageCountOk || !freezeCountOk)
            {
                return false;
            }

            Console.WriteLine("[SPELL VALIDATION] composition ok");
            return true;
        }

        private bool ValidateSiege()
        {
            using Mat? shot = _adb.TakeScreenshot();
            if (shot == null || shot.Empty())
            {
                return false;
            }

            using Mat roi = Crop(shot, SiegeRoi);
            return TryMatch("Siege Machines", "slammer", roi, ValidationIconThreshold, out _, out _);
        }

        public static void DiagnoseSavedArmyWindow(string imagePath, string templatesPath)
        {
            string templateRoot = Path.Combine(templatesPath, "Smart_Auto_train");
            if (!File.Exists(imagePath))
            {
                Console.WriteLine($"[DIAG] saved Army Window image not found: {imagePath}");
                return;
            }

            using Mat shot = Cv2.ImRead(imagePath, ImreadModes.Color);
            if (shot.Empty())
            {
                Console.WriteLine($"[DIAG] saved Army Window image is empty/unreadable: {imagePath}");
                return;
            }

            Console.WriteLine($"[DIAG] Analyzing saved Army Window image: {imagePath}");
            Console.WriteLine($"[DIAG] Image size: {shot.Width}x{shot.Height}");

            VisionEngine vision = new(templatesPath);
            DiagnoseTemplate(shot, templateRoot, vision, ArmyRoi, "Army Troops", "dragon", ValidationIconThreshold);
            DiagnoseTemplate(shot, templateRoot, vision, ArmyRoi, "s_troops", "s_dragon", ValidationIconThreshold);
            DiagnoseTemplate(shot, templateRoot, vision, ArmyRoi, "Army Troops", "balloon", ValidationIconThreshold);
            DiagnoseTemplate(shot, templateRoot, vision, SpellRoi, "Spells", "rage", ValidationIconThreshold);
            DiagnoseTemplate(shot, templateRoot, vision, SpellRoi, "Spells", "freeze", ValidationIconThreshold);
            DiagnoseTemplate(shot, templateRoot, vision, SiegeRoi, "Siege Machines", "slammer", ValidationIconThreshold);
        }

        private void TrainTroops(JsonElement cfg)
        {
            ArmySpec spec = GetArmySpec(cfg);

            using Mat? shot = _adb.TakeScreenshot();
            int limit = 240;
            if (shot != null && !shot.Empty())
            {
                int? measured = MeasureArmySpaceSecondary(shot);
                if (measured is >= 120)
                {
                    limit = measured.Value;
                }
            }

            Console.WriteLine($"[SPACE CHECK] Available space = {limit}");

            ClearIfTrash(TrashArmyRoi, TapClearArmy, ConfirmTapArmy);
            _adb.Tap(OpenArmyTab.X, OpenArmyTab.Y);
            Thread.Sleep(1000);

            int mainCost = SpaceCost[spec.Main];
            int mainSpace = ((limit * 80 / 100) / mainCost) * mainCost;
            int mainCount = mainSpace / mainCost;
            int balloonCount = Math.Max(0, (limit - mainSpace) / SpaceCost["balloon"]);

            Console.WriteLine($"[TRAIN] {mainCount}x{spec.Main}, {balloonCount}xballoon (limit={limit})");
            TapIconInTab(spec.Main, mainCount);
            TapIconInTab("balloon", balloonCount);

            _adb.Tap(CloseArmyTab.X, CloseArmyTab.Y);
            Thread.Sleep(1000);
        }

        private void TrainSpells()
        {
            int limit = MeasureSpellSpace() ?? 11;
            Console.WriteLine($"[SPELL SPACE CHECK] Available space = {limit}");

            ClearIfTrash(TrashSpellRoi, TapClearSpell, ConfirmTapSpell);
            _adb.Tap(OpenSpellTab.X, OpenSpellTab.Y);
            Thread.Sleep(1000);

            var expected = GetExpectedSpellCounts(limit);
            int rageCount = expected.RageCount;
            int freezeCount = expected.FreezeCount;

            Console.WriteLine($"[TRAIN] {rageCount}xrage, {freezeCount}xfreeze (limit={limit})");
            TapIconInTab("rage", rageCount);
            TapIconInTab("freeze", freezeCount);

            _adb.Tap(CloseSpellTab.X, CloseSpellTab.Y);
            Thread.Sleep(1000);
        }

        private void TrainSlammer()
        {
            using Mat? shot = _adb.TakeScreenshot();
            if (shot != null && !shot.Empty())
            {
                using Mat roi = Crop(shot, SiegeRoi);
                if (TryMatch("Siege Machines", "slammer", roi, 0.80, out _, out _))
                {
                    Console.WriteLine("[SIEGE] composition ok");
                    return;
                }
            }

            Console.WriteLine("[SIEGE] 'slammer' missing - will rebuild");
            ClearIfTrash(TrashSiegeRoi, TapClearSiege, ConfirmTapSiege);

            _adb.Tap(OpenSiegeTab.X, OpenSiegeTab.Y);
            Thread.Sleep(1000);

            Console.WriteLine("[TRAIN] 3xslammer");
            TapIconInTab("slammer", 3);

            _adb.Tap(CloseSiegeTab.X, CloseSiegeTab.Y);
            Thread.Sleep(1000);
        }

        private void ClearIfTrash(Rect roi, Point tapCoord, Point confirmCoord)
        {
            using Mat? shot = _adb.TakeScreenshot();
            if (shot == null || shot.Empty())
            {
                return;
            }

            using Mat crop = Crop(shot, roi);
            if (!TryMatch("to_train", "trash_icon", crop, 0.80, out _, out _))
            {
                return;
            }

            Console.WriteLine("[TRASH] cleaning troops...");
            _adb.Tap(tapCoord.X, tapCoord.Y);
            Thread.Sleep(1000);
            _adb.Tap(confirmCoord.X, confirmCoord.Y);
            Thread.Sleep(1000);
        }

        private void TapIconInTab(string name, int count)
        {
            if (count <= 0)
            {
                return;
            }

            using Mat? tab = _adb.TakeScreenshot();
            if (tab == null || tab.Empty())
            {
                return;
            }

            if (!TryMatch("to_train", name, tab, 0.70, out Point center, out _))
            {
                Console.WriteLine($"[TRAIN] {name}.png not found in tab");
                return;
            }

            for (int i = 0; i < count; i++)
            {
                _adb.Tap(center.X, center.Y);
            }
        }

        private int? MeasureArmySpaceSecondary(Mat shot)
        {
            using Mat region = Crop(shot, ArmySpaceSecondaryRoi);
            using Mat regionGray = new();
            Cv2.CvtColor(region, regionGray, ColorConversionCodes.BGR2GRAY);

            int[] spaceMap = { 220, 240, 260, 280, 310, 320, 300, 340 };
            int bestIndex = -1;
            double bestScore = -1.0;

            for (int i = 0; i < spaceMap.Length; i++)
            {
                string templatePath = Path.Combine(_templateRoot, $"army_space_{i}.png");
                if (!File.Exists(templatePath))
                {
                    continue;
                }

                using Mat template = Cv2.ImRead(templatePath, ImreadModes.Grayscale);
                if (template.Empty() || template.Width > regionGray.Width || template.Height > regionGray.Height)
                {
                    continue;
                }

                using Mat result = new();
                Cv2.MatchTemplate(regionGray, template, result, TemplateMatchModes.CCoeffNormed);
                Cv2.MinMaxLoc(result, out _, out double maxVal, out _, out _);

                if (maxVal > bestScore)
                {
                    bestScore = maxVal;
                    bestIndex = i;
                }
            }

            if (bestIndex >= 0 && bestScore >= 0.90)
            {
                int space = spaceMap[bestIndex];
                Console.WriteLine($"[SPACE secondary] match=army_space_{bestIndex}  score={bestScore:F3}  => space={space}");
                return space;
            }

            Console.WriteLine($"[SPACE secondary] no confident match (best={bestScore:F3}), skipping.");
            return null;
        }

        private int? MeasureSpellSpace()
        {
            using Mat? shot = _adb.TakeScreenshot();
            if (shot == null || shot.Empty())
            {
                return null;
            }

            return MeasureSpellSpaceFromShot(shot);
        }

        private int? MeasureSpellSpaceFromShot(Mat shot)
        {
            using Mat spaceImage = Crop(shot, SpellSpaceRoi);
            double bestScore = -1.0;
            int? bestLimit = null;

            foreach (int value in new[] { 6, 9, 11 })
            {
                string templatePath = Path.Combine(_templateRoot, $"Spell_space_{value}.png");
                if (!File.Exists(templatePath))
                {
                    continue;
                }

                using Mat template = Cv2.ImRead(templatePath, ImreadModes.Color);
                if (template.Empty() || template.Width > spaceImage.Width || template.Height > spaceImage.Height)
                {
                    continue;
                }

                using Mat result = new();
                Cv2.MatchTemplate(spaceImage, template, result, TemplateMatchModes.CCoeffNormed);
                Cv2.MinMaxLoc(result, out _, out double maxVal, out _, out _);

                if (maxVal > bestScore)
                {
                    bestScore = maxVal;
                    bestLimit = value;
                }
            }

            if (bestLimit == null || bestScore < 0.85)
            {
                Console.WriteLine($"[SPELL SPACE CHECK] no good match (best={bestScore:F3}), defaulting to 11");
                return 11;
            }

            return bestLimit.Value;
        }

        private bool ValidateIconCount(Mat shot, Rect sectionRoi, string label, Point centerInSection, int expected)
        {
            if (expected <= 0)
            {
                return true;
            }

            Rect countRoi = CountRoiForIcon(shot, sectionRoi, centerInSection);
            if (!_vision.TryExtractNumericalMetrics(shot, countRoi, out int actual, out double confidence, useRgbThresh: true))
            {
                Console.WriteLine($"[COUNT OCR] {label}: unknown, keeping template fallback");
                return true;
            }

            int normalized = NormalizeBadgeCount(actual, expected);
            if (normalized != actual)
            {
                Console.WriteLine($"[COUNT OCR] {label}: read={actual}, normalized={normalized}, expected>={expected}, confidence={confidence:F2}");
            }
            else
            {
                Console.WriteLine($"[COUNT OCR] {label}: read={actual}, expected>={expected}, confidence={confidence:F2}");
            }

            if (confidence < 0.58)
            {
                return true;
            }

            if (normalized < expected)
            {
                Console.WriteLine($"[COUNT OCR] {label} missing count - will train fresh load");
                return false;
            }

            return true;
        }

        private static Rect CountRoiForIcon(Mat shot, Rect sectionRoi, Point centerInSection)
        {
            int iconLeft = sectionRoi.X + centerInSection.X - 62;
            int iconTop = sectionRoi.Y + centerInSection.Y - 62;
            Rect rough = new(iconLeft + 24, iconTop + 8, 44, 26);
            return ImageUtils.ClampRect(rough, shot.Width, shot.Height);
        }

        private static int NormalizeBadgeCount(int actual, int expected)
        {
            int normalized = actual;
            int suspiciousLimit = Math.Max(expected + 3, expected * 2);
            while (normalized >= 10 && normalized > suspiciousLimit)
            {
                int digits = normalized.ToString().Length;
                int divisor = (int)Math.Pow(10, digits - 1);
                normalized %= divisor;
            }

            return normalized == 0 ? actual : normalized;
        }

        private static (int MainCount, int BalloonCount) GetExpectedTroopCounts(ArmySpec spec, int limit)
        {
            int mainCost = SpaceCost[spec.Main];
            int mainSpace = ((limit * 80 / 100) / mainCost) * mainCost;
            int mainCount = mainSpace / mainCost;
            int balloonCount = Math.Max(0, (limit - mainSpace) / SpaceCost["balloon"]);
            return (mainCount, balloonCount);
        }

        private static (int RageCount, int FreezeCount) GetExpectedSpellCounts(int limit)
        {
            int primarySpace = ((limit * 80 / 100) / 2) * 2;
            int rageCount = primarySpace / 2;
            int freezeCount = Math.Max(0, limit - primarySpace);
            return (rageCount, freezeCount);
        }

        private bool TryFindTemplate(Mat screenshot, string templateName, Rect? roi, out Point center, out double score)
        {
            center = default;
            score = 0;

            string templatePath = Path.Combine(_templateRoot, templateName);
            if (!File.Exists(templatePath))
            {
                Console.WriteLine($"[quick_train WARNING] Missing template: {templatePath}");
                return false;
            }

            using Mat template = Cv2.ImRead(templatePath, ImreadModes.Color);
            if (template.Empty())
            {
                return false;
            }

            Rect searchRect = roi ?? new Rect(0, 0, screenshot.Width, screenshot.Height);
            searchRect = ImageUtils.ClampRect(searchRect, screenshot.Width, screenshot.Height);
            if (searchRect.Width < template.Width || searchRect.Height < template.Height)
            {
                return false;
            }

            using Mat searchArea = new(screenshot, searchRect);
            using Mat result = new();
            Cv2.MatchTemplate(searchArea, template, result, TemplateMatchModes.CCoeffNormed);
            Cv2.MinMaxLoc(result, out _, out score, out _, out Point maxLoc);

            center = new Point(
                searchRect.X + maxLoc.X + template.Width / 2,
                searchRect.Y + maxLoc.Y + template.Height / 2
            );
            return true;
        }

        private bool TryMatch(string subdir, string name, Mat haystack, double threshold, out Point center, out double score)
        {
            center = default;
            score = 0;

            string? templatePath = FindTemplatePath(name, subdir);
            if (templatePath == null || haystack.Empty())
            {
                return false;
            }

            using Mat template = Cv2.ImRead(templatePath, ImreadModes.Color);
            if (template.Empty() || template.Width > haystack.Width || template.Height > haystack.Height)
            {
                return false;
            }

            using Mat result = new();
            Cv2.MatchTemplate(haystack, template, result, TemplateMatchModes.CCoeffNormed);
            Cv2.MinMaxLoc(result, out _, out score, out _, out Point maxLoc);

            center = new Point(maxLoc.X + template.Width / 2, maxLoc.Y + template.Height / 2);
            bool matched = score >= threshold;
            string verdict = matched ? "ok" : "low";
            Console.WriteLine($"[TEMPLATE] {subdir}/{name}: score={score:F3}, threshold={threshold:F2}, center=({center.X},{center.Y}) => {verdict}");
            return matched;
        }

        private static void DiagnoseTemplate(Mat shot, string templateRoot, VisionEngine vision, Rect roi, string subdir, string name, double threshold)
        {
            using Mat haystack = Crop(shot, roi);
            string? templatePath = FindTemplatePath(templateRoot, name, subdir);
            if (templatePath == null)
            {
                Console.WriteLine($"[DIAG] {subdir}/{name}: template missing");
                return;
            }

            using Mat template = Cv2.ImRead(templatePath, ImreadModes.Color);
            if (template.Empty() || template.Width > haystack.Width || template.Height > haystack.Height)
            {
                Console.WriteLine($"[DIAG] {subdir}/{name}: template invalid or larger than ROI");
                return;
            }

            using Mat result = new();
            Cv2.MatchTemplate(haystack, template, result, TemplateMatchModes.CCoeffNormed);
            Cv2.MinMaxLoc(result, out _, out double score, out _, out Point maxLoc);

            Point centerInSection = new(maxLoc.X + template.Width / 2, maxLoc.Y + template.Height / 2);
            Point centerAbsolute = new(roi.X + centerInSection.X, roi.Y + centerInSection.Y);
            string verdict = score >= threshold ? "ok" : "low";
            Console.WriteLine($"[DIAG] {subdir}/{name}: score={score:F3}, threshold={threshold:F2}, centerAbs=({centerAbsolute.X},{centerAbsolute.Y}) => {verdict}");

            if (score < 0.60)
            {
                return;
            }

            Rect countRoi = CountRoiForIcon(shot, roi, centerInSection);
            if (vision.TryExtractNumericalMetrics(shot, countRoi, out int actual, out double confidence, useRgbThresh: true))
            {
                Console.WriteLine($"[DIAG COUNT OCR] {name}: read={actual}, confidence={confidence:F2}, roi=({countRoi.X},{countRoi.Y},{countRoi.Width},{countRoi.Height})");
            }
            else
            {
                Console.WriteLine($"[DIAG COUNT OCR] {name}: unknown, roi=({countRoi.X},{countRoi.Y},{countRoi.Width},{countRoi.Height})");
            }

            ScanCountCandidates(shot, roi, centerInSection, vision, name);
        }

        private static void ScanCountCandidates(Mat shot, Rect sectionRoi, Point centerInSection, VisionEngine vision, string name)
        {
            int iconLeft = sectionRoi.X + centerInSection.X - 62;
            int iconTop = sectionRoi.Y + centerInSection.Y - 62;
            var candidates = new List<(int Value, double Confidence, Rect Roi)>();

            for (int y = 0; y <= 100; y += 8)
            {
                for (int x = 0; x <= 86; x += 8)
                {
                    Rect roi = ImageUtils.ClampRect(new Rect(iconLeft + x, iconTop + y, 44, 26), shot.Width, shot.Height);
                    if (roi.Width < 30 || roi.Height < 18)
                    {
                        continue;
                    }

                    if (!vision.TryExtractNumericalMetrics(shot, roi, out int value, out double confidence, useRgbThresh: true))
                    {
                        continue;
                    }

                    if (value > 0 && value <= 80 && confidence >= 0.68)
                    {
                        candidates.Add((value, confidence, roi));
                    }
                }
            }

            var best = candidates
                .OrderByDescending(c => c.Confidence)
                .ThenBy(c => c.Value >= 10 ? 0 : 1)
                .Take(8)
                .ToList();

            if (best.Count == 0)
            {
                Console.WriteLine($"[DIAG COUNT SCAN] {name}: no plausible candidates");
                return;
            }

            string summary = string.Join("; ", best.Select(c => $"{c.Value}@{c.Confidence:F2}/({c.Roi.X},{c.Roi.Y},{c.Roi.Width},{c.Roi.Height})"));
            Console.WriteLine($"[DIAG COUNT SCAN] {name}: {summary}");
        }

        private string? FindTemplatePath(string name, string? subdir = null)
        {
            string fileName = name.EndsWith(".png", StringComparison.OrdinalIgnoreCase) ? name : $"{name}.png";
            string root = subdir == null ? _templateRoot : Path.Combine(_templateRoot, subdir);
            if (!Directory.Exists(root))
            {
                return null;
            }

            return Directory.EnumerateFiles(root, fileName, SearchOption.AllDirectories).FirstOrDefault();
        }

        private static string? FindTemplatePath(string templateRoot, string name, string? subdir = null)
        {
            string fileName = name.EndsWith(".png", StringComparison.OrdinalIgnoreCase) ? name : $"{name}.png";
            string root = subdir == null ? templateRoot : Path.Combine(templateRoot, subdir);
            if (!Directory.Exists(root))
            {
                return null;
            }

            return Directory.EnumerateFiles(root, fileName, SearchOption.AllDirectories).FirstOrDefault();
        }

        private static Mat Crop(Mat image, Rect rect)
        {
            Rect clamped = ImageUtils.ClampRect(rect, image.Width, image.Height);
            return new Mat(image, clamped);
        }

        private static ArmySpec GetArmySpec(JsonElement cfg)
        {
            string attack = "Dragon_Attack";
            if (cfg.ValueKind == JsonValueKind.Object
                && cfg.TryGetProperty("attack", out JsonElement attackElement)
                && attackElement.ValueKind == JsonValueKind.String)
            {
                attack = attackElement.GetString() ?? attack;
            }

            return ArmySets.TryGetValue(attack, out ArmySpec? spec)
                ? spec
                : ArmySets["Dragon_Attack"];
        }



        private sealed record ArmySpec(string Main, string[] Troops, string[] Spells, string Siege);
    }
}
