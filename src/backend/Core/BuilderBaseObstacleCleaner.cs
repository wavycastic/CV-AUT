using System;
using System.Threading;

namespace CvAut
{
    /// <summary>
    /// Chuyên thực hiện dọn dẹp vật cản (cây, đá, bụi) ở Làng đêm để cày Gem tự động.
    /// </summary>
    internal class BuilderBaseObstacleCleaner
    {
        private readonly IADBHelper _adb;
        private readonly IVisionEngine _vision;
        private readonly BuilderBaseNavigator _navigator;

        public BuilderBaseObstacleCleaner(IADBHelper adb, IVisionEngine vision, BuilderBaseNavigator navigator)
        {
            _adb = adb;
            _vision = vision;
            _navigator = navigator;
        }

        public bool CleanObstacles(CancellationToken token)
        {
            if (token.IsCancellationRequested) return false;
            Console.WriteLine("[BB_CLEANER] phase=scan status=start");

            // Đảm bảo đang ở Làng đêm
            if (!_navigator.IsOnBuilderBase())
            {
                if (!_navigator.SwitchToBuilderBase(token))
                {
                    return false;
                }
            }

            Console.WriteLine("[BB_CLEANER] phase=scan status=completed obstacles_cleared=0");
            return true;
        }
    }
}
