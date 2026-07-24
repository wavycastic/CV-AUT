using System;
using System.Collections.Generic;
using System.Linq;
using OpenCvSharp;

namespace CvAut
{
    internal sealed partial class WallUpdater
    {
        public List<Point> ScanWallLocations(Mat screenshot)
        {
            var locations = new List<Point>();
            if (screenshot == null || screenshot.Empty()) return locations;

            string[] templates = GetWallTemplateNames();
            if (templates.Length == 0) return locations;

            Rect roi = ImageUtils.ClampRect(BuilderUpgradeMenuRoi, screenshot.Width, screenshot.Height);
            if (roi.Width <= 0 || roi.Height <= 0) return locations;

            using Mat roiBgr = new Mat(screenshot, roi);
            using Mat roiGray = new Mat();
            Cv2.CvtColor(roiBgr, roiGray, ColorConversionCodes.BGR2GRAY);

            var merged = new List<WallCandidate>();
            foreach (string t in templates)
            {
                merged.AddRange(MatchWallTemplateInRoi(roiGray, t, BuilderUpgradeMenuRoi));
            }

            locations.AddRange(DedupeCandidates(merged, 10).Select(c => c.Point));
            return locations;
        }
    }
}
