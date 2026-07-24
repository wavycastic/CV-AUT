using System;
using System.Threading;

namespace CvAut;

internal sealed class DonationService
{
    private readonly IADBHelper _adb;

    public DonationService(IADBHelper adb)
    {
        _adb = adb;
    }

    public bool RequestClanTroops(CancellationToken token)
    {
        if (token.IsCancellationRequested) return false;
        Console.WriteLine("[CLAN] phase=request_troops status=start");
        foreach ((int x, int y, int delay) in new[]
        {
            (65, 625, 800),
            (1020, 680, 800),
            (960, 620, 800),
            (1480, 75, 500)
        })
        {
            _adb.Tap(x, y);
            if (token.WaitHandle.WaitOne(delay)) return false;
        }
        Console.WriteLine("[CLAN] phase=request_troops status=success");
        return true;
    }
}
