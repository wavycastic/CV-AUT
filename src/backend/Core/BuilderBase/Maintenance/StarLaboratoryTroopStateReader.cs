using System;
using System.IO;
using System.Linq;
using OpenCvSharp;
using Point = OpenCvSharp.Point;

namespace CvAut
{
    internal static class StarLaboratoryTroopStateReader
    {
        public static StarLabTroopState ReadStarLabTroopState(Mat screenshot, Point center)
        {
            if (BuilderBaseMaintenanceUi.PixelNear(screenshot, center.X + 47, center.Y + 1, 0xD3D3CB, 20)) return StarLabTroopState.NotUnlocked;
            if (BuilderBaseMaintenanceUi.PixelNear(screenshot, center.X + 22, center.Y + 60, 0xFFC360, 20)) return StarLabTroopState.MaxLevel;
            if (BuilderBaseMaintenanceUi.PixelNear(screenshot, center.X + 76, center.Y + 76, 0xFFFFFF, 20) || BuilderBaseMaintenanceUi.PixelNear(screenshot, center.X + 76, center.Y + 80, 0xFFFFFF, 20)) return StarLabTroopState.MaxLevel;
            if (BuilderBaseMaintenanceUi.PixelNear(screenshot, center.X + 3, center.Y + 19, 0xB7B7B7, 20) || BuilderBaseMaintenanceUi.PixelNear(screenshot, center.X + 93, center.Y + 20, 0x757575, 24)) return StarLabTroopState.LabUpgradeRequiredOrBusy;
            if (BuilderBaseMaintenanceUi.PixelNear(screenshot, center.X + 67, center.Y + 79, 0xFF7B72, 24) || BuilderBaseMaintenanceUi.PixelNear(screenshot, center.X + 67, center.Y + 82, 0xFF7B72, 24)) return StarLabTroopState.NotEnoughLoot;
            if (BuilderBaseMaintenanceUi.PixelNear(screenshot, center.X + 47, center.Y + 40, 0xD3D3CB, 28)) return StarLabTroopState.Unknown;
            return StarLabTroopState.Upgradeable;
        }

        public static int ReadStarLabResourceCost(Point troopCenter, string troopName, BuilderBaseMaintenanceUi ui)
        {
            Rect redRoi = Rect.FromLTRB(troopCenter.X + 2, troopCenter.Y + 76, troopCenter.X + 172, troopCenter.Y + 112);
            int red = ui.ReadNumberFromCurrentScreen(redRoi, 100_000_000);
            if (red >= 3000)
            {
                Console.WriteLine($"[BB-MAINT] phase=star_laboratory_ocr status=success mode=resource_red troop=\"{troopName}\" value={red}");
                return red;
            }

            Rect whiteRoi = Rect.FromLTRB(troopCenter.X + 2, troopCenter.Y + 86, troopCenter.X + 180, troopCenter.Y + 124);
            int white = ui.ReadNumberFromCurrentScreen(whiteRoi, 100_000_000);
            if (white >= 3000)
            {
                Console.WriteLine($"[BB-MAINT] phase=star_laboratory_ocr status=success mode=resource_white troop=\"{troopName}\" value={white}");
                return white;
            }

            int fallback = ReadNumberNear(troopCenter, 100_000_000, ui);
            Console.WriteLine($"[BB-MAINT] phase=star_laboratory_ocr status={(fallback >= 3000 ? "success" : "fail")} mode=resource_fallback troop=\"{troopName}\" value={fallback}");
            return fallback >= 3000 ? fallback : 0;
        }

        public static int ReadNumberNear(Point center, int maxPlausible, BuilderBaseMaintenanceUi ui)
        {
            Rect roi = Rect.FromLTRB(center.X - 10, center.Y + 45, center.X + 180, center.Y + 105);
            return ui.ReadNumberFromCurrentScreen(roi, maxPlausible);
        }

        public static int ReadStarLabTimeMinutes(Rect roi, string phase, BuilderBaseMaintenanceUi ui)
        {
            int value = ui.ReadNumberFromCurrentScreen(roi, 999999);
            Console.WriteLine($"[BB-MAINT] phase=star_laboratory_ocr status={(value > 0 ? "success" : "fail")} mode=time phase_detail={phase} minutes={value}");
            return value;
        }

        public static void SaveStarLabDebugScreenshot(BuilderBaseMaintenanceOptions options, IADBHelper adb, string phase)
        {
            if (!options.StarLaboratoryDebugScreenshots) return;
            using Mat? screenshot = adb.TakeScreenshot();
            if (screenshot == null || screenshot.Empty()) return;
            SaveStarLabDebugScreenshot(screenshot, phase);
        }

        public static void SaveStarLabDebugScreenshot(Mat screenshot, string phase)
        {
            try
            {
                string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SimpliMixi", "logs", "StarLabUpgrade");
                Directory.CreateDirectory(dir);
                string safePhase = string.Concat(phase.Select(ch => char.IsLetterOrDigit(ch) || ch == '_' || ch == '-' ? ch : '_'));
                string path = Path.Combine(dir, $"{safePhase}_{DateTime.UtcNow:yyyyMMdd_HHmmss_fff}.png");
                Cv2.ImWrite(path, screenshot);
                Console.WriteLine($"[BB-MAINT] phase=star_laboratory_debug status=saved path=\"{path}\"");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[BB-MAINT] phase=star_laboratory_debug status=fail reason=\"{ex.Message}\"");
            }
        }
    }

    internal enum StarLabTroopState
    {
        NotPresent,
        Unknown,
        Upgradeable,
        NotUnlocked,
        NotEnoughLoot,
        MaxLevel,
        LabUpgradeRequiredOrBusy
    }
}
