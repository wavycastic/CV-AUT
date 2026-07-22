using System;
using System.Threading;

namespace CvAut
{
    /// <summary>
    /// Chuyên xử lý tiến trình Quick Train lính và xếp hàng xin/luyện lính cho Làng chính.
    /// </summary>
    internal class TroopTrainer
    {
        private readonly ADBHelper _adb;
        private readonly VisionEngine _vision;

        public TroopTrainer(ADBHelper adb, VisionEngine vision)
        {
            _adb = adb;
            _vision = vision;
        }

        public bool TrainTroops(CancellationToken token)
        {
            if (token.IsCancellationRequested) return false;
            Console.WriteLine("[TRAINING] phase=train_troops status=start");

            // Mở giao diện luyện quân (Train Army button)
            _adb.Tap(65, 625);
            Thread.Sleep(1000);

            // Bấm Quick Train slot 1
            _adb.Tap(1360, 260);
            Thread.Sleep(800);

            // Đóng giao diện luyện quân
            _adb.Tap(1480, 75);
            Thread.Sleep(600);

            Console.WriteLine("[TRAINING] phase=train_troops status=success");
            return true;
        }
    }
}
