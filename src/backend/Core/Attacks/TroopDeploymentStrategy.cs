using System;
using System.Threading;

namespace CvAut
{
    /// <summary>
    /// Định nghĩa giao diện và thuật toán thực thi chiến thuật thả quân tự động (Troop Deployment Strategy).
    /// </summary>
    internal interface ITroopDeploymentStrategy
    {
        string Name { get; }
        void Execute(ADBHelper adb, VisionEngine vision, CancellationToken token);
    }

    internal class StandardBarchStrategy : ITroopDeploymentStrategy
    {
        public string Name => "barch_standard";

        public void Execute(ADBHelper adb, VisionEngine vision, CancellationToken token)
        {
            if (token.IsCancellationRequested) return;

            Console.WriteLine("[ATTACK] phase=deploy strategy=barch status=executing");
            // Thả quân Barbarian & Archer quanh ranh giới đỏ làng đối thủ
            adb.Tap(200, 700); // Chọn slot Barbarian
            Thread.Sleep(300);
            adb.Swipe(200, 200, 1400, 200, 800); // Rải đường trên

            adb.Tap(300, 700); // Chọn slot Archer
            Thread.Sleep(300);
            adb.Swipe(200, 200, 1400, 200, 800); // Rải đường trên
        }
    }
}
