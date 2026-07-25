using System;
using System.Threading;

namespace CvAut
{
    /// <summary>
    /// Chịu trách nhiệm duy nhất: vòng lặp chờ giả lập báo online qua ADB.
    /// </summary>
    internal static class AdbReadinessWaiter
    {
        private const int DefaultDeadlineSeconds = 90;
        private const int ConnectAttemptTimeoutSeconds = 3;
        private const int PollIntervalMs = 1000;

        /// <summary>
        /// Chờ tối đa 90 giây cho đến khi ADB báo thiết bị online, hoặc cho đến khi bị hủy.
        /// </summary>
        public static bool WaitForOnline(IADBHelper adb, CancellationToken token)
        {
            DateTime deadline = DateTime.Now.AddSeconds(DefaultDeadlineSeconds);
            while (DateTime.Now < deadline && !token.IsCancellationRequested)
            {
                // Thử kết nối trong tối đa 3 giây
                if (adb.EnsureConnectedOnline(timeoutSeconds: ConnectAttemptTimeoutSeconds))
                {
                    return true;
                }

                if (token.WaitHandle.WaitOne(PollIntervalMs)) return false;
            }

            return false;
        }
    }
}
