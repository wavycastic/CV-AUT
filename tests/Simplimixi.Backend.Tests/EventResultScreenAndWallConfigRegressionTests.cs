using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading;
using CvAut.Automation;
using OpenCvSharp;
using Xunit;

namespace CvAut.Backend.Tests
{
    public class EventResultScreenAndWallConfigRegressionTests
    {
        private sealed class TestAdbHelper : IADBHelper
        {
            private readonly Mat _screenshot;
            public List<Point> TappedPoints { get; } = new();

            public TestAdbHelper(Mat screenshot)
            {
                _screenshot = screenshot;
            }

            public string Host => "127.0.0.1";
            public int Port => 5555;
            public string DeviceAddress => "127.0.0.1:5555";
            public FramePacer FramePacer { get; } = new FramePacer();
            public Func<bool>? BeforeInputAction { get; set; }

            public bool IsDeviceConnected() => true;
            public bool EnsureConnectedOnline(int timeoutSeconds = 30) => true;
            public string GetDeviceState() => "device";
            public string ExecuteShell(string command) => string.Empty;

            public void Tap(int x, int y) { TappedPoints.Add(new Point(x, y)); }
            public void TapSequence(IEnumerable<Point> points) { TappedPoints.AddRange(points); }
            public void TapSequenceSafeFast(IEnumerable<Point> points, int batchSize = 4, int batchDelayMs = 90) { TappedPoints.AddRange(points); }
            public void TapSequenceSafeFast(IEnumerable<Point> points, int batchSize, int batchDelayMs, CancellationToken token) { TappedPoints.AddRange(points); }
            public void Swipe(int startX, int startY, int endX, int endY, int durationMs = 300) { }

            public Mat? TakeScreenshot() => _screenshot.Clone();
            public void PinchIn(int centerX = 800, int centerY = 450) { }
            public bool PinchInZoomOut(int count = 5, int durationMs = 450, int intervalMs = 350) => true;
            public void Dispose() { }
        }

        [Fact]
        public void Issue1_EventResultScreen_ClaimRewardButton_MatchedCorrectly_AndActiveBattleFalse()
        {
            string imagePath = Path.Combine(AppContext.BaseDirectory, "Fixtures", "Screenshot_2026.08.01_21.22.10.457.png");
            if (!File.Exists(imagePath))
            {
                imagePath = Path.Combine(Directory.GetCurrentDirectory(), "tests", "Simplimixi.Backend.Tests", "Fixtures", "Screenshot_2026.08.01_21.22.10.457.png");
            }
            if (!File.Exists(imagePath))
            {
                imagePath = @"E:\Projects\CV-AUT\tests\Simplimixi.Backend.Tests\Fixtures\Screenshot_2026.08.01_21.22.10.457.png";
            }

            using Mat screenshot = FixtureLoader.LoadMandatory(imagePath);

            string templatesDir = Path.Combine(AppContext.BaseDirectory, "assets", "Templates");
            if (!Directory.Exists(templatesDir))
            {
                templatesDir = Path.Combine(Directory.GetCurrentDirectory(), "assets", "Templates");
            }
            if (!Directory.Exists(templatesDir))
            {
                templatesDir = @"E:\Projects\CV-AUT\assets\Templates";
            }

            IVisionEngine vision = new VisionEngine(templatesDir);

            // 1. Verify active battle is NOT falsely present
            bool isActiveBattle = BattleScreenDetector.IsActiveBattlePresent(vision, screenshot, out double activeBattleScore);
            Assert.False(isActiveBattle);
            Assert.Equal(0, activeBattleScore);

            // 2. Verify continue button matcher finds claim_reward with high score & correct center
            bool foundContinue = BattleScreenDetector.TryFindContinueButton(
                vision,
                screenshot,
                out Point center,
                out double continueScore,
                out string matchedTemplate);

            Assert.True(foundContinue);
            Assert.True(continueScore >= AutomationThresholds.ResultContinueThreshold, $"Expected score >= {AutomationThresholds.ResultContinueThreshold}, got {continueScore}");
            Assert.Contains("claim_reward", matchedTemplate);
            Assert.InRange(center.X, 750, 850);
            Assert.InRange(center.Y, 740, 800);

            // 3. Verify BattleCompletionWatcher and BattleResultDetector recognize result screen
            using var testAdb = new TestAdbHelper(screenshot);
            PopupHandlerService popups = new PopupHandlerService(testAdb, vision, templatesDir);
            var completionWatcher = new BattleCompletionWatcher(testAdb, vision, popups);
            bool battleEnded1 = completionWatcher.BattleEnded(out string matchInfo1);

            Assert.True(battleEnded1);
            Assert.Contains("claim_reward", matchInfo1);

            var resultDetector = new BattleResultDetector(testAdb, vision, () => false);
            bool battleEnded2 = resultDetector.BattleEnded(out string matchInfo2);

            Assert.True(battleEnded2);
            Assert.Contains("claim_reward", matchInfo2);
        }

        [Fact]
        public void Issue2_WallConfig_RootActiveConfigPrecedence_OverLegacyVillageProfile()
        {
            // Scenario 1: Root HAS upgrade_wall = true, profile Village_1 HAS upgrade_wall = false
            // Root active config must win!
            string jsonRootTrue = """
            {
              "upgrade_wall": true,
              "wall_gold_threshold": 7000000,
              "wall_elixir_threshold": 6000000,
              "wall_gold_reserve": 200000,
              "wall_elixir_reserve": 50000,
              "wall_batch_limit": 3
            }
            """;
            using JsonDocument docTrue = JsonDocument.Parse(jsonRootTrue);
            var wallConfigTrue = ConfigService.GetWallUpgradeConfig(docTrue.RootElement, 1);

            Assert.True(wallConfigTrue.Enabled);
            Assert.Equal(7000000, wallConfigTrue.GoldThreshold);
            Assert.Equal(6000000, wallConfigTrue.ElixirThreshold);
            Assert.Equal(200000, wallConfigTrue.GoldReserve);
            Assert.Equal(50000, wallConfigTrue.ElixirReserve);
            Assert.Equal(3, wallConfigTrue.BatchLimit);

            // Scenario 2: Root HAS upgrade_wall = false (explicitly set by user to false)
            string jsonRootFalse = """
            {
              "upgrade_wall": false,
              "wall_batch_limit": 2
            }
            """;
            using JsonDocument docFalse = JsonDocument.Parse(jsonRootFalse);
            var wallConfigFalse = ConfigService.GetWallUpgradeConfig(docFalse.RootElement, 1);

            Assert.False(wallConfigFalse.Enabled);
            Assert.Equal(2, wallConfigFalse.BatchLimit);

            // Scenario 3: Root MISSING upgrade_wall property, profile HAS upgrade_wall = false in Village_1.json
            // Should fallback to legacy profile value
            string jsonRootMissing = """
            {
              "farming_thresholds": { "gold_threshold": 500000 }
            }
            """;
            using JsonDocument docMissing = JsonDocument.Parse(jsonRootMissing);
            var wallConfigMissing = ConfigService.GetWallUpgradeConfig(docMissing.RootElement, 1);

            // Village_1.json has upgrade_wall: false
            Assert.False(wallConfigMissing.Enabled);
        }

        [Fact]
        public void Issue3_ClaimRewardFlow_ExecutesFiveSteps_AndHitsFailsafeWhenContinueTimeout()
        {
            using Mat dummyMat = new Mat(900, 1600, MatType.CV_8UC3, new Scalar(0, 0, 0));
            using var testAdb = new TestAdbHelper(dummyMat);

            string templatesDir = Path.Combine(AppContext.BaseDirectory, "assets", "Templates");
            if (!Directory.Exists(templatesDir))
            {
                templatesDir = @"E:\Projects\CV-AUT\assets\Templates";
            }
            IVisionEngine vision = new VisionEngine(templatesDir);
            PopupHandlerService popups = new PopupHandlerService(testAdb, vision, templatesDir);
            var completionWatcher = new BattleCompletionWatcher(testAdb, vision, popups);

            Point matchCenter = new Point(800, 770);
            bool success = completionWatcher.HandleClaimRewardFlow(matchCenter, continueTimeoutSeconds: 1);

            Assert.True(success);
            // Steps verified in TappedPoints:
            // 1. matchCenter (800, 770)
            // 2. 3 taps at safe point (836, 786)
            // 3. 1 tap at safe point (836, 786)
            // 4. Fail-safe tap at safe point (836, 786)
            Assert.True(testAdb.TappedPoints.Count >= 6, $"Expected at least 6 taps, got {testAdb.TappedPoints.Count}");
            Assert.Equal(matchCenter, testAdb.TappedPoints[0]);

            Point safePoint = AutomationRoiConstants.ClaimRewardSafeTapPoint;
            Assert.Equal(safePoint, testAdb.TappedPoints[1]);
            Assert.Equal(safePoint, testAdb.TappedPoints[2]);
            Assert.Equal(safePoint, testAdb.TappedPoints[3]);
            Assert.Equal(safePoint, testAdb.TappedPoints[4]);
            Assert.Equal(safePoint, testAdb.TappedPoints[5]);

            // Verify safe point is within ROI x=724..948, y=750..822
            Assert.InRange(safePoint.X, 724, 948);
            Assert.InRange(safePoint.Y, 750, 822);
        }

        [Fact]
        public void Issue3_BattleScreenDetector_ResultContinueTemplates_ContainsContinueReward()
        {
            Assert.Contains(@"ui\continue_reward.png", BattleScreenDetector.ResultContinueTemplates);
        }
    }
}
