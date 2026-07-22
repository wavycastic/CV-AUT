using System;
using System.Threading;

namespace CvAut
{
    internal sealed partial class BuilderBaseMaintenance
    {
        public bool CleanYard(CancellationToken token)
        {
            if (token.IsCancellationRequested) return false;
            Console.WriteLine("[BB_MAINTENANCE] phase=clean_yard status=start");
            var cleaner = new BuilderBaseObstacleCleaner(_adb, _vision, _navigator);
            return cleaner.CleanObstacles(token);
        }
    }
}
