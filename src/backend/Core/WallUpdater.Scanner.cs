using System.Collections.Generic;
using OpenCvSharp;
using Point = OpenCvSharp.Point;

namespace CvAut
{
    internal sealed partial class WallUpdater
    {
        /// <summary>Quét vị trí tường trên một ảnh có sẵn; uỷ quyền cho WallCandidateScanner.</summary>
        public List<Point> ScanWallLocations(Mat screenshot) => _scanner.ScanWallLocations(screenshot);
    }
}
