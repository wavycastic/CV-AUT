using System;
using System.Threading;
using OpenCvSharp;
using Point = OpenCvSharp.Point;

namespace CvAut
{
    internal sealed class ReturnHomeController
    {
        private readonly IADBHelper _adb;
        private readonly IVisionEngine _vision;
        private readonly BuilderBaseNavigator _navigator;
        private readonly AttackEntryFlow _entryFlow;
        private readonly Func<bool> _isBBAttackPageFunc;

        public ReturnHomeController(IADBHelper adb, IVisionEngine vision, BuilderBaseNavigator navigator, AttackEntryFlow entryFlow, Func<bool> isBBAttackPageFunc)
        {
            _adb = adb;
            _vision = vision;
            _navigator = navigator;
            _entryFlow = entryFlow;
            _isBBAttackPageFunc = isBBAttackPageFunc;
        }

        public bool ReturnHomeDropTrophyBB(CancellationToken token)
        {
            Console.WriteLine("[BB-ATTACK] phase=return_home status=start");

            for (int attempt = 1; attempt <= 15 && !token.IsCancellationRequested; attempt++)
            {
                if (_navigator.IsOnBuilderBase())
                {
                    Console.WriteLine("[BB-ATTACK] phase=return_home status=success reason=already_on_builder_base");
                    return true;
                }

                if (_isBBAttackPageFunc())
                {
                    using Mat? screenshot = _adb.TakeScreenshot();
                    if (screenshot == null || screenshot.Empty())
                    {
                        Console.WriteLine($"[BB-ATTACK] phase=return_home status=pending action=surrender attempt={attempt} reason=screenshot_unavailable");
                        if (Sleep(1000, token)) return false;
                        continue;
                    }

                    Point surrenderPoint = new(
                        (int)Math.Round(65 * screenshot.Width / 860.0),
                        (int)Math.Round(540 * screenshot.Height / 732.0));

                    _adb.Tap(surrenderPoint.X, surrenderPoint.Y);
                    Console.WriteLine($"[BB-ATTACK] phase=return_home status=pending action=surrender attempt={attempt} point=({surrenderPoint.X},{surrenderPoint.Y})");
                    if (Sleep(1000, token)) return false;
                    continue;
                }

                if (_entryFlow.TapFirstVisible(BuilderBaseAttackLayout.SurrenderTemplates, 0.52, null, token, out string surrenderTemplate))
                {
                    Console.WriteLine($"[BB-ATTACK] phase=return_home status=pending template=\"{surrenderTemplate}\" attempt={attempt}");
                    if (Sleep(1000, token)) return false;
                }

                if (_entryFlow.TapFirstVisible(BuilderBaseAttackLayout.ReturnHomeTemplates, 0.45, BuilderBaseAttackLayout.ResultRoi, token, out string returnTemplate))
                {
                    if (IsBonusOrChallengeTemplate(returnTemplate))
                    {
                        Console.WriteLine($"[BB-ATTACK] phase=bonus status=detected template=\"{returnTemplate}\" action=acknowledge");
                    }

                    Console.WriteLine($"[BB-ATTACK] phase=return_home status=pending template=\"{returnTemplate}\" attempt={attempt}");
                    if (Sleep(1800, token)) return false;
                    if (_navigator.IsOnBuilderBase())
                    {
                        Console.WriteLine("[BB-ATTACK] phase=return_home status=success reason=builder_base_detected");
                        return true;
                    }
                }

                if (_entryFlow.TapFirstVisible(BuilderBaseAttackLayout.SurrenderConfirmTemplates, 0.52, BuilderBaseAttackLayout.ResultRoi, token, out string confirmTemplate))
                {
                    Console.WriteLine($"[BB-ATTACK] phase=surrender_confirm status=pending template=\"{confirmTemplate}\" attempt={attempt}");
                    if (Sleep(1800, token)) return false;
                }
            }

            Console.WriteLine("[BB-ATTACK] phase=return_home status=fail reason=not_returned_after_attempts");
            return _navigator.IsOnBuilderBase();
        }

        private static bool IsBonusOrChallengeTemplate(string template)
        {
            return template.IndexOf("bonus", StringComparison.OrdinalIgnoreCase) >= 0
                || template.IndexOf("challenge", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static bool Sleep(int milliseconds, CancellationToken token) => token.WaitHandle.WaitOne(milliseconds);
    }
}
