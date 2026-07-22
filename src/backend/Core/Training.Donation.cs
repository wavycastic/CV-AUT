using System;
using System.Threading;

namespace CvAut
{
    internal partial class Training
    {
        public bool RequestClanTroops(CancellationToken token)
        {
            if (token.IsCancellationRequested) return false;
            Console.WriteLine("[TRAINING] phase=request_clan_troops status=start");
            var donationManager = new ClanDonationManager(_adb, _vision);
            return donationManager.RequestTroops(token);
        }
    }
}
