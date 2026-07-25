using System;
using System.Threading;
using OpenCvSharp;
using Point = OpenCvSharp.Point;

namespace CvAut
{
    /// <summary>
    /// Điều hướng Làng đêm / Builder Base.
    /// Phase đầu chỉ làm nhiệm vụ nhận diện và chuyển làng, port lại ý tưởng từ MBR
    /// nhưng dùng ADB + VisionEngine của dự án hiện tại.
    /// </summary>
    internal sealed class BuilderBaseNavigator
    {
        private readonly IVillageSwitchIO _io;
        private readonly Func<int, CancellationToken, bool> _sleep;
        private readonly VillagePresenceDetector _detector;
        private readonly BuilderBaseStageSwitcher _stageSwitcher;

        public BuilderBaseNavigator(IADBHelper adb, IVisionEngine vision)
            : this(new VillageSwitchIO(adb, vision))
        {
        }

        internal BuilderBaseNavigator(IVillageSwitchIO io)
            : this(io, Sleep)
        {
        }

        internal BuilderBaseNavigator(IVillageSwitchIO io, Func<int, CancellationToken, bool> sleep)
        {
            _io = io;
            _sleep = sleep;
            _detector = new VillagePresenceDetector(io);
            _stageSwitcher = new BuilderBaseStageSwitcher(io, _detector, sleep, IsOnBuilderBase, ZoomOutApprox);
        }

        public bool IsOnBuilderBase()
        {
            using Mat? screenshot = _io.TakeScreenshot();
            if (screenshot == null || screenshot.Empty())
            {
                BuilderBaseNavigationLog.Write("detect", "fail", "builder_base", null, "reason=screenshot_empty");
                return false;
            }

            return _detector.IsOnBuilderBase(screenshot, log: true);
        }

        public bool IsOnMainVillage()
        {
            using Mat? screenshot = _io.TakeScreenshot();
            if (screenshot == null || screenshot.Empty())
            {
                BuilderBaseNavigationLog.Write("detect", "fail", "main_village", null, "reason=screenshot_empty");
                return false;
            }

            return _detector.IsOnMainVillage(screenshot, log: true);
        }

        public bool SwitchToBuilderBase(CancellationToken token)
        {
            BuilderBaseNavigationLog.Write("switch", "start", "builder_base");
            if (IsOnBuilderBase())
            {
                BuilderBaseNavigationLog.Write("switch", "success", "builder_base", null, "reason=already_there");
                return true;
            }

            for (int attempt = 1; attempt <= BuilderBaseNavigationLayout.SwitchAttempts && !token.IsCancellationRequested; attempt++)
            {
                // MBR luôn ZoomOut trước khi tìm thuyền để đảm bảo boat/tunnel nằm trong viewport.
                BuilderBaseNavigationLog.Write("switch", "pending", "builder_base", attempt, "action=zoom_out");
                ZoomOutApprox(token);
                if (_sleep(500, token)) return false;

                using Mat? screenshot = _io.TakeScreenshot();
                if (screenshot == null || screenshot.Empty())
                {
                    BuilderBaseNavigationLog.Write("switch", "fail", "builder_base", attempt, "reason=screenshot_empty");
                    return false;
                }

                if (_detector.IsOnBuilderBase(screenshot, log: false))
                {
                    BuilderBaseNavigationLog.Write("switch", "success", "builder_base", attempt, "reason=detected_after_zoom");
                    return true;
                }

                if (_detector.TryTapFirst(screenshot, BuilderBaseNavigationLayout.SwitchToBuilderTemplates, BuilderBaseNavigationLayout.SwitchButtonThreshold, BuilderBaseNavigationLayout.SwitchButtonRoi, out string matched, out double score, out Point tapPoint))
                {
                    BuilderBaseNavigationLog.Write("switch", "tap_switch", "builder_base", attempt, $"template={matched} score={score:F2} tap=({tapPoint.X},{tapPoint.Y}) roi=switch_button");
                }
                else
                {
                    // Fallback: tap tọa độ cố định thuyền ở bờ trái dưới
                    BuilderBaseNavigationLog.Write("switch", "fallback_tap", "builder_base", attempt, "reason=boat_template_not_found x=150 y=690");
                    _io.Tap(150, 690);
                }

                if (WaitForVillage("builder_base", BuilderBaseNavigationLayout.SwitchVerifyTimeoutMs, token, attempt))
                {
                    BuilderBaseNavigationLog.Write("switch", "success", "builder_base", attempt);
                    return true;
                }

                BuilderBaseNavigationLog.Write("switch", "fail", "builder_base", attempt, "reason=verify_timeout");
            }

            BuilderBaseNavigationLog.Write("switch", "fail", "builder_base", null, $"reason=not_detected_after_attempts attempts={BuilderBaseNavigationLayout.SwitchAttempts}");
            return false;
        }

        public bool SwitchToMainVillage(CancellationToken token)
        {
            BuilderBaseNavigationLog.Write("switch", "start", "main_village");
            if (IsOnMainVillage() && !IsOnBuilderBase())
            {
                BuilderBaseNavigationLog.Write("switch", "success", "main_village", null, "reason=already_there");
                return true;
            }

            for (int attempt = 1; attempt <= BuilderBaseNavigationLayout.SwitchAttempts && !token.IsCancellationRequested; attempt++)
            {
                BuilderBaseNavigationLog.Write("switch", "pending", "main_village", attempt, "action=zoom_out");
                ZoomOutApprox(token);
                if (_sleep(500, token)) return false;

                using Mat? screenshot = _io.TakeScreenshot();
                if (screenshot == null || screenshot.Empty())
                {
                    BuilderBaseNavigationLog.Write("switch", "fail", "main_village", attempt, "reason=screenshot_empty");
                    return false;
                }

                if (_detector.IsOnMainVillage(screenshot, log: false) && !_detector.IsOnBuilderBase(screenshot, log: false))
                {
                    BuilderBaseNavigationLog.Write("switch", "success", "main_village", attempt, "reason=detected_after_zoom");
                    return true;
                }

                if (_detector.TryTapFirst(screenshot, BuilderBaseNavigationLayout.SwitchToMainTemplates, BuilderBaseNavigationLayout.SwitchButtonThreshold, BuilderBaseNavigationLayout.SwitchToMainButtonRoi, out string matched, out double score, out Point tapPoint))
                {
                    BuilderBaseNavigationLog.Write("switch", "tap_switch", "main_village", attempt, $"template={matched} score={score:F2} tap=({tapPoint.X},{tapPoint.Y}) roi=switch_button");
                }
                else
                {
                    Point fallback = GetMainVillageFallbackTap(attempt);
                    BuilderBaseNavigationLog.Write("switch", "fallback_tap", "main_village", attempt, $"reason=boat_template_not_found x={fallback.X} y={fallback.Y}");
                    _io.Tap(fallback.X, fallback.Y);
                }

                if (WaitForVillage("main_village", BuilderBaseNavigationLayout.SwitchVerifyTimeoutMs, token, attempt))
                {
                    BuilderBaseNavigationLog.Write("switch", "success", "main_village", attempt);
                    return true;
                }

                BuilderBaseNavigationLog.Write("switch", "fail", "main_village", attempt, "reason=verify_timeout");
            }

            BuilderBaseNavigationLog.Write("switch", "fail", "main_village", null, $"reason=not_detected_after_attempts attempts={BuilderBaseNavigationLayout.SwitchAttempts}");
            return false;
        }

        public bool SwitchToOttoVillage(CancellationToken token)
        {
            return _stageSwitcher.Switch(
                targetStage: "otto",
                fallbackX: 210,
                fallbackY: 170,
                token);
        }

        public bool SwitchToBuilderBaseStage1(CancellationToken token)
        {
            return _stageSwitcher.Switch(
                targetStage: "builder_base",
                fallbackX: 210,
                fallbackY: 170,
                token);
        }

        private bool WaitForVillage(string targetVillage, int timeoutMs, CancellationToken token, int attempt)
        {
            DateTime deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
            BuilderBaseNavigationLog.Write("switch", "pending", targetVillage, attempt, $"action=verify_switch timeout_ms={timeoutMs} poll_ms={BuilderBaseNavigationLayout.SwitchPollIntervalMs}");

            bool lastOnBuilderBase = false;
            bool lastOnMainVillage = false;

            while (DateTime.UtcNow < deadline && !token.IsCancellationRequested)
            {
                if (_sleep(BuilderBaseNavigationLayout.SwitchPollIntervalMs, token)) return false;

                using Mat? screenshot = _io.TakeScreenshot();
                if (screenshot == null || screenshot.Empty()) continue;

                bool onBuilderBase = _detector.IsOnBuilderBase(screenshot, log: false);
                bool onMainVillage = _detector.IsOnMainVillage(screenshot, log: false);
                lastOnBuilderBase = onBuilderBase;
                lastOnMainVillage = onMainVillage;

                if (targetVillage == "builder_base" && onBuilderBase)
                {
                    return true;
                }

                if (targetVillage == "main_village" && onMainVillage && !onBuilderBase)
                {
                    return true;
                }
            }

            using (Mat? screenshot = _io.TakeScreenshot())
            {
                if (screenshot != null && !screenshot.Empty())
                {
                    NavigationDebugRecorder.SaveDebugScreenshot(screenshot, $"{targetVillage}_verify_timeout_attempt_{attempt}");
                }
            }

            BuilderBaseNavigationLog.Write("switch", "fail", targetVillage, attempt, $"reason=verify_timeout last_builder_base={lastOnBuilderBase} last_main_village={lastOnMainVillage}");

            return false;
        }

        private static Point GetMainVillageFallbackTap(int attempt)
        {
            return attempt switch
            {
                1 => new Point(1160, 210),
                2 => new Point(1150, 196),
                3 => new Point(1136, 184),
                _ => new Point(1120, 172),
            };
        }

        public void ZoomOutApprox(CancellationToken token)
        {
            if (token.IsCancellationRequested) return;
            _io.PinchInZoomOut(count: 2, durationMs: 400, intervalMs: 300);
            _sleep(500, token);
        }

        private static bool Sleep(int milliseconds, CancellationToken token)
        {
            return token.WaitHandle.WaitOne(milliseconds);
        }
    }
}
