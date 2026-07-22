using System;
using System.IO;
using System.Threading;
using OpenCvSharp;

namespace CvAut.Handlers
{
    /// <summary>
    /// Xử lý popup nhận thưởng sao (Star Bonus Popup).
    /// </summary>
    internal class StarBonusPopupHandler
    {
        private readonly ADBHelper _adb;
        private readonly VisionEngine _vision;
        private readonly string _templatesPath;

        public StarBonusPopupHandler(ADBHelper adb, VisionEngine vision, string templatesPath)
        {
            _adb = adb;
            _vision = vision;
            _templatesPath = templatesPath;
        }

        public bool HandleIfPresent()
        {
            using Mat? screenshot = _adb.TakeScreenshot();
            if (screenshot == null) return false;
            return HandleIfPresent(screenshot);
        }

        public bool HandleIfPresent(Mat screenshot)
        {
            string templatePath = Path.Combine(_templatesPath, @"ui\star_bonus.png");
            if (!File.Exists(templatePath)) return false;

            if (_vision.TryFindTemplate(screenshot, templatePath, AutomationRoiConstants.StarBonusPopupRoi, AutomationThresholds.StarBonusPopupThreshold, out Point p, out double score))
            {
                Console.WriteLine($"[STAR BONUS] phase=detected status=dismissing score={score:F3} pos=({p.X},{p.Y})");
                _adb.Tap(AutomationRoiConstants.StarBonusOkayTapPoint.X, AutomationRoiConstants.StarBonusOkayTapPoint.Y);
                Thread.Sleep(1000);
                return true;
            }

            return false;
        }
    }
}
