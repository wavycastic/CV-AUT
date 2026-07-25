using System.Threading;

namespace CvAut
{
    /// <summary>
    /// Basic main village navigation for the wall upgrade flow: dismissing leftover menus/popups.
    /// </summary>
    internal sealed class WallMenuNavigator
    {
        private readonly IADBHelper _adb;

        public WallMenuNavigator(IADBHelper adb)
        {
            _adb = adb;
        }

        /// <summary>Dismisses menus on a best-effort basis, swallowing every error so the main flow is never broken.</summary>
        public void BestEffortDismiss()
        {
            try
            {
                _adb.Tap(WallUiLayout.HomeMenuPoint.X, WallUiLayout.HomeMenuPoint.Y);
                Thread.Sleep(150);
                _adb.Tap(WallUiLayout.DismissPoint.X, WallUiLayout.DismissPoint.Y);
            }
            catch { }
        }

        /// <summary>Dismisses menus while honouring the cancellation signal.</summary>
        public void SafeDismiss(CancellationToken token)
        {
            if (token.IsCancellationRequested)
            {
                BestEffortDismiss();
                return;
            }
            _adb.Tap(WallUiLayout.HomeMenuPoint.X, WallUiLayout.HomeMenuPoint.Y);
            if (ThreadingUtil.InterruptibleSleep(150, token)) return;
            _adb.Tap(WallUiLayout.DismissPoint.X, WallUiLayout.DismissPoint.Y);
        }
    }
}
