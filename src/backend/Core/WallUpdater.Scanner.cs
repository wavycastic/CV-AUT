using System;
using System.Collections.Generic;
using OpenCvSharp;

namespace CvAut
{
    internal sealed partial class WallUpdater
    {
        public List<Point> ScanWallLocations(Mat screenshot)
        {
            var locations = new List<Point>();
            if (screenshot == null || screenshot.Empty()) return locations;

            // Delegated candidate scanning logic using active generic templates
            string[] templates = GetWallTemplateNames();
            if (templates.Length == 0) return locations;

            using Mat gray = new Mat();
            Cv2.CvtColor(screenshot, gray, ColorConversionCodes.BGR2GRAY);

            var merged = new List<WallCandidate>();
            foreach (string t in templates)
            {
                merged.AddRange(MatchWallTemplateInRoi(gray, t, BuilderUpgradeMenuRoi));
            }

            locations.AddRange(DedupeCandidates(merged, 10).Select(c => c.Point));
            return locations;
        }
    }
}
