using System;
using System.Threading;
using OpenCvSharp;
using Point = OpenCvSharp.Point;

namespace CvAut
{
    /// <summary>
    /// Nâng tối đa một đoạn tường Builder Base mỗi cycle qua menu gợi ý thợ xây.
    /// Chỉ tap khi cùng resource/cost được xác nhận ở cả template ready và upgrade.
    /// </summary>
    internal sealed class BuilderBaseWallUpdater
    {
        private readonly ADBHelper _adb;
        private readonly VisionEngine _vision;
        private readonly BuilderBaseNavigator _navigator;

        private const double ReadyThreshold = 0.70;
        private const double UpgradeThreshold = 0.72;
        private const int MaxWallCandidatesPerCycle = 4;
        private static readonly Point BuilderMenuPoint = new(738, 36);
        private static readonly Rect BuilderMenuRoi = Rect.FromLTRB(570, 70, 1060, 650);
        private static readonly Rect UpgradeButtonRoi = Rect.FromLTRB(420, 430, 1180, 850);
        private static readonly Point DismissPoint = new(140, 606);

        private static readonly WallOption[] Options =
        {
            new("gold", "1m"), new("gold", "800k"), new("gold", "600k"),
            new("gold", "400k"), new("gold", "240k"), new("gold", "150k"),
            new("gold", "50k"), new("gold", "10k"), new("gold", "2k"),
            new("elixir", "1m"), new("elixir", "800k"), new("elixir", "600k"),
            new("elixir", "400k")
        };

        public BuilderBaseWallUpdater(ADBHelper adb, VisionEngine vision, BuilderBaseNavigator navigator)
        {
            _adb = adb;
            _vision = vision;
            _navigator = navigator;
        }

        public bool TryUpgradeOne(CancellationToken token)
        {
            Console.WriteLine("[BB-WALL] phase=upgrade status=start limit=1");
            if (!_navigator.IsOnBuilderBase())
            {
                Console.WriteLine("[BB-WALL] phase=upgrade status=skip reason=not_on_builder_base");
                return false;
            }

            _adb.Tap(BuilderMenuPoint.X, BuilderMenuPoint.Y);
            if (Sleep(900, token)) return false;

            if (!IsBuilderMenuLikelyOpen())
            {
                Console.WriteLine("[BB-WALL] phase=builder_menu status=fail reason=menu_not_open_after_tap");
                Dismiss(token);
                return false;
            }

            int candidatesTried = 0;
            foreach (WallOption option in Options)
            {
                if (candidatesTried >= MaxWallCandidatesPerCycle)
                {
                    Console.WriteLine($"[BB-WALL] phase=upgrade status=skip reason=max_candidates_reached tried={candidatesTried}");
                    break;
                }
                string readyTemplate = $@"walls\builder_hall\{option.Resource}\ready\wall_ready_{option.Resource}{option.Cost}";
                if (!TryFind(readyTemplate, ReadyThreshold, BuilderMenuRoi, out Point readyCenter, out double readyScore))
                {
                    continue;
                }

                candidatesTried++;
                Console.WriteLine($"[BB-WALL] phase=ready status=success resource={option.Resource} cost={option.Cost} score={readyScore:F2} center=({readyCenter.X},{readyCenter.Y}) candidate={candidatesTried}");
                _adb.Tap(readyCenter.X, readyCenter.Y);
                if (Sleep(900, token)) return false;

                string upgradeTemplate = $@"walls\builder_hall\{option.Resource}\upgrade\{option.Resource}_wall_upgrade{option.Cost}";
                if (!TryFind(upgradeTemplate, UpgradeThreshold, UpgradeButtonRoi, out Point upgradeCenter, out double upgradeScore))
                {
                    Console.WriteLine($"[BB-WALL] phase=upgrade status=skip resource={option.Resource} cost={option.Cost} reason=matching_upgrade_button_not_found candidate={candidatesTried}");
                    Dismiss(token);
                    if (Sleep(300, token)) return false;
                    _adb.Tap(BuilderMenuPoint.X, BuilderMenuPoint.Y);
                    if (Sleep(500, token)) return false;
                    continue;
                }

                Console.WriteLine($"[BB-WALL] phase=upgrade status=pending resource={option.Resource} cost={option.Cost} score={upgradeScore:F2} center=({upgradeCenter.X},{upgradeCenter.Y})");
                _adb.Tap(upgradeCenter.X, upgradeCenter.Y);
                if (Sleep(1500, token)) return false;

                bool upgradeStillVisible = TryFind(upgradeTemplate, UpgradeThreshold, UpgradeButtonRoi, out _, out double afterScore);
                Dismiss(token);
                if (upgradeStillVisible)
                {
                    Console.WriteLine($"[BB-WALL] phase=upgrade status=uncertain resource={option.Resource} cost={option.Cost} reason=button_still_visible_after_tap score_after={afterScore:F2}");
                    return false;
                }

                Console.WriteLine($"[BB-WALL] phase=upgrade status=success resource={option.Resource} cost={option.Cost} count=1");
                return true;
            }

            Console.WriteLine("[BB-WALL] phase=upgrade status=skip reason=no_ready_wall_found");
            Dismiss(token);
            return false;
        }

        private bool IsBuilderMenuLikelyOpen()
        {
            foreach (WallOption option in Options)
            {
                string readyTemplate = $@"walls\builder_hall\{option.Resource}\ready\wall_ready_{option.Resource}{option.Cost}";
                if (TryFind(readyTemplate, ReadyThreshold - 0.08, BuilderMenuRoi, out _, out _)) return true;
            }
            return false;
        }

        private bool TryFind(string template, double threshold, Rect roi, out Point center, out double score)
        {
            center = default;
            score = 0;
            using Mat? screenshot = _adb.TakeScreenshot();
            if (screenshot == null || screenshot.Empty()) return false;

            Point? found = _vision.FindElement(screenshot, template, threshold, roi, out score);
            if (found == null) return false;
            center = found.Value;
            return true;
        }

        private void Dismiss(CancellationToken token)
        {
            if (token.IsCancellationRequested) return;
            _adb.Tap(DismissPoint.X, DismissPoint.Y);
            Sleep(500, token);
        }

        private static bool Sleep(int milliseconds, CancellationToken token)
        {
            return token.WaitHandle.WaitOne(milliseconds);
        }

        private sealed record WallOption(string Resource, string Cost);
    }
}
