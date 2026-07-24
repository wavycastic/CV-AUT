using System;
using System.Collections.Generic;
using System.Threading;
using CvAut;
using OpenCvSharp;
using Xunit;
using Point = OpenCvSharp.Point;

namespace CvAut.Backend.Tests
{
    public class BuilderBaseNavigatorFlowTests
    {
        [Fact]
        public void SwitchToBuilderBase_TapsTemplateAndVerifiesTarget()
        {
            var io = new FakeVillageSwitchIO(VillageState.MainVillage);
            var navigator = new BuilderBaseNavigator(io, NoSleep);

            bool result = navigator.SwitchToBuilderBase(CancellationToken.None);

            Assert.True(result);
            Assert.Equal(VillageState.BuilderBase, io.State);
            Assert.Contains(io.Taps, tap => tap == new Point(150, 690));
            Assert.True(io.PinchCount >= 1);
        }

        [Fact]
        public void SwitchToMainVillage_UsesMainVillageRoiAndVerifiesNotBuilderBase()
        {
            var io = new FakeVillageSwitchIO(VillageState.BuilderBase);
            var navigator = new BuilderBaseNavigator(io, NoSleep);

            bool result = navigator.SwitchToMainVillage(CancellationToken.None);

            Assert.True(result);
            Assert.Equal(VillageState.MainVillage, io.State);
            Assert.Contains(io.Rois, roi => roi == Rect.FromLTRB(0, 35, 260, 170));
            Assert.Contains(io.Taps, tap => tap == new Point(1160, 210));
        }

        [Fact]
        public void SwitchToBuilderBase_RetriesWhenFirstVerifyMisses()
        {
            var io = new FakeVillageSwitchIO(VillageState.MainVillage)
            {
                BuilderDetectionMissesAfterTap = 1
            };
            var navigator = new BuilderBaseNavigator(io, NoSleep);

            bool result = navigator.SwitchToBuilderBase(CancellationToken.None);

            Assert.True(result);
            Assert.Equal(VillageState.BuilderBase, io.State);
            Assert.True(io.ScreenshotCount >= 3);
        }

        [Fact]
        public void SwitchToBuilderBase_VerifiesWithMbrBuilderMarkerBeforeFallbackIcons()
        {
            var io = new FakeVillageSwitchIO(VillageState.MainVillage)
            {
                DisableBuilderFallbackIcons = true
            };
            var navigator = new BuilderBaseNavigator(io, NoSleep);

            bool result = navigator.SwitchToBuilderBase(CancellationToken.None);

            Assert.True(result);
            Assert.Contains("village\\Page\\BuilderBase\\BuilderEye_0_90", io.MatchedTemplates);
            Assert.DoesNotContain("ui\\builder_available", io.MatchedTemplates);
        }

        [Fact]
        public void SwitchToMainVillage_VerifiesWithMbrMainMarkerBeforePrimaryUi()
        {
            var io = new FakeVillageSwitchIO(VillageState.BuilderBase)
            {
                DisableMainPrimaryUi = true
            };
            var navigator = new BuilderBaseNavigator(io, NoSleep);

            bool result = navigator.SwitchToMainVillage(CancellationToken.None);

            Assert.True(result);
            Assert.Contains("village\\Page\\MainVillage\\MainVillage_100_90", io.MatchedTemplates);
        }

        [Fact]
        public void SwitchToOttoVillage_RequiresOttoStageMarker()
        {
            var io = new FakeVillageSwitchIO(VillageState.BuilderBase);
            var navigator = new BuilderBaseNavigator(io, NoSleep);

            bool result = navigator.SwitchToOttoVillage(CancellationToken.None);

            Assert.True(result);
            Assert.Equal(VillageState.Otto, io.State);
            Assert.Contains("village\\Page\\BuilderBase\\MachineEye_0_90", io.MatchedTemplates);
        }

        [Fact]
        public void SwitchToOttoVillage_FailsWhenTunnelTapDoesNotChangeStage()
        {
            var io = new FakeVillageSwitchIO(VillageState.BuilderBase) { IgnoreStageTap = true };
            var navigator = new BuilderBaseNavigator(io, NoSleep);

            bool result = navigator.SwitchToOttoVillage(CancellationToken.None);

            Assert.False(result);
            Assert.Equal(VillageState.BuilderBase, io.State);
        }

        private static bool NoSleep(int milliseconds, CancellationToken token) => token.IsCancellationRequested;

        private enum PendingTap
        {
            None,
            ToBuilder,
            ToMain,
            ToOtto,
            ToBuilderStage1
        }

        private sealed class FakeVillageSwitchIO : IVillageSwitchIO
        {
            private PendingTap _pendingTap;

            public FakeVillageSwitchIO(VillageState state)
            {
                State = state;
            }

            public VillageState State { get; private set; }
            public int BuilderDetectionMissesAfterTap { get; init; }
            public int BuilderDetectionMissesServed { get; private set; }
            public int ScreenshotCount { get; private set; }
            public int PinchCount { get; private set; }
            public bool DisableBuilderFallbackIcons { get; init; }
            public bool DisableMainPrimaryUi { get; init; }
            public bool IgnoreStageTap { get; init; }
            public List<Point> Taps { get; } = new();
            public List<Rect?> Rois { get; } = new();
            public List<string> MatchedTemplates { get; } = new();

            private int RemainingBuilderMisses { get; set; }

            public Mat? TakeScreenshot()
            {
                ScreenshotCount++;
                return new Mat(900, 1600, MatType.CV_8UC3, Scalar.Black);
            }

            public Point? FindElement(Mat screenshot, string templateName, double threshold, Rect? roi, out double score)
            {
                Rois.Add(roi);
                score = 0.95;

                if (templateName.EndsWith("switch_builder", StringComparison.OrdinalIgnoreCase) && State == VillageState.MainVillage)
                {
                    _pendingTap = PendingTap.ToBuilder;
                    return new Point(150, 690);
                }

                if ((templateName.EndsWith("return_home", StringComparison.OrdinalIgnoreCase)
                    || templateName.EndsWith("return_home_n", StringComparison.OrdinalIgnoreCase)
                    || templateName.EndsWith("home", StringComparison.OrdinalIgnoreCase))
                    && State == VillageState.BuilderBase)
                {
                    _pendingTap = PendingTap.ToMain;
                    return new Point(1160, 210);
                }

                if ((templateName.EndsWith("game_setting", StringComparison.OrdinalIgnoreCase)
                    || templateName.EndsWith("shop", StringComparison.OrdinalIgnoreCase))
                    && State == VillageState.MainVillage
                    && !DisableMainPrimaryUi)
                {
                    MatchedTemplates.Add(templateName);
                    return new Point(1500, 600);
                }

                if ((templateName.EndsWith("MainVillage_100_90", StringComparison.OrdinalIgnoreCase)
                    || templateName.EndsWith("GobBuilder_100_92", StringComparison.OrdinalIgnoreCase))
                    && State == VillageState.MainVillage)
                {
                    MatchedTemplates.Add(templateName);
                    return new Point(420, 660);
                }

                if (templateName.EndsWith("BuilderEye_0_90", StringComparison.OrdinalIgnoreCase)
                    && State == VillageState.BuilderBase)
                {
                    MatchedTemplates.Add(templateName);
                    return new Point(420, 660);
                }

                if (templateName.EndsWith("MachineEye_0_90", StringComparison.OrdinalIgnoreCase)
                    && State == VillageState.Otto)
                {
                    MatchedTemplates.Add(templateName);
                    return new Point(420, 660);
                }

                if (templateName.EndsWith("otto_tunnel", StringComparison.OrdinalIgnoreCase))
                {
                    _pendingTap = PendingTap.ToOtto;
                    return new Point(210, 170);
                }

                if (templateName.EndsWith("builder_tunnel", StringComparison.OrdinalIgnoreCase))
                {
                    _pendingTap = PendingTap.ToBuilderStage1;
                    return new Point(210, 170);
                }

                if ((templateName.EndsWith("builder_available", StringComparison.OrdinalIgnoreCase)
                    || templateName.EndsWith("x_night", StringComparison.OrdinalIgnoreCase))
                    && State == VillageState.BuilderBase
                    && !DisableBuilderFallbackIcons)
                {
                    if (RemainingBuilderMisses > 0)
                    {
                        RemainingBuilderMisses--;
                        BuilderDetectionMissesServed++;
                        return null;
                    }

                    MatchedTemplates.Add(templateName);
                    return new Point(100, 100);
                }

                score = 0;
                return null;
            }

            public void Tap(int x, int y)
            {
                Taps.Add(new Point(x, y));
                if (_pendingTap == PendingTap.ToBuilder)
                {
                    State = VillageState.BuilderBase;
                    RemainingBuilderMisses = BuilderDetectionMissesAfterTap;
                }
                else if (_pendingTap == PendingTap.ToMain)
                {
                    State = VillageState.MainVillage;
                }
                else if (_pendingTap == PendingTap.ToOtto && !IgnoreStageTap)
                {
                    State = VillageState.Otto;
                }
                else if (_pendingTap == PendingTap.ToBuilderStage1 && !IgnoreStageTap)
                {
                    State = VillageState.BuilderBase;
                }

                _pendingTap = PendingTap.None;
            }

            public void PinchInZoomOut(int count, int durationMs, int intervalMs)
            {
                PinchCount += count;
            }
        }
    }

    internal enum VillageState
    {
        MainVillage,
        BuilderBase,
        Otto
    }
}
