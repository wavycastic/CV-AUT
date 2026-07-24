using System;
using System.Threading;

namespace CvAut;

internal static class ThreadingUtil
{
    public static bool InterruptibleSleep(int milliseconds, CancellationToken token = default)
    {
        if (token == default)
        {
            Thread.Sleep(milliseconds);
            return false;
        }
        DateTime end = DateTime.Now.AddMilliseconds(milliseconds);
        while (DateTime.Now < end)
        {
            int waitMs = Math.Min(500, Math.Max(1, (int)(end - DateTime.Now).TotalMilliseconds));
            if (token.WaitHandle.WaitOne(waitMs))
                return true;
        }
        return false;
    }
}
