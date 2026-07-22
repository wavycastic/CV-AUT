using System;
using System.Threading;

namespace CvAut
{
    internal partial class Attacks
    {
        public bool CastLightningSpells(CancellationToken token)
        {
            if (token.IsCancellationRequested) return false;
            Console.WriteLine("[ATTACKS] phase=cast_spells status=start type=lightning");
            return true;
        }
    }
}
