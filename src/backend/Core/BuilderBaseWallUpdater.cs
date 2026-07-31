using System;
using System.Collections.Generic;
using System.Threading;
using OpenCvSharp;
using Point = OpenCvSharp.Point;

namespace CvAut
{
    internal sealed record BuilderBaseWallUpgradeAttempt(bool UiConfirmed, string Resource, int Cost, string Reason)
    {
        public static BuilderBaseWallUpgradeAttempt Failed(string reason) => new(false, string.Empty, 0, reason);
    }

    /// <summary>
    /// Nâng tối đa một đoạn tường Builder Base mỗi cycle qua menu gợi ý thợ xây.
    /// Chỉ tap khi cùng resource/cost được xác nhận ở cả template ready và upgrade.
    /// </summary>
    internal sealed class BuilderBaseWallUpdater
    {
        private readonly IADBHelper _adb;
        private readonly IVisionEngine _vision;
        private readonly BuilderBaseNavigator _navigator;

        private const double ReadyThreshold = 0.70;
        private const double UpgradeThreshold = 0.72;

        /// <summary>
        /// Ngưỡng cho icon tường trong menu thợ xây. Đây là icon UI nên dùng mức thấp hơn
        /// template tường, ngang với ButtonThreshold của luồng đánh.
        /// </summary>
        private const double MenuIconThreshold = 0.62;

        private const int MaxWallCandidatesPerCycle = 4;
        private const string MenuIconTemplate = @"ui\icon_wall";
        private static readonly Point BuilderMenuPoint = new(738, 36);
        private static readonly Rect BuilderMenuRoi = Rect.FromLTRB(570, 70, 1060, 650);
        private static readonly Rect UpgradeButtonRoi = Rect.FromLTRB(420, 430, 1180, 850);
        private static readonly Point DismissPoint = new(140, 606);

        /// <summary>
        /// Tiền tố của template ready. Bộ asset đã hiệu chỉnh có nhiều biến thể ảnh cho cùng một
        /// mức giá (wall_/wall1_/wall2_/wall3_) ứng với các kiểu tường khác nhau hiện trong menu.
        /// </summary>
        private static readonly string[] ReadyTemplatePrefixes = { "wall", "wall1", "wall2", "wall3" };

        private static readonly WallOption[] Options =
        {
            new("gold", "1m"), new("gold", "800k"), new("gold", "600k"),
            new("gold", "400k"), new("gold", "240k"), new("gold", "150k"),
            new("gold", "50k"), new("gold", "10k"), new("gold", "2k"),
            new("elixir", "1m"), new("elixir", "800k"), new("elixir", "600k"),
            new("elixir", "400k")
        };

        public BuilderBaseWallUpdater(IADBHelper adb, IVisionEngine vision, BuilderBaseNavigator navigator)
        {
            _adb = adb;
            _vision = vision;
            _navigator = navigator;
        }

        /// <summary>
        /// Bộ template mà luồng nâng tường thực sự phụ thuộc, để asset audit kiểm đúng thứ cần kiểm.
        /// Chỉ gồm biến thể chuẩn "wall_" vì wall1_/wall2_/wall3_ là tùy chọn, không phải mức giá nào cũng có.
        /// </summary>
        public static IReadOnlyList<string> GetRequiredTemplates()
        {
            var templates = new List<string> { MenuIconTemplate };
            foreach (WallOption option in Options)
            {
                templates.Add(ReadyTemplate(option, "wall"));
                templates.Add(UpgradeTemplate(option));
            }

            return templates;
        }

        public BuilderBaseWallUpgradeAttempt TryUpgradeOne(CancellationToken token)
        {
            Console.WriteLine("[BB-WALL] phase=upgrade status=start limit=1");
            if (!_navigator.IsOnBuilderBase())
            {
                Console.WriteLine("[BB-WALL] phase=upgrade status=skip reason=not_on_builder_base");
                return BuilderBaseWallUpgradeAttempt.Failed("not_on_builder_base");
            }

            _adb.Tap(BuilderMenuPoint.X, BuilderMenuPoint.Y);
            if (Sleep(900, token)) return BuilderBaseWallUpgradeAttempt.Failed("cancelled");

            var exhausted = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            int candidatesTried = 0;
            while (true)
            {
                if (token.IsCancellationRequested) return BuilderBaseWallUpgradeAttempt.Failed("cancelled");

                if (candidatesTried >= MaxWallCandidatesPerCycle)
                {
                    Console.WriteLine($"[BB-WALL] phase=upgrade status=skip reason=max_candidates_reached tried={candidatesTried}");
                    Dismiss(token);
                    return BuilderBaseWallUpgradeAttempt.Failed("max_candidates_reached");
                }

                using Mat? menuShot = _adb.TakeScreenshot();
                if (menuShot == null || menuShot.Empty())
                {
                    Console.WriteLine("[BB-WALL] phase=builder_menu status=fail reason=screenshot_unavailable");
                    Dismiss(token);
                    return BuilderBaseWallUpgradeAttempt.Failed("screenshot_unavailable");
                }

                bool menuOpen = TryFind(menuShot, MenuIconTemplate, MenuIconThreshold, BuilderMenuRoi, out _, out _);
                if (!menuOpen)
                {
                    Console.WriteLine("[BB-WALL] phase=builder_menu status=fail reason=menu_not_open_after_tap");
                    Dismiss(token);
                    return BuilderBaseWallUpgradeAttempt.Failed("menu_not_open_after_tap");
                }

                if (!TryFindReadyCandidate(menuShot, exhausted, out WallOption? option, out string readyTemplate, out Point readyCenter, out double readyScore))
                {
                    Console.WriteLine("[BB-WALL] phase=upgrade status=skip reason=no_ready_wall_found");
                    Dismiss(token);
                    return BuilderBaseWallUpgradeAttempt.Failed("no_ready_wall_found");
                }

                candidatesTried++;
                exhausted.Add(CandidateKey(option!));
                Console.WriteLine($"[BB-WALL] phase=ready status=success resource={option!.Resource} cost={option.Cost} template=\"{readyTemplate}\" score={readyScore:F2} center=({readyCenter.X},{readyCenter.Y}) candidate={candidatesTried}");
                _adb.Tap(readyCenter.X, readyCenter.Y);
                if (Sleep(900, token)) return BuilderBaseWallUpgradeAttempt.Failed("cancelled");

                string upgradeTemplate = UpgradeTemplate(option);
                if (!TryFindOnFreshScreenshot(upgradeTemplate, UpgradeThreshold, UpgradeButtonRoi, out Point upgradeCenter, out double upgradeScore))
                {
                    Console.WriteLine($"[BB-WALL] phase=upgrade status=skip resource={option.Resource} cost={option.Cost} reason=matching_upgrade_button_not_found candidate={candidatesTried}");
                    Dismiss(token);
                    if (Sleep(300, token)) return BuilderBaseWallUpgradeAttempt.Failed("cancelled");
                    _adb.Tap(BuilderMenuPoint.X, BuilderMenuPoint.Y);
                    if (Sleep(500, token)) return BuilderBaseWallUpgradeAttempt.Failed("cancelled");
                    continue;
                }

                Console.WriteLine($"[BB-WALL] phase=upgrade status=pending resource={option.Resource} cost={option.Cost} score={upgradeScore:F2} center=({upgradeCenter.X},{upgradeCenter.Y})");
                _adb.Tap(upgradeCenter.X, upgradeCenter.Y);
                if (Sleep(1500, token)) return BuilderBaseWallUpgradeAttempt.Failed("cancelled");

                bool upgradeStillVisible = TryFindOnFreshScreenshot(upgradeTemplate, UpgradeThreshold, UpgradeButtonRoi, out _, out double afterScore);
                Dismiss(token);
                if (upgradeStillVisible)
                {
                    Console.WriteLine($"[BB-WALL] phase=upgrade status=uncertain resource={option.Resource} cost={option.Cost} reason=button_still_visible_after_tap score_after={afterScore:F2}");
                    return BuilderBaseWallUpgradeAttempt.Failed("button_still_visible_after_tap");
                }

                int cost = ParseCost(option.Cost);
                Console.WriteLine($"[BB-WALL] phase=upgrade status=pending resource={option.Resource} cost={cost} reason=await_resource_verification");
                return new(true, option.Resource, cost, "ui_confirmed");
            }
        }

        internal static bool VerifyResourceDelta(
            BuilderBaseWallUpgradeAttempt attempt,
            BuilderBaseReportSnapshot before,
            BuilderBaseReportSnapshot after,
            out int delta)
        {
            delta = 0;
            if (!attempt.UiConfirmed || attempt.Cost <= 0 || !before.Reliable || !after.Reliable) return false;

            int beforeValue = attempt.Resource.Equals("gold", StringComparison.OrdinalIgnoreCase) ? before.Gold : before.Elixir;
            int afterValue = attempt.Resource.Equals("gold", StringComparison.OrdinalIgnoreCase) ? after.Gold : after.Elixir;
            delta = beforeValue - afterValue;
            int tolerance = Math.Max(1000, attempt.Cost / 10);
            return delta >= attempt.Cost - tolerance && delta <= attempt.Cost + tolerance;
        }

        private bool TryFindReadyCandidate(
            Mat screenshot,
            HashSet<string> exhausted,
            out WallOption? option,
            out string template,
            out Point center,
            out double score)
        {
            foreach (WallOption candidate in Options)
            {
                if (exhausted.Contains(CandidateKey(candidate))) continue;
                foreach (string prefix in ReadyTemplatePrefixes)
                {
                    string readyTemplate = ReadyTemplate(candidate, prefix);
                    if (!TryFind(screenshot, readyTemplate, ReadyThreshold, BuilderMenuRoi, out center, out score)) continue;
                    option = candidate;
                    template = readyTemplate;
                    return true;
                }
            }

            option = null;
            template = string.Empty;
            center = default;
            score = 0;
            return false;
        }

        private static string CandidateKey(WallOption option) => $"{option.Resource}:{option.Cost}";

        private static string ReadyTemplate(WallOption option, string prefix)
            => $@"walls\builder_hall\{option.Resource}\ready\{prefix}_ready_{option.Resource}{option.Cost}";

        private static string UpgradeTemplate(WallOption option)
            => $@"walls\builder_hall\{option.Resource}\upgrade\{option.Resource}_wall_upgrade{option.Cost}";

        private static int ParseCost(string value)
        {
            if (value.EndsWith("m", StringComparison.OrdinalIgnoreCase)
                && int.TryParse(value[..^1], out int millions)) return millions * 1_000_000;
            if (value.EndsWith("k", StringComparison.OrdinalIgnoreCase)
                && int.TryParse(value[..^1], out int thousands)) return thousands * 1_000;
            return int.TryParse(value, out int exact) ? exact : 0;
        }

        private bool TryFindOnFreshScreenshot(string template, double threshold, Rect roi, out Point center, out double score)
        {
            center = default;
            score = 0;
            using Mat? screenshot = _adb.TakeScreenshot();
            if (screenshot == null || screenshot.Empty()) return false;

            return TryFind(screenshot, template, threshold, roi, out center, out score);
        }

        private bool TryFind(Mat screenshot, string template, double threshold, Rect roi, out Point center, out double score)
        {
            center = default;
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
