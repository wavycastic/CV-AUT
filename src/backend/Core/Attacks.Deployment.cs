using System;
using System.Threading;

namespace CvAut
{
    internal partial class Attacks
    {
        public bool DeployTroopsWithStrategy(string strategyName, CancellationToken token)
        {
            if (token.IsCancellationRequested) return false;
            Console.WriteLine($"[ATTACKS] phase=deploy_strategy status=start strategy={strategyName}");
            var strategy = new StandardBarchStrategy();
            strategy.Execute(_adb, _vision, token);
            return true;
        }
    }
}
