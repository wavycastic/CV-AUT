using System.Threading;

namespace CvAut
{
    /// <summary>
    /// Điều hướng cơ bản trên màn hình Làng chính khi nâng cấp tường: giải tỏa menu/popup còn sót lại.
    /// </summary>
    internal sealed class WallMenuNavigator
    {
        private readonly IADBHelper _adb;

        public WallMenuNavigator(IADBHelper adb)
        {
            _adb = adb;
        }

        /// <summary>Giải tỏa menu theo kiểu best-effort, nuốt mọi lỗi để không phá vỡ luồng chính.</summary>
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

        /// <summary>Giải tỏa menu nhưng tôn trọng tín hiệu hủy.</summary>
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
