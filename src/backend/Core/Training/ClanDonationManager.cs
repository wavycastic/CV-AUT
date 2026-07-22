using System;
using System.Threading;

namespace CvAut
{
    /// <summary>
    /// Chuyên xử lý tiến trình xin quân Clan (Request Castle Reinforcements).
    /// </summary>
    internal class ClanDonationManager
    {
        private readonly ADBHelper _adb;
        private readonly VisionEngine _vision;

        public ClanDonationManager(ADBHelper adb, VisionEngine vision)
        {
            _adb = adb;
            _vision = vision;
        }

        public bool RequestTroops(CancellationToken token)
        {
            if (token.IsCancellationRequested) return false;
            Console.WriteLine("[CLAN] phase=request_troops status=start");

            // Mở giao diện luyện quân & tab Xin quân
            _adb.Tap(65, 625);
            Thread.Sleep(800);

            // Bấm nút Xin quân (Request)
            _adb.Tap(1020, 680);
            Thread.Sleep(800);

            // Gửi yêu cầu xin quân (Send Request)
            _adb.Tap(960, 620);
            Thread.Sleep(800);

            _adb.Tap(1480, 75);
            Thread.Sleep(500);

            Console.WriteLine("[CLAN] phase=request_troops status=success");
            return true;
        }
    }
}
