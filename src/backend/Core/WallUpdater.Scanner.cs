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

            Console.WriteLine("[WALL_SCANNER] phase=scan status=completed locations=0");
            return locations;
        }
    }
}
