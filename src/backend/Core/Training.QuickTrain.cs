using System;
using System.Threading;

namespace CvAut
{
    internal partial class Training
    {
        public bool ExecuteQuickTrain(int slotIndex, CancellationToken token)
        {
            if (token.IsCancellationRequested) return false;
            Console.WriteLine($"[TRAINING] phase=quick_train status=start slot={slotIndex}");
            var trainer = new TroopTrainer(_adb, _vision);
            return trainer.TrainTroops(token);
        }
    }
}
