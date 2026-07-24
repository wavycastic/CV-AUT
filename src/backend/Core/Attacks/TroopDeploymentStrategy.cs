using System;
using System.Threading;
using CvAut.AttackPipelines;

namespace CvAut
{
    /// <summary>
    /// Compatibility strategy for the legacy BARCH entry point. New attack flows use
    /// the same ITroopDeploymentStrategy contract as the staged pipeline.
    /// </summary>
    internal sealed class StandardBarchStrategy : ITroopDeploymentStrategy
    {
        private readonly IADBHelper _adb;

        public StandardBarchStrategy(IADBHelper adb)
        {
            _adb = adb ?? throw new ArgumentNullException(nameof(adb));
        }

        public string Name => "barch_standard";

        public AttackStageResult Deploy(AttackContext context)
        {
            CancellationToken token = context.CancellationToken;
            if (token.IsCancellationRequested)
                return AttackStageResult.Cancelled();

            Console.WriteLine("[ATTACK] phase=deploy strategy=barch status=executing");
            _adb.Tap(200, 700);
            if (token.WaitHandle.WaitOne(300))
                return AttackStageResult.Cancelled();
            _adb.Swipe(200, 200, 1400, 200, 800);

            _adb.Tap(300, 700);
            if (token.WaitHandle.WaitOne(300))
                return AttackStageResult.Cancelled();
            _adb.Swipe(200, 200, 1400, 200, 800);

            return token.IsCancellationRequested
                ? AttackStageResult.Cancelled()
                : AttackStageResult.Success();
        }
    }
}
