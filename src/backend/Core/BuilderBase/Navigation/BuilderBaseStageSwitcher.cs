using System;
using System.Linq;
using System.Threading;
using OpenCvSharp;
using Point = OpenCvSharp.Point;

namespace CvAut
{
    /// <summary>
    /// Moves between Builder Base stages (stage 1 and the Otto/O.T.T.O village) through the tunnel.
    /// </summary>
    internal sealed class BuilderBaseStageSwitcher
    {
        private readonly IVillageSwitchIO _io;
        private readonly VillagePresenceDetector _detector;
        private readonly Func<int, CancellationToken, bool> _sleep;
        private readonly Func<bool> _isOnBuilderBase;
        private readonly Action<CancellationToken> _zoomOut;

        internal BuilderBaseStageSwitcher(
            IVillageSwitchIO io,
            VillagePresenceDetector detector,
            Func<int, CancellationToken, bool> sleep,
            Func<bool> isOnBuilderBase,
            Action<CancellationToken> zoomOut)
        {
            _io = io;
            _detector = detector;
            _sleep = sleep;
            _isOnBuilderBase = isOnBuilderBase;
            _zoomOut = zoomOut;
        }

        internal bool Switch(string targetStage, int fallbackX, int fallbackY, CancellationToken token)
        {
            BuilderBaseNavigationLog.Write("switch_stage", "start", targetStage);
            if (!_isOnBuilderBase())
            {
                BuilderBaseNavigationLog.Write("switch_stage", "fail", targetStage, null, "reason=not_on_builder_base");
                return false;
            }

            for (int attempt = 1; attempt <= 3 && !token.IsCancellationRequested; attempt++)
            {
                _zoomOut(token);

                using Mat? screenshot = _io.TakeScreenshot();
                if (screenshot == null || screenshot.Empty()) return false;

                if (IsOnBuilderBaseStage(screenshot, targetStage, out string currentMarker, out double currentScore))
                {
                    BuilderBaseNavigationLog.Write("switch_stage", "success", targetStage, attempt, $"reason=already_there marker=\"{currentMarker}\" score={currentScore:F2}");
                    return true;
                }

                if (TryTapStageTunnel(screenshot, targetStage, attempt))
                {
                    if (_sleep(2600, token)) return false;
                }
                else
                {
                    // MBR SwitchToBuilderBase clicks BBTunnel/OOTunnel with offsets. Until those PNGs exist,
                    // use the old coordinate fallback but mark it as unverified-template in logs.
                    BuilderBaseNavigationLog.Write("switch_stage", "fallback_tap", targetStage, attempt, $"reason=tunnel_template_not_found x={fallbackX} y={fallbackY}");
                    _io.Tap(fallbackX, fallbackY);
                    if (_sleep(2600, token)) return false;
                }

                _zoomOut(token);
                using Mat? verification = _io.TakeScreenshot();
                if (verification != null && !verification.Empty() && IsOnBuilderBaseStage(verification, targetStage, out string marker, out double markerScore))
                {
                    BuilderBaseNavigationLog.Write("switch_stage", "success", targetStage, attempt, $"marker=\"{marker}\" score={markerScore:F2}");
                    return true;
                }

                BuilderBaseNavigationLog.Write("switch_stage", "retry", targetStage, attempt, "reason=target_stage_not_detected");
            }

            BuilderBaseNavigationLog.Write("switch_stage", "fail", targetStage, null, "reason=not_detected_after_attempts");
            return false;
        }

        private bool IsOnBuilderBaseStage(Mat screenshot, string targetStage, out string marker, out double score)
        {
            string[] templates = targetStage.Equals("otto", StringComparison.OrdinalIgnoreCase)
                ? BuilderBaseNavigationLayout.OttoStageTemplates
                : BuilderBaseNavigationLayout.BuilderBaseStage1Templates;
            return _detector.TryFindAny(screenshot, templates, BuilderBaseNavigationLayout.BuilderBaseThreshold, BuilderBaseNavigationLayout.BuilderBaseMarkerRoi, out marker, out score, out _);
        }

        private bool TryTapStageTunnel(Mat screenshot, string targetStage, int attempt)
        {
            string targetMarker = targetStage.Equals("otto", StringComparison.OrdinalIgnoreCase) ? "otto" : "builder";
            foreach (string template in BuilderBaseNavigationLayout.StageTunnelTemplates
                .Where(template => template.Contains(targetMarker, StringComparison.OrdinalIgnoreCase)
                    || (!template.Contains("otto", StringComparison.OrdinalIgnoreCase)
                        && !template.Contains("builder", StringComparison.OrdinalIgnoreCase))))
            {
                Point? center = _io.FindElement(screenshot, template, BuilderBaseNavigationLayout.SwitchButtonThreshold, BuilderBaseNavigationLayout.StageTunnelRoi, out double score);
                if (center == null) continue;

                int offsetX = template.Contains("otto", StringComparison.OrdinalIgnoreCase) ? -45 : -40;
                int offsetY = template.Contains("otto", StringComparison.OrdinalIgnoreCase) ? 15 : 25;
                int tapX = Math.Clamp(center.Value.X + offsetX, 0, 1599);
                int tapY = Math.Clamp(center.Value.Y + offsetY, 0, 899);
                BuilderBaseNavigationLog.Write("switch_stage", "tap_tunnel", targetStage, attempt, $"template={template} score={score:F2} center=({center.Value.X},{center.Value.Y}) tap=({tapX},{tapY})");
                _io.Tap(tapX, tapY);
                return true;
            }

            return false;
        }
    }
}
