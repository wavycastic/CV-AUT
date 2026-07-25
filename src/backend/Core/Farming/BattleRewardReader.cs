using System;
using System.IO;
using OpenCvSharp;

namespace CvAut
{
    /// <summary>
    /// Single responsibility: read the battle outcome from the screen (star count and looted resources).
    /// </summary>
    internal sealed class BattleRewardReader
    {
        private readonly ADBHelper _adb;
        private readonly VisionEngine _vision;
        private readonly string _templatesPath;

        public BattleRewardReader(ADBHelper adb, VisionEngine vision, string templatesPath)
        {
            _adb = adb;
            _vision = vision;
            _templatesPath = templatesPath;
        }

        public int GetStarsFromScreen()
        {
            using Mat? screenshot = _adb.TakeScreenshot();
            if (screenshot == null || screenshot.Empty())
            {
                Console.WriteLine("[FSM-CS WARNING] phase=battle_stats status=fail reason=screenshot_failed");
                return 3;
            }

            using Mat gray = new Mat();
            Cv2.CvtColor(screenshot, gray, ColorConversionCodes.BGR2GRAY);
            using Mat thresh = new Mat();
            Cv2.Threshold(gray, thresh, 200, 255, ThresholdTypes.Binary);

            using Mat template3 = Cv2.ImRead(Path.Combine(_templatesPath, @"stars\3_stars.png"), ImreadModes.Grayscale);
            using Mat template2 = Cv2.ImRead(Path.Combine(_templatesPath, @"stars\2_stars.png"), ImreadModes.Grayscale);
            using Mat template1 = Cv2.ImRead(Path.Combine(_templatesPath, @"stars\1_star.png"), ImreadModes.Grayscale);

            if (template3 != null && !template3.Empty() && MatchStarTemplate(thresh, template3, 0.45)) return 3;
            if (template2 != null && !template2.Empty() && MatchStarTemplate(thresh, template2, 0.55)) return 2;
            if (template1 != null && !template1.Empty() && MatchStarTemplate(thresh, template1, 0.55)) return 1;

            Console.WriteLine("[FSM-CS WARNING] phase=battle_stats status=fallback reason=star_template_not_found");
            return 3;
        }

        private static bool MatchStarTemplate(Mat grayScreen, Mat starTemplate, double threshold)
        {
            using Mat res = new Mat();
            Cv2.MatchTemplate(grayScreen, starTemplate, res, TemplateMatchModes.CCoeffNormed);
            Cv2.MinMaxLoc(res, out _, out double maxVal, out _, out _);
            return maxVal >= threshold;
        }

        public (int Gold, int Elixir, int DarkElixir) GainResources(int stars)
        {
            using Mat? screenshot = _adb.TakeScreenshot();
            if (screenshot == null || screenshot.Empty())
            {
                Console.WriteLine("[FSM-CS] phase=gain_resources status=fail reason=screenshot_failed");
                return (0, 0, 0);
            }

            int gold = OcrResourceSum(screenshot, new Rect(315, 374, 220, 30), "gold", 100);
            int elixir = OcrResourceSum(screenshot, new Rect(710, 374, 220, 30), "elixir", 100);
            int darkElixir = OcrResourceSum(screenshot, new Rect(1085, 374, 220, 30), "dark_elixir", 10);
            Console.WriteLine($"[FSM-CS] phase=gain_resources status=success gold={gold} elixir={elixir} dark_elixir={darkElixir} stars={stars}");
            return (gold, elixir, darkElixir);
        }

        public int OcrResourceSum(Mat screenshot, Rect roi, string label, int minValidValue)
        {
            if (_vision.TryExtractNumericalMetrics(screenshot, roi, out int value, out double confidence) && IsPlausibleResourceValue(value, confidence, minValidValue, label))
                return value;
            return 0;
        }

        public static bool IsPlausibleResourceValue(int value, double confidence, int minValidValue, string label)
        {
            if (value < 0) { Console.WriteLine($"[OCR-WARN] phase=validation status=reject label={label} value={value} confidence={confidence:F2} reason=negative_value"); return false; }
            if (value < minValidValue) { Console.WriteLine($"[OCR-WARN] phase=validation status=reject label={label} value={value} min={minValidValue} reason=below_minimum"); return false; }
            if (confidence < 0.25) { Console.WriteLine($"[OCR-WARN] phase=validation status=reject label={label} value={value} confidence={confidence:F2} reason=low_confidence"); return false; }
            return true;
        }
    }
}
