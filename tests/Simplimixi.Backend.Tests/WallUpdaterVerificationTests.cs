using System.Text.Json;
using CvAut;
using OpenCvSharp;
using Xunit;

namespace CvAut.Backend.Tests
{
    public class WallUpdaterVerificationTests
    {
        [Fact]
        public void WallUpgradeDecider_PicksElixirWhenElixirAffordabilityIsHigher()
        {
            // Gold: 10M, threshold: 0, reserve: 9M -> afford 1M / 1M = 1 wall
            // Elixir: 15M, threshold: 0, reserve: 0 -> afford 15M / 1M = 15 walls -> capped by batchLimit 5
            var input = new WallUpgradeDecisionInput(
                WallCost: 1_000_000,
                Gold: 10_000_000,
                Elixir: 15_000_000,
                GoldStartThreshold: 0,
                ElixirStartThreshold: 0,
                GoldReserve: 9_000_000,
                ElixirReserve: 0,
                BatchLimit: 5);

            WallUpgradeDecision decision = WallUpgradeDecider.Decide(input);

            Assert.Equal(WallUpgradeResource.Elixir, decision.Resource);
            Assert.Equal(5, decision.RequestedCount);
        }

        [Fact]
        public void WallUpgradeDecider_RespectsReserves()
        {
            // Gold: 10M, Reserve: 9M, WallCost: 2M -> spendable 1M -> cannot afford 2M wall
            var input = new WallUpgradeDecisionInput(
                WallCost: 2_000_000,
                Gold: 10_000_000,
                Elixir: 0,
                GoldStartThreshold: 5_000_000,
                ElixirStartThreshold: 0,
                GoldReserve: 9_000_000,
                ElixirReserve: 0,
                BatchLimit: 1);

            WallUpgradeDecision decision = WallUpgradeDecider.Decide(input);

            Assert.Equal(WallUpgradeResource.None, decision.Resource);
            Assert.Equal(0, decision.RequestedCount);
        }
        [Fact]
        public void ScanWallLocations_RuntimeMenuScreenshot_FindsBothVisibleWallRows()
        {
            string fixturePath = System.IO.Path.Combine(
                System.AppContext.BaseDirectory,
                "Fixtures",
                "Wall",
                "wall_menu_visible_1600x900.png");
            string templatesPath = System.IO.Path.Combine(
                System.AppContext.BaseDirectory,
                "assets",
                "Templates");

            using Mat screenshot = FixtureLoader.LoadMandatory(fixturePath);
            var adb = new ADBHelper("127.0.0.1", 5556);
            using var vision = new VisionEngine(templatesPath);
            var updater = new WallUpdater(adb, vision, templatesPath);

            var locations = updater.ScanWallLocations(screenshot);

            Assert.Contains(locations, point => System.Math.Abs(point.X - 647) <= 10 && System.Math.Abs(point.Y - 560) <= 10);
            Assert.Contains(locations, point => System.Math.Abs(point.X - 647) <= 10 && System.Math.Abs(point.Y - 607) <= 10);
        }

        [Fact]
        public void ScanWallLocations_HandlesEmptyScreenshotGracefully()
        {
            var adb = new ADBHelper("127.0.0.1", 5556);
            var vision = new VisionEngine(System.IO.Path.Combine(System.AppContext.BaseDirectory, "assets", "Templates"));
            var updater = new WallUpdater(adb, vision, System.IO.Path.Combine(System.AppContext.BaseDirectory, "assets", "Templates"));

            using Mat screenshot = new Mat();
            var locations = updater.ScanWallLocations(screenshot);

            Assert.Empty(locations);
        }

        [Fact]
        public void WallUpgradeDecider_ReturnsMissingWallCost_WhenCostIsZeroOrNegative()
        {
            var inputZero = new WallUpgradeDecisionInput(
                WallCost: 0,
                Gold: 10_000_000,
                Elixir: 10_000_000,
                GoldStartThreshold: 0,
                ElixirStartThreshold: 0,
                GoldReserve: 0,
                ElixirReserve: 0,
                BatchLimit: 1);

            WallUpgradeDecision decisionZero = WallUpgradeDecider.Decide(inputZero);
            Assert.Equal(WallUpgradeResource.None, decisionZero.Resource);
            Assert.Equal("missing_wall_cost", decisionZero.SkipReason);
        }

        [Fact]
        public void ReadWallUpgradeCost_RuntimeLevel8Panel_ReadsOneHundredThousandForBothResources()
        {
            string fixturePath = System.IO.Path.Combine(
                System.AppContext.BaseDirectory,
                "Fixtures",
                "Wall",
                "wall_level8_upgrade_panel_1600x900.png");
            string templatesPath = System.IO.Path.Combine(
                System.AppContext.BaseDirectory,
                "assets",
                "Templates");

            using Mat screenshot = FixtureLoader.LoadMandatory(fixturePath);
            using var vision = new VisionEngine(templatesPath);

            bool goldRead = WallUpdater.TryReadWallUpgradeCost(
                vision,
                screenshot,
                WallUiLayout.GoldUpgradeCostRoi,
                out int goldCost,
                out double goldConfidence);
            bool elixirRead = WallUpdater.TryReadWallUpgradeCost(
                vision,
                screenshot,
                WallUiLayout.ElixirUpgradeCostRoi,
                out int elixirCost,
                out double elixirConfidence);

            Assert.True(goldRead);
            Assert.True(elixirRead);
            Assert.Equal(100_000, goldCost);
            Assert.Equal(100_000, elixirCost);
            Assert.True(goldConfidence >= 0.80, $"Gold confidence was {goldConfidence:F2}");
            Assert.True(elixirConfidence >= 0.80, $"Elixir confidence was {elixirConfidence:F2}");
            Assert.True(WallUpdater.ValidateWallCosts(goldCost, elixirCost).IsValid);
        }

        [Fact]
        public void ValidateWallCosts_FailsWhenCostsAreUnreadable()
        {
            var result = WallUpdater.ValidateWallCosts(0, 0);

            Assert.False(result.IsValid);
            Assert.Equal("wall_cost_unreadable", result.Reason);
            Assert.Equal(0, result.Cost);
        }

        [Fact]
        public void ValidateWallCosts_FailsWhenCostRatioExceedsMaxTolerance()
        {
            // 2M vs 3M -> ratio 1.5 > 1.15 -> mismatch
            var result = WallUpdater.ValidateWallCosts(2_000_000, 3_000_000);

            Assert.False(result.IsValid);
            Assert.Equal("wall_cost_mismatch", result.Reason);
            Assert.Equal(0, result.Cost);
        }

        [Fact]
        public void ValidateWallCosts_DifferentPositiveValues_FailsClosed()
        {
            var result = WallUpdater.ValidateWallCosts(1_000_000, 1_500_000);

            Assert.False(result.IsValid);
            Assert.Equal("wall_cost_mismatch", result.Reason);
            Assert.Equal(0, result.Cost);
        }

        [Theory]
        [InlineData(69_686, 75_000)]
        [InlineData(75_001, 75_001)]
        [InlineData(40_000, 40_000)]
        public void ValidateWallCosts_ImplausibleValues_FailClosed(int goldCost, int elixirCost)
        {
            var result = WallUpdater.ValidateWallCosts(goldCost, elixirCost);

            Assert.False(result.IsValid);
            Assert.Equal("wall_cost_implausible", result.Reason);
            Assert.Equal(0, result.Cost);
        }

        [Fact]
        public void IsResourceDeltaVerified_VerifiesDeltaWithinTolerance()
        {
            long before = 10_000_000;
            long expectedSpend = 2_000_000; // 2 walls @ 1M each

            // Exact spend: after = 8M -> verified
            Assert.True(WallUpdater.IsResourceDeltaVerified(before, 8_000_000, expectedSpend));

            // Within 10% tolerance (spend = 1.95M): after = 8,050,000 -> verified
            Assert.True(WallUpdater.IsResourceDeltaVerified(before, 8_050_000, expectedSpend));

            // Outside tolerance (spend = 1.0M): after = 9,000,000 -> mismatch
            Assert.False(WallUpdater.IsResourceDeltaVerified(before, 9_000_000, expectedSpend));

            // Unreadable resource (after = 0) -> unreadable
            Assert.False(WallUpdater.IsResourceDeltaVerified(before, 0, expectedSpend));
        }

        [Fact]
        public void WallDynamicLocalizer_RuntimeLevel8Panel_FindsGoldAndElixirButtonsAndReadsCost()
        {
            string fixturePath = System.IO.Path.Combine(
                System.AppContext.BaseDirectory,
                "Fixtures",
                "Wall",
                "wall_level8_upgrade_panel_1600x900.png");
            string templatesPath = System.IO.Path.Combine(
                System.AppContext.BaseDirectory,
                "assets",
                "Templates");

            using Mat screenshot = FixtureLoader.LoadMandatory(fixturePath);
            using var vision = new VisionEngine(templatesPath);

            var goldInfo = WallDynamicLocalizer.LocalizeResourceButton(vision, screenshot, "gold");
            var elixirInfo = WallDynamicLocalizer.LocalizeResourceButton(vision, screenshot, "elixir");

            Assert.True(goldInfo.Found, $"Gold button not found: {goldInfo.SkipReason}");
            Assert.True(elixirInfo.Found, $"Elixir button not found: {elixirInfo.SkipReason}");

            Assert.True(goldInfo.ButtonRect.Contains(goldInfo.TapPoint), $"Gold tap point {goldInfo.TapPoint} outside {goldInfo.ButtonRect}");
            Assert.True(elixirInfo.ButtonRect.Contains(elixirInfo.TapPoint), $"Elixir tap point {elixirInfo.TapPoint} outside {elixirInfo.ButtonRect}");

            bool goldCostRead = WallUpdater.TryReadWallUpgradeCost(vision, screenshot, goldInfo.CostRoi, out int goldCost, out double goldConfidence);
            bool elixirCostRead = WallUpdater.TryReadWallUpgradeCost(vision, screenshot, elixirInfo.CostRoi, out int elixirCost, out double elixirConfidence);

            Assert.True(goldCostRead);
            Assert.True(elixirCostRead);
            Assert.Equal(100_000, goldCost);
            Assert.Equal(100_000, elixirCost);
            Assert.True(goldConfidence >= 0.80);
            Assert.True(elixirConfidence >= 0.80);
        }

        [Theory]
        [InlineData(20, 20)]
        [InlineData(-20, -15)]
        public void WallDynamicLocalizer_ShiftedScreenshot_LocalizesCorrectlyWithoutAbsoluteRoi(int dx, int dy)
        {
            string fixturePath = System.IO.Path.Combine(
                System.AppContext.BaseDirectory,
                "Fixtures",
                "Wall",
                "wall_level8_upgrade_panel_1600x900.png");
            string templatesPath = System.IO.Path.Combine(
                System.AppContext.BaseDirectory,
                "assets",
                "Templates");

            using Mat original = FixtureLoader.LoadMandatory(fixturePath);
            using var vision = new VisionEngine(templatesPath);

            // Create shifted image
            using Mat shifted = new Mat(original.Size(), original.Type(), Scalar.All(0));
            using Mat translationMatrix = new Mat(2, 3, MatType.CV_32FC1, new float[]
            {
                1, 0, dx,
                0, 1, dy
            });
            Cv2.WarpAffine(original, shifted, translationMatrix, original.Size());

            var goldInfo = WallDynamicLocalizer.LocalizeResourceButton(vision, shifted, "gold");
            var elixirInfo = WallDynamicLocalizer.LocalizeResourceButton(vision, shifted, "elixir");

            Assert.True(goldInfo.Found, $"Gold button not found on shifted ({dx},{dy}): {goldInfo.SkipReason}");
            Assert.True(elixirInfo.Found, $"Elixir button not found on shifted ({dx},{dy}): {elixirInfo.SkipReason}");

            bool goldCostRead = WallUpdater.TryReadWallUpgradeCost(vision, shifted, goldInfo.CostRoi, out int goldCost, out _);
            bool elixirCostRead = WallUpdater.TryReadWallUpgradeCost(vision, shifted, elixirInfo.CostRoi, out int elixirCost, out _);

            Assert.True(goldCostRead);
            Assert.True(elixirCostRead);
            Assert.Equal(100_000, goldCost);
            Assert.Equal(100_000, elixirCost);
        }

        [Fact]
        public void WallDynamicLocalizer_MissingButton_ReturnsSafeFailureWithoutTap()
        {
            string templatesPath = System.IO.Path.Combine(
                System.AppContext.BaseDirectory,
                "assets",
                "Templates");
            using var vision = new VisionEngine(templatesPath);
            using Mat blankScreenshot = new Mat(new Size(1600, 900), MatType.CV_8UC3, Scalar.All(0));

            var goldInfo = WallDynamicLocalizer.LocalizeResourceButton(vision, blankScreenshot, "gold");
            var elixirInfo = WallDynamicLocalizer.LocalizeResourceButton(vision, blankScreenshot, "elixir");

            Assert.False(goldInfo.Found);
            Assert.False(elixirInfo.Found);
            Assert.Equal("resource_button_pair_not_validated", goldInfo.SkipReason);
            Assert.Equal("resource_button_pair_not_validated", elixirInfo.SkipReason);
        }

        [Theory]
        [InlineData(20, 20)]
        [InlineData(-20, -15)]
        public void WallPanelInspector_ValidateWallPanelOpen_ShiftedScreenshot_ValidatesPanelOpen(int dx, int dy)
        {
            string fixturePath = System.IO.Path.Combine(
                System.AppContext.BaseDirectory,
                "Fixtures",
                "Wall",
                "wall_level8_upgrade_panel_1600x900.png");
            string templatesPath = System.IO.Path.Combine(
                System.AppContext.BaseDirectory,
                "assets",
                "Templates");

            using Mat original = FixtureLoader.LoadMandatory(fixturePath);
            using var vision = new VisionEngine(templatesPath);

            using Mat shifted = new Mat(original.Size(), original.Type(), Scalar.All(0));
            using Mat translationMatrix = new Mat(2, 3, MatType.CV_32FC1, new float[]
            {
                1, 0, dx,
                0, 1, dy
            });
            Cv2.WarpAffine(original, shifted, translationMatrix, original.Size());

            var local = WallDynamicLocalizer.LocalizePanelAndButtons(vision, shifted);
            Assert.True(local.GoldInfo.Found);
            Assert.True(local.ElixirInfo.Found);
            Assert.True(WallPanelInspector.IsResourceUpgradeButtonAvailable(shifted, local.GoldInfo));
            Assert.True(WallPanelInspector.IsResourceUpgradeButtonAvailable(shifted, local.ElixirInfo));
        }

        [Fact]
        public void WallQuantityAdjuster_NullOrMissingButtonInfo_ReturnsZeroWithoutTapping()
        {
            var adb = new ADBHelper("127.0.0.1", 5556);
            var adjuster = new WallQuantityAdjuster(adb);

            int selectedNull = adjuster.AddWallsSafely("gold", 3, 5, CancellationToken.None, "test", null, 0, null);
            Assert.Equal(0, selectedNull);

            var missingInfo = new WallResourceButtonInfo { Found = false, SkipReason = "test_missing" };
            int selectedMissing = adjuster.AddWallsSafely("gold", 3, 5, CancellationToken.None, "test", null, 0, missingInfo);
            Assert.Equal(0, selectedMissing);
        }

        [Fact]
        public void WallDynamicLocalizer_ConfirmButtonLocalizationFail_ReturnsNotLocalizedWithoutTap()
        {
            string templatesPath = System.IO.Path.Combine(
                System.AppContext.BaseDirectory,
                "assets",
                "Templates");
            using var vision = new VisionEngine(templatesPath);
            using Mat blankMat = new Mat(new Size(1600, 900), MatType.CV_8UC3, Scalar.All(0));

            var confirmInfo = WallDynamicLocalizer.LocalizeConfirmButton(vision, blankMat, false, null);

            Assert.False(confirmInfo.Found);
            Assert.Equal("confirm_button_not_localized", confirmInfo.SkipReason);
        }

        [Fact]
        public void WallPanelInspector_IsResourceUpgradeButtonAvailable_NullButtonInfo_ReturnsFalseWithoutFixedFallback()
        {
            using Mat screenshot = new Mat(new Size(1600, 900), MatType.CV_8UC3, Scalar.White);

            Assert.False(WallPanelInspector.IsResourceUpgradeButtonAvailable(screenshot, null));

            var missingInfo = new WallResourceButtonInfo { Found = false };
            Assert.False(WallPanelInspector.IsResourceUpgradeButtonAvailable(screenshot, missingInfo));
        }

        [Fact]
        public void WallDynamicLocalizer_AlteredPriceText_LocalizesButtonsAndTapPointsWithoutPriceDependency()
        {
            string fixturePath = System.IO.Path.Combine(
                System.AppContext.BaseDirectory,
                "Fixtures",
                "Wall",
                "wall_level8_upgrade_panel_1600x900.png");
            string templatesPath = System.IO.Path.Combine(
                System.AppContext.BaseDirectory,
                "assets",
                "Templates");

            using Mat original = FixtureLoader.LoadMandatory(fixturePath);
            using var vision = new VisionEngine(templatesPath);

            // Alter price text inside cost capsules to 8,888,888
            using Mat alteredPriceScreenshot = original.Clone();
            Cv2.Rectangle(alteredPriceScreenshot, new Rect(915, 632, 115, 28), new Scalar(30, 30, 30), -1);
            Cv2.Rectangle(alteredPriceScreenshot, new Rect(1090, 632, 115, 28), new Scalar(30, 30, 30), -1);
            Cv2.PutText(alteredPriceScreenshot, "8,888,888", new Point(920, 655), HersheyFonts.HersheySimplex, 0.6, Scalar.White, 2);
            Cv2.PutText(alteredPriceScreenshot, "8,888,888", new Point(1095, 655), HersheyFonts.HersheySimplex, 0.6, Scalar.White, 2);

            var panelLocal = WallDynamicLocalizer.LocalizePanelAndButtons(vision, alteredPriceScreenshot);
            Assert.True(panelLocal.GoldInfo.Found, $"Gold button localization failed: {panelLocal.GoldInfo.SkipReason}");
            Assert.True(panelLocal.ElixirInfo.Found, $"Elixir button localization failed: {panelLocal.ElixirInfo.SkipReason}");

            Assert.True(panelLocal.GoldInfo.ButtonRect.Contains(panelLocal.GoldInfo.TapPoint));
            Assert.True(panelLocal.ElixirInfo.ButtonRect.Contains(panelLocal.ElixirInfo.TapPoint));
            Assert.True(panelLocal.ElixirInfo.ButtonRect.X > panelLocal.GoldInfo.ButtonRect.X);
        }

        [Theory]
        [InlineData(20, 20)]
        [InlineData(-20, -15)]
        public void WallDynamicLocalizer_AlteredPriceTextAndShifted_LocalizesCorrectly(int dx, int dy)
        {
            string fixturePath = System.IO.Path.Combine(
                System.AppContext.BaseDirectory,
                "Fixtures",
                "Wall",
                "wall_level8_upgrade_panel_1600x900.png");
            string templatesPath = System.IO.Path.Combine(
                System.AppContext.BaseDirectory,
                "assets",
                "Templates");

            using Mat original = FixtureLoader.LoadMandatory(fixturePath);
            using var vision = new VisionEngine(templatesPath);

            using Mat altered = original.Clone();
            Cv2.Rectangle(altered, new Rect(915, 632, 115, 28), new Scalar(30, 30, 30), -1);
            Cv2.Rectangle(altered, new Rect(1090, 632, 115, 28), new Scalar(30, 30, 30), -1);
            Cv2.PutText(altered, "8,888,888", new Point(920, 655), HersheyFonts.HersheySimplex, 0.6, Scalar.White, 2);
            Cv2.PutText(altered, "8,888,888", new Point(1095, 655), HersheyFonts.HersheySimplex, 0.6, Scalar.White, 2);

            using Mat shifted = new Mat(original.Size(), original.Type(), Scalar.All(0));
            using Mat translationMatrix = new Mat(2, 3, MatType.CV_32FC1, new float[]
            {
                1, 0, dx,
                0, 1, dy
            });
            Cv2.WarpAffine(altered, shifted, translationMatrix, original.Size());

            var panelLocal = WallDynamicLocalizer.LocalizePanelAndButtons(vision, shifted);
            Assert.True(panelLocal.GoldInfo.Found, $"Gold button shifted altered price failed: {panelLocal.GoldInfo.SkipReason}");
            Assert.True(panelLocal.ElixirInfo.Found, $"Elixir button shifted altered price failed: {panelLocal.ElixirInfo.SkipReason}");
            Assert.True(panelLocal.GoldInfo.ButtonRect.Contains(panelLocal.GoldInfo.TapPoint));
            Assert.True(panelLocal.ElixirInfo.ButtonRect.Contains(panelLocal.ElixirInfo.TapPoint));
        }

        [Fact]
        public void WallDynamicLocalizer_Level16Panel_FindsGoldAndElixirButtonsAndReadsCost5M()
        {
            string fixturePath = System.IO.Path.Combine(
                System.AppContext.BaseDirectory,
                "Fixtures",
                "Wall",
                "wall_level16_upgrade_panel_1600x900.png");
            string templatesPath = System.IO.Path.Combine(
                System.AppContext.BaseDirectory,
                "assets",
                "Templates");

            using Mat screenshot = FixtureLoader.LoadMandatory(fixturePath);
            using var vision = new VisionEngine(templatesPath);

            var panelLocal = WallDynamicLocalizer.LocalizePanelAndButtons(vision, screenshot);

            Assert.True(panelLocal.GoldInfo.Found, $"Gold button Level 16 not found: {panelLocal.GoldInfo.SkipReason}");
            Assert.True(panelLocal.ElixirInfo.Found, $"Elixir button Level 16 not found: {panelLocal.ElixirInfo.SkipReason}");

            Assert.True(panelLocal.GoldInfo.ButtonRect.Contains(panelLocal.GoldInfo.TapPoint));
            Assert.True(panelLocal.ElixirInfo.ButtonRect.Contains(panelLocal.ElixirInfo.TapPoint));
            Assert.True(panelLocal.ElixirInfo.ButtonRect.X > panelLocal.GoldInfo.ButtonRect.X);

            bool goldCostRead = WallUpdater.TryReadWallUpgradeCost(vision, screenshot, panelLocal.GoldInfo.CostRoi, out int goldCost, out double goldConfidence);
            bool elixirCostRead = WallUpdater.TryReadWallUpgradeCost(vision, screenshot, panelLocal.ElixirInfo.CostRoi, out int elixirCost, out double elixirConfidence);

            Assert.True(goldCostRead, "Gold cost OCR failed on Level 16");
            Assert.True(elixirCostRead, "Elixir cost OCR failed on Level 16");
            Assert.Equal(5_000_000, goldCost);
            Assert.Equal(5_000_000, elixirCost);
            Assert.True(goldConfidence >= 0.80);
            Assert.True(elixirConfidence >= 0.80);
        }

        [Fact]
        public void WallDynamicLocalizer_DecoyOverlappingContours_RejectsOverlappingPairs()
        {
            string templatesPath = System.IO.Path.Combine(
                System.AppContext.BaseDirectory,
                "assets",
                "Templates");
            using var vision = new VisionEngine(templatesPath);

            // Draw a synthetic image with overlapping decoy rectangles at top left (x=292 and x=327)
            using Mat decoyImg = new Mat(new Size(1600, 900), MatType.CV_8UC3, Scalar.All(20));
            // Decoy overlapping boxes
            Cv2.Rectangle(decoyImg, new Rect(292, 550, 162, 143), Scalar.All(200), 2);
            Cv2.Rectangle(decoyImg, new Rect(327, 550, 129, 145), Scalar.All(200), 2);

            var panelLocal = WallDynamicLocalizer.LocalizePanelAndButtons(vision, decoyImg);

            Assert.False(panelLocal.GoldInfo.Found);
            Assert.False(panelLocal.ElixirInfo.Found);
            Assert.Equal("resource_button_pair_not_validated", panelLocal.GoldInfo.SkipReason);
            Assert.Equal("resource_button_pair_not_validated", panelLocal.ElixirInfo.SkipReason);
        }

        [Fact]
        public void WallDynamicLocalizer_BottomCostLayoutVariant_LocalizesButtonsAndReadsCost()
        {
            string fixturePath = System.IO.Path.Combine(
                System.AppContext.BaseDirectory,
                "Fixtures",
                "Wall",
                "wall_bottom_cost_panel_1600x900.png");
            string templatesPath = System.IO.Path.Combine(
                System.AppContext.BaseDirectory,
                "assets",
                "Templates");

            using Mat screenshot = FixtureLoader.LoadMandatory(fixturePath);
            using var vision = new VisionEngine(templatesPath);

            var panelLocal = WallDynamicLocalizer.LocalizePanelAndButtons(vision, screenshot);

            Assert.True(panelLocal.GoldInfo.Found, $"Gold button variant layout not found: {panelLocal.GoldInfo.SkipReason}");
            Assert.True(panelLocal.ElixirInfo.Found, $"Elixir button variant layout not found: {panelLocal.ElixirInfo.SkipReason}");

            Assert.True(panelLocal.GoldInfo.ButtonRect.Contains(panelLocal.GoldInfo.TapPoint));
            Assert.True(panelLocal.ElixirInfo.ButtonRect.Contains(panelLocal.ElixirInfo.TapPoint));

            bool goldCostRead = WallUpdater.TryReadWallUpgradeCost(vision, screenshot, panelLocal.GoldInfo.CostRoi, out int goldCost, out double goldConfidence);
            bool elixirCostRead = WallUpdater.TryReadWallUpgradeCost(vision, screenshot, panelLocal.ElixirInfo.CostRoi, out int elixirCost, out double elixirConfidence);

            Assert.True(goldCostRead, "Gold cost OCR failed on bottom cost layout");
            Assert.True(elixirCostRead, "Elixir cost OCR failed on bottom cost layout");
            Assert.Equal(75_000, goldCost);
            Assert.Equal(75_000, elixirCost);
            Assert.True(goldConfidence >= 0.80, $"Gold confidence was {goldConfidence:F2}");
            Assert.True(elixirConfidence >= 0.80, $"Elixir confidence was {elixirConfidence:F2}");
        }

        [Fact]
        public void WallDynamicLocalizer_RightEdgeCostNoise_ReadsTwoHundredThousandForBothResources()
        {
            string fixturePath = System.IO.Path.Combine(
                System.AppContext.BaseDirectory,
                "Fixtures",
                "Wall",
                "wall_level9_200k_edge_noise_1600x900.png");
            string templatesPath = System.IO.Path.Combine(
                System.AppContext.BaseDirectory,
                "assets",
                "Templates");

            using Mat screenshot = FixtureLoader.LoadMandatory(fixturePath);
            using var vision = new VisionEngine(templatesPath);

            var panelLocal = WallDynamicLocalizer.LocalizePanelAndButtons(vision, screenshot);

            Assert.True(panelLocal.GoldInfo.Found, $"Gold button not found: {panelLocal.GoldInfo.SkipReason}");
            Assert.True(panelLocal.ElixirInfo.Found, $"Elixir button not found: {panelLocal.ElixirInfo.SkipReason}");

            Assert.True(WallUpdater.TryReadWallUpgradeCost(vision, screenshot, panelLocal.GoldInfo.CostRoi, out int goldCost, out double goldConfidence));
            Assert.True(WallUpdater.TryReadWallUpgradeCost(vision, screenshot, panelLocal.ElixirInfo.CostRoi, out int elixirCost, out double elixirConfidence));
            Assert.Equal(200_000, goldCost);
            Assert.Equal(200_000, elixirCost);
            Assert.True(goldConfidence >= 0.80, $"Gold confidence was {goldConfidence:F2}");
            Assert.True(elixirConfidence >= 0.80, $"Elixir confidence was {elixirConfidence:F2}");
            Assert.True(WallUpdater.ValidateWallCosts(goldCost, elixirCost).IsValid);
        }

        public static IEnumerable<object[]> WallGoldenCostCases()
        {
            yield return new object[] { "wall_level8_upgrade_panel_1600x900.png", 100_000 };
            yield return new object[] { "wall_bottom_cost_panel_1600x900.png", 75_000 };
            yield return new object[] { "wall_level16_upgrade_panel_1600x900.png", 5_000_000 };
            yield return new object[] { "wall_level16_5m_runtime_elixir_ocr_fail_1600x900.png", 5_000_000 };
            yield return new object[] { "wall_level9_200k_edge_noise_1600x900.png", 200_000 };
            yield return new object[] { "wall_runtime_278_75k_1600x900.png", 75_000 };
            yield return new object[] { "wall_level18_red_10m_top_1600x900.png", 10_000_000 };
            yield return new object[] { "wall_level17_red_7m_top_1600x900.png", 7_000_000 };
            yield return new object[] { "wall_red_1m_top_1600x900.png", 1_000_000 };
            yield return new object[] { "wall_red_1_5m_top_1600x900.png", 1_500_000 };
        }

        [Theory]
        [MemberData(nameof(WallGoldenCostCases))]
        public void WallGoldenFixtures_ReadExpectedCosts(string fixtureFileName, int expectedCost)
        {
            using Mat screenshot = LoadWallFixture(fixtureFileName);
            using var vision = CreateVisionEngine();

            AssertWallCostsRead(vision, screenshot, expectedCost, fixtureFileName);
        }

        public static IEnumerable<object[]> WallAugmentedCostCases()
        {
            foreach (object[] golden in WallGoldenCostCases())
            {
                yield return new object[] { golden[0], golden[1], 20, 20, 1.00, 0.0 };
                yield return new object[] { golden[0], golden[1], -20, -15, 1.00, 0.0 };
            }
        }

        [Theory]
        [MemberData(nameof(WallAugmentedCostCases))]
        public void WallAugmentedFixtures_ReadExpectedCosts(string fixtureFileName, int expectedCost, int dx, int dy, double alpha, double beta)
        {
            using Mat original = LoadWallFixture(fixtureFileName);
            using Mat augmented = ApplyWallAugmentation(original, dx, dy, alpha, beta);
            using var vision = CreateVisionEngine();

            AssertWallCostsRead(vision, augmented, expectedCost, $"{fixtureFileName} shift=({dx},{dy}) alpha={alpha:F2}");
        }

        [Theory]
        [InlineData("wall_level8_upgrade_panel_1600x900.png", 100_000, 1032, 643)]
        [InlineData("wall_level9_200k_edge_noise_1600x900.png", 200_000, 1110, 636)]
        public void WallSyntheticRightEdgeNoise_DoesNotAppendExtraDigit(string fixtureFileName, int expectedCost, int noiseX, int noiseY)
        {
            using Mat screenshot = LoadWallFixture(fixtureFileName);
            using var vision = CreateVisionEngine();

            // Simulate a bright clipped digit/sparkle at the right edge of Elixir CostRoi.
            Cv2.PutText(screenshot, "7", new Point(noiseX, noiseY), HersheyFonts.HersheySimplex, 0.55, Scalar.White, 2);

            AssertWallCostsRead(vision, screenshot, expectedCost, $"{fixtureFileName} with synthetic right-edge noise");
        }

        [Theory]
        [InlineData(1_000)]
        [InlineData(12_345)]
        [InlineData(75_000)]
        [InlineData(100_000)]
        [InlineData(200_000)]
        [InlineData(999_999)]
        [InlineData(1_234_567)]
        [InlineData(5_000_000)]
        [InlineData(8_888_888)]
        public void WallSyntheticCostTextMatrix_ReadsGeneratedPrice(int expectedCost)
        {
            using Mat screenshot = LoadWallFixture("wall_level9_200k_edge_noise_1600x900.png");
            using var vision = CreateVisionEngine();
            var panelLocal = WallDynamicLocalizer.LocalizePanelAndButtons(vision, screenshot);
            Assert.True(panelLocal.GoldInfo.Found, $"Gold not found: {panelLocal.GoldInfo.SkipReason}");
            Assert.True(panelLocal.ElixirInfo.Found, $"Elixir not found: {panelLocal.ElixirInfo.SkipReason}");

            DrawSyntheticCostText(screenshot, panelLocal.GoldInfo.CostRoi, expectedCost);
            DrawSyntheticCostText(screenshot, panelLocal.ElixirInfo.CostRoi, expectedCost);

            bool goldRead = WallUpdater.TryReadWallUpgradeCost(vision, screenshot, panelLocal.GoldInfo.CostRoi, out int goldCost, out double goldConfidence);
            bool elixirRead = WallUpdater.TryReadWallUpgradeCost(vision, screenshot, panelLocal.ElixirInfo.CostRoi, out int elixirCost, out double elixirConfidence);

            Assert.True(goldRead, $"Gold OCR failed for synthetic cost {expectedCost:N0}");
            Assert.True(elixirRead, $"Elixir OCR failed for synthetic cost {expectedCost:N0}");
            Assert.Equal(expectedCost, goldCost);
            Assert.Equal(expectedCost, elixirCost);
            Assert.True(goldConfidence >= 0.80, $"Gold confidence was {goldConfidence:F2} for synthetic cost {expectedCost:N0}");
            Assert.True(elixirConfidence >= 0.80, $"Elixir confidence was {elixirConfidence:F2} for synthetic cost {expectedCost:N0}");
        }

        private static VisionEngine CreateVisionEngine()
        {
            string templatesPath = System.IO.Path.Combine(System.AppContext.BaseDirectory, "assets", "Templates");
            return new VisionEngine(templatesPath);
        }

        private static Mat LoadWallFixture(string fixtureFileName)
        {
            string fixturePath = System.IO.Path.Combine(System.AppContext.BaseDirectory, "Fixtures", "Wall", fixtureFileName);
            return FixtureLoader.LoadMandatory(fixturePath);
        }

        private static Mat ApplyWallAugmentation(Mat original, int dx, int dy, double alpha, double beta)
        {
            Mat shifted = new Mat(original.Size(), original.Type(), Scalar.All(0));
            using Mat translationMatrix = new Mat(2, 3, MatType.CV_32FC1, new float[] { 1, 0, dx, 0, 1, dy });
            Cv2.WarpAffine(original, shifted, translationMatrix, original.Size());

            if (Math.Abs(alpha - 1.0) > 0.001 || Math.Abs(beta) > 0.001)
            {
                Mat adjusted = new Mat();
                shifted.ConvertTo(adjusted, shifted.Type(), alpha, beta);
                shifted.Dispose();
                return adjusted;
            }

            return shifted;
        }

        private static void DrawSyntheticCostText(Mat screenshot, Rect costRoi, int cost)
        {
            Rect clearRoi = new(
                Math.Max(0, costRoi.X + 2),
                Math.Max(0, costRoi.Y + 2),
                Math.Min(costRoi.Width - 4, screenshot.Width - costRoi.X - 2),
                Math.Min(costRoi.Height - 5, screenshot.Height - costRoi.Y - 2));

            Cv2.Rectangle(screenshot, clearRoi, new Scalar(63, 57, 61), -1);

            string text = cost.ToString(System.Globalization.CultureInfo.InvariantCulture);
            using var ocr = new DigitOcrReader();
            var templates = (System.Collections.Generic.Dictionary<int, Mat>)typeof(DigitOcrReader)
                .GetField("_templates", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
                .GetValue(ocr)!;

            const int digitWidth = 12;
            const int digitHeight = 16;
            const int spacing = 1;
            int totalWidth = text.Length * digitWidth + (text.Length - 1) * spacing;
            int startX = costRoi.X + Math.Max(3, (costRoi.Width - totalWidth) / 2);
            int startY = costRoi.Y + Math.Max(3, (costRoi.Height - digitHeight) / 2);

            foreach (char ch in text)
            {
                int digit = ch - '0';
                for (int y = 0; y < digitHeight; y++)
                {
                    for (int x = 0; x < digitWidth; x++)
                    {
                        if (templates[digit].At<byte>(y, x) > 0)
                        {
                            screenshot.Set(startY + y, startX + x, new Vec3b(255, 255, 255));
                        }
                    }
                }

                startX += digitWidth + spacing;
            }
        }

        private static void AssertWallCostsRead(VisionEngine vision, Mat screenshot, int expectedCost, string caseName)
        {
            var panelLocal = WallDynamicLocalizer.LocalizePanelAndButtons(vision, screenshot);
            Assert.True(panelLocal.GoldInfo.Found, $"Gold not found for {caseName}: {panelLocal.GoldInfo.SkipReason}");
            Assert.True(panelLocal.ElixirInfo.Found, $"Elixir not found for {caseName}: {panelLocal.ElixirInfo.SkipReason}");

            bool goldRead = WallUpdater.TryReadWallUpgradeCost(vision, screenshot, panelLocal.GoldInfo.CostRoi, out int goldCost, out double goldConfidence);
            bool elixirRead = WallUpdater.TryReadWallUpgradeCost(vision, screenshot, panelLocal.ElixirInfo.CostRoi, out int elixirCost, out double elixirConfidence);

            Assert.True(goldRead, $"Gold OCR failed for {caseName}");
            Assert.True(elixirRead, $"Elixir OCR failed for {caseName}");
            Assert.Equal(expectedCost, goldCost);
            Assert.Equal(expectedCost, elixirCost);
            Assert.True(goldConfidence >= 0.80, $"Gold confidence was {goldConfidence:F2} for {caseName}");
            Assert.True(elixirConfidence >= 0.80, $"Elixir confidence was {elixirConfidence:F2} for {caseName}");
            Assert.True(WallUpdater.ValidateWallCosts(goldCost, elixirCost).IsValid, $"Gold/Elixir mismatch for {caseName}: {goldCost} vs {elixirCost}");
        }

        // -------------------------------------------------------------------------
        // HUD-delta verification unit tests (Bước 2 — cases a/b/c/d)
        // -------------------------------------------------------------------------

        /// <summary>
        /// Case a: actualSpend khớp chính xác expectedSpend → verified true.
        /// </summary>
        [Fact]
        public void IsResourceDeltaVerified_ExactSpend_ReturnsTrue()
        {
            long before = 10_000_000;
            long expectedSpend = 1_000_000; // 1 tường × 1M

            // after = before - expectedSpend → actualSpend == expectedSpend
            long after = before - expectedSpend;

            Assert.True(WallUpdater.IsResourceDeltaVerified(before, after, expectedSpend),
                $"Expected verified=true for exact spend. before={before:N0} after={after:N0} expectedSpend={expectedSpend:N0}");
        }

        /// <summary>
        /// Case a (batch): actualSpend = expectedSpend = 3 × 500_000 → verified true.
        /// </summary>
        [Fact]
        public void IsResourceDeltaVerified_ExactBatchSpend_ReturnsTrue()
        {
            long singleWallCost = 500_000;
            int count = 3;
            long before = 8_000_000;
            long expectedSpend = singleWallCost * count; // 1_500_000
            long after = before - expectedSpend;         // 6_500_000

            Assert.True(WallUpdater.IsResourceDeltaVerified(before, after, expectedSpend),
                $"Batch exact spend should verify. before={before:N0} after={after:N0} expectedSpend={expectedSpend:N0}");
        }

        /// <summary>
        /// Case b: actualSpend lệch quá tolerance (mua thiếu rõ ràng) → verified false.
        /// Tolerance mặc định = Max(20_000, expectedSpend/10).
        /// </summary>
        [Fact]
        public void IsResourceDeltaVerified_SpendFarBelowTolerance_ReturnsFalse()
        {
            long before = 10_000_000;
            long expectedSpend = 2_000_000; // tolerance = Max(20_000, 200_000) = 200_000
            // actualSpend = 1_000_000 → lệch 1_000_000 >> tolerance 200_000
            long after = before - 1_000_000;

            Assert.False(WallUpdater.IsResourceDeltaVerified(before, after, expectedSpend),
                $"Spend much lower than expected (>{expectedSpend / 10:N0} tolerance) should be false.");
        }

        /// <summary>
        /// Case b (phía trên): actualSpend vượt quá expectedSpend + tolerance → verified false.
        /// </summary>
        [Fact]
        public void IsResourceDeltaVerified_SpendFarAboveTolerance_ReturnsFalse()
        {
            long before = 10_000_000;
            long expectedSpend = 1_000_000; // tolerance = Max(20_000, 100_000) = 100_000
            // actualSpend = 1_500_000 → lệch 500_000 >> tolerance 100_000
            long after = before - 1_500_000;

            Assert.False(WallUpdater.IsResourceDeltaVerified(before, after, expectedSpend),
                $"Spend much higher than expected (>{expectedSpend / 10:N0} tolerance) should be false.");
        }

        /// <summary>
        /// Case c: Giảm giá — actualSpend NHỎ hơn expectedSpend nhiều (>tolerance).
        /// Ví dụ: bot tính expectedSpend = 2_000_000 nhưng do sự kiện giảm giá,
        /// game chỉ trừ 1_000_000 → actualSpend lệch 1_000_000, tolerance = 200_000.
        /// Hành vi đúng theo policy hiện tại: IsResourceDeltaVerified trả FALSE
        /// (policy không có ngoại lệ cho discount, chỉ chấp nhận window [expected±tolerance]).
        ///
        /// NOTE cho reviewer: nếu muốn cho phép actualSpend &lt; expected khi có sự kiện giảm giá,
        /// cần sửa WallCostPolicy.IsResourceDeltaVerified để nới vế dưới của window
        /// (ví dụ: chỉ check actualSpend &lt;= expected + tolerance, bỏ giới hạn dưới).
        /// Hiện tại test này GHI NHẬN hành vi policy hiện tại (false) mà không thay đổi policy.
        /// </summary>
        [Fact]
        public void IsResourceDeltaVerified_DiscountedSpend_ExceedsTolerance_ReturnsFalse_PolicyCurrentBehavior()
        {
            long before = 10_000_000;
            long expectedSpend = 2_000_000; // tolerance = Max(20_000, 200_000) = 200_000
            // Giả sử sự kiện giảm giá 50%: game chỉ trừ 1_000_000
            long discountedActualSpend = 1_000_000;
            long after = before - discountedActualSpend;

            // Theo policy hiện tại: (expectedSpend - tolerance) = 1_800_000, actualSpend = 1_000_000 < 1_800_000 → false
            Assert.False(WallUpdater.IsResourceDeltaVerified(before, after, expectedSpend),
                "Discounted spend that is far below (expected - tolerance) should return false under current policy. " +
                "See method summary for note on discount handling.");
        }

        /// <summary>
        /// Case c bổ sung: Giảm giá nhỏ vẫn nằm trong tolerance → vẫn verified true.
        /// Ví dụ: expectedSpend = 1_000_000, tolerance = 100_000, actualSpend = 950_000 (giảm 5%).
        /// </summary>
        [Fact]
        public void IsResourceDeltaVerified_SmallDiscount_WithinTolerance_ReturnsTrue()
        {
            long before = 5_000_000;
            long expectedSpend = 1_000_000; // tolerance = Max(20_000, 100_000) = 100_000
            // actualSpend = 950_000 → lệch 50_000 < tolerance 100_000 → verified
            long after = before - 950_000;

            Assert.True(WallUpdater.IsResourceDeltaVerified(before, after, expectedSpend),
                "Small discount within tolerance should still verify.");
        }

        /// <summary>
        /// Case d: before hoặc after &lt;= 0 (đọc HUD lỗi) → verified false.
        /// </summary>
        [Theory]
        [InlineData(0L, 5_000_000L)]    // before = 0 → lỗi
        [InlineData(10_000_000L, 0L)]   // after = 0 → lỗi
        [InlineData(-1L, 5_000_000L)]   // before âm → lỗi
        [InlineData(10_000_000L, -1L)]  // after âm → lỗi
        public void IsResourceDeltaVerified_HudReadFailure_ReturnsFalse(long before, long after)
        {
            long expectedSpend = 1_000_000;

            Assert.False(WallUpdater.IsResourceDeltaVerified(before, after, expectedSpend),
                $"HUD read failure (before={before}, after={after}) should return false. The policy guards: 'if (resourceAfter <= 0 || resourceBefore <= 0) return false'.");
        }

        /// <summary>
        /// Case d bổ sung: Verify rằng tolerance mặc định = Max(20_000, expectedSpend/10)
        /// áp dụng đúng cho giá nhỏ (expectedSpend &lt; 200_000 → tolerance = 20_000).
        /// </summary>
        [Fact]
        public void IsResourceDeltaVerified_SmallCostUsesMinimumTolerance20k()
        {
            long before = 500_000;
            long expectedSpend = 75_000; // tolerance = Max(20_000, 7_500) = 20_000
            // actualSpend = 70_000 → lệch 5_000 < tolerance 20_000 → verified
            long after = before - 70_000;

            Assert.True(WallUpdater.IsResourceDeltaVerified(before, after, expectedSpend),
                "Small cost should use minimum tolerance of 20_000, and 5k deviation should verify.");

            // actualSpend = 40_000 → lệch 35_000 > tolerance 20_000 → false
            long afterFar = before - 40_000;
            Assert.False(WallUpdater.IsResourceDeltaVerified(before, afterFar, expectedSpend),
                "Deviation of 35k on a 75k spend should exceed minimum tolerance of 20k.");
        }

        [Theory]
        [InlineData(100_000, 0)]
        [InlineData(0, 100_000)]
        public void ValidateWallCosts_OnlyOneReadable_FailsClosed(int goldCost, int elixirCost)
        {
            WallCostValidationResult result = WallUpdater.ValidateWallCosts(goldCost, elixirCost);

            Assert.False(result.IsValid);
            Assert.Equal(0, result.Cost);
            Assert.Equal("wall_cost_pair_incomplete", result.Reason);
        }

        [Theory]
        [InlineData("wall_level8_upgrade_panel_1600x900.png", 100_000)]
        [InlineData("wall_bottom_cost_panel_1600x900.png", 75_000)]
        [InlineData("wall_level16_upgrade_panel_1600x900.png", 5_000_000)]
        [InlineData("wall_runtime_278_75k_1600x900.png", 75_000)]
        public void WallCostRoiLocator_RealFixtures_ReturnsVerifiedPairedRois(string fixtureFileName, int expectedCost)
        {
            using Mat screenshot = LoadWallFixture(fixtureFileName);
            using var vision = CreateVisionEngine();
            WallPanelLocalizationResult panel = WallDynamicLocalizer.LocalizePanelAndButtons(vision, screenshot);

            Assert.True(panel.GoldInfo.Found, panel.GoldInfo.SkipReason);
            Assert.True(panel.ElixirInfo.Found, panel.ElixirInfo.SkipReason);

            WallCostRoiPairLocalization pair = WallCostRoiLocator.LocalizePair(
                vision,
                screenshot,
                panel.GoldInfo.ButtonRect,
                panel.ElixirInfo.ButtonRect);

            Assert.True(pair.Found, pair.FailureReason);
            Assert.True(pair.OcrVerified, pair.FailureReason);
            Assert.Equal(expectedCost, pair.GoldCost);
            Assert.Equal(expectedCost, pair.ElixirCost);
            Assert.True(panel.GoldInfo.ButtonRect.IntersectsWith(pair.GoldRoi));
            Assert.True(panel.ElixirInfo.ButtonRect.IntersectsWith(pair.ElixirRoi));
            Assert.StartsWith("pair_grid_", pair.Method);
        }

        [Fact]
        public void WallCostRoiLocator_Level18RedCost_SelectsTopHeaderAndReads10M()
        {
            using Mat screenshot = LoadWallFixture("wall_level18_red_10m_top_1600x900.png");
            using var vision = CreateVisionEngine();

            WallPanelLocalizationResult panel = WallDynamicLocalizer.LocalizePanelAndButtons(vision, screenshot);

            Assert.Equal(WallUpgradeResourceMode.GoldAndElixir, panel.ResourceMode);
            Assert.True(panel.GoldInfo.CostRoiVerified);
            Assert.True(panel.ElixirInfo.CostRoiVerified);
            Assert.True(panel.GoldInfo.CostRoi.Y < panel.GoldInfo.ButtonRect.Y + panel.GoldInfo.ButtonRect.Height / 2);
            Assert.True(panel.ElixirInfo.CostRoi.Y < panel.ElixirInfo.ButtonRect.Y + panel.ElixirInfo.ButtonRect.Height / 2);
            Assert.True(WallUpdater.TryReadWallUpgradeCost(vision, screenshot, panel.GoldInfo.CostRoi, out int goldCost, out _));
            Assert.True(WallUpdater.TryReadWallUpgradeCost(vision, screenshot, panel.ElixirInfo.CostRoi, out int elixirCost, out _));
            Assert.Equal(10_000_000, goldCost);
            Assert.Equal(10_000_000, elixirCost);
        }

        [Fact]
        public void WallDynamicLocalizer_FullyUpgradedWall_ReturnsTwoButtonsAndSafeState()
        {
            using Mat screenshot = LoadWallFixture("wall_fully_upgraded_info_select_1600x900.png");
            using var vision = CreateVisionEngine();
            WallPanelLocalizationResult panel = WallDynamicLocalizer.LocalizePanelAndButtons(vision, screenshot);

            Assert.Equal(2, panel.DetectedButtons.Count);
            Assert.Equal(WallUpgradeResourceMode.FullyUpgraded, panel.ResourceMode);
            Assert.False(panel.GoldInfo.Found);
            Assert.False(panel.ElixirInfo.Found);
            Assert.Equal("wall_fully_upgraded", panel.GoldInfo.SkipReason);
            Assert.Equal("wall_fully_upgraded", panel.ElixirInfo.SkipReason);
        }

        [Fact]
        public void AllNamedWallScreenshotsMatchFilenameMetadata()
        {
            string inputDir = Path.Combine(Directory.GetCurrentDirectory(), "scratch", "walltestimage");
            if (!Directory.Exists(inputDir))
            {
                inputDir = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "scratch", "walltestimage"));
            }

            string[] imagePaths = Directory.GetFiles(inputDir, "wall_level*.png");
            Assert.Equal(122, imagePaths.Length);
            int[] levelCosts =
            {
                0, 1_000, 5_000, 10_000, 20_000, 30_000, 50_000, 75_000, 100_000,
                200_000, 500_000, 1_000_000, 1_500_000, 2_000_000, 3_000_000,
                4_000_000, 5_000_000, 7_000_000
            };

            using var vision = CreateVisionEngine();
            var failures = new List<string>();
            foreach (string path in imagePaths.OrderBy(path => path, StringComparer.Ordinal))
            {
                string name = Path.GetFileName(path);
                using Mat screenshot = Cv2.ImRead(path);
                WallPanelLocalizationResult panel = WallDynamicLocalizer.LocalizePanelAndButtons(vision, screenshot);

                if (name.Contains("_buttons5_", StringComparison.Ordinal) && panel.DetectedButtons.Count != 5)
                {
                    failures.Add($"{name}: expected 5 buttons, got {panel.DetectedButtons.Count}");
                }

                if (name.Contains("fully_upgraded", StringComparison.Ordinal))
                {
                    if (panel.ResourceMode != WallUpgradeResourceMode.FullyUpgraded)
                        failures.Add($"{name}: expected FullyUpgraded, got {panel.ResourceMode}");
                    continue;
                }

                int levelStart = "wall_level".Length;
                int levelEnd = name.IndexOf('_', levelStart);
                int level = int.Parse(name[levelStart..levelEnd], System.Globalization.CultureInfo.InvariantCulture);
                int expectedCost = name.Contains("_75k_discount_", StringComparison.Ordinal)
                    ? 75_000
                    : level == 18 ? 10_000_000 : levelCosts[level];
                WallUpgradeResourceMode expectedMode = name.Contains("_gold_only_", StringComparison.Ordinal)
                    ? WallUpgradeResourceMode.GoldOnly
                    : WallUpgradeResourceMode.GoldAndElixir;

                int goldCost = 0;
                int elixirCost = 0;
                bool goldOk = panel.GoldInfo.Found && WallUpdater.TryReadWallUpgradeCost(
                    vision, screenshot, panel.GoldInfo.CostRoi, out goldCost, out _);
                bool elixirOk = panel.ElixirInfo.Found && WallUpdater.TryReadWallUpgradeCost(
                    vision, screenshot, panel.ElixirInfo.CostRoi, out elixirCost, out _);

                if (panel.ResourceMode != expectedMode || !panel.GoldInfo.CostRoiVerified ||
                    !goldOk || goldCost != expectedCost)
                {
                    failures.Add(
                        $"{name}: expected mode={expectedMode}, cost={expectedCost:N0}; " +
                        $"got mode={panel.ResourceMode}, gold={goldCost:N0}, goldVerified={panel.GoldInfo.CostRoiVerified}");
                    continue;
                }

                bool goldRed = WallCostPolicy.IsUpgradeCostRed(screenshot, panel.GoldInfo.CostRoi, out _, out _);
                if (name.Contains("_gold_red_", StringComparison.Ordinal) != goldRed)
                    failures.Add($"{name}: Gold color in filename does not match pixels (red={goldRed})");

                if (expectedMode == WallUpgradeResourceMode.GoldAndElixir)
                {
                    if (!panel.ElixirInfo.CostRoiVerified || !elixirOk || elixirCost != expectedCost)
                    {
                        failures.Add(
                            $"{name}: expected Elixir={expectedCost:N0}; got {elixirCost:N0}, " +
                            $"verified={panel.ElixirInfo.CostRoiVerified}");
                        continue;
                    }
                    bool elixirRed = WallCostPolicy.IsUpgradeCostRed(screenshot, panel.ElixirInfo.CostRoi, out _, out _);
                    if (name.Contains("_elixir_red_", StringComparison.Ordinal) != elixirRed)
                        failures.Add($"{name}: Elixir color in filename does not match pixels (red={elixirRed})");
                }
                else if (panel.ElixirInfo.Found)
                {
                    failures.Add($"{name}: Gold-only image unexpectedly localized an Elixir button");
                }
            }

            Assert.True(failures.Count == 0, string.Join(Environment.NewLine, failures));
        }

        [Fact]
        public void TestGoldRedElixirWhiteScreenshots()
        {
            string inputDir = Path.Combine(Directory.GetCurrentDirectory(), "scratch", "walltestimage");
            if (!Directory.Exists(inputDir))
            {
                inputDir = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "scratch", "walltestimage"));
            }

            string[] imagePaths = Directory
                .GetFiles(inputDir, "wall_level*_batch_20260806_2212.png")
                .OrderBy(path => int.Parse(
                    Path.GetFileName(path).Split('_')[1]["level".Length..],
                    System.Globalization.CultureInfo.InvariantCulture))
                .ToArray();
            int[] expectedCosts =
            {
                20_000, 30_000, 50_000, 75_000, 100_000, 200_000, 500_000, 1_000_000,
                1_500_000, 2_000_000, 3_000_000, 4_000_000, 5_000_000, 7_000_000, 10_000_000
            };
            Assert.Equal(expectedCosts.Length, imagePaths.Length);

            using var vision = CreateVisionEngine();
            var failures = new List<string>();
            for (int index = 0; index < imagePaths.Length; index++)
            {
                using Mat screenshot = Cv2.ImRead(imagePaths[index]);
                WallPanelLocalizationResult panel = WallDynamicLocalizer.LocalizePanelAndButtons(vision, screenshot);
                int goldCost = 0;
                int elixirCost = 0;
                double goldConfidence = 0;
                double elixirConfidence = 0;
                bool goldOk = panel.GoldInfo.Found && WallUpdater.TryReadWallUpgradeCost(
                    vision, screenshot, panel.GoldInfo.CostRoi, out goldCost, out goldConfidence);
                bool elixirOk = panel.ElixirInfo.Found && WallUpdater.TryReadWallUpgradeCost(
                    vision, screenshot, panel.ElixirInfo.CostRoi, out elixirCost, out elixirConfidence);

                System.Console.WriteLine(
                    $"[GOLD RED / ELIXIR WHITE] L{index + 4}: Mode={panel.ResourceMode}, " +
                    $"Gold={goldCost:N0} ({goldOk}, {goldConfidence:F2}) Roi={panel.GoldInfo.CostRoi}, " +
                    $"Elixir={elixirCost:N0} ({elixirOk}, {elixirConfidence:F2}) Roi={panel.ElixirInfo.CostRoi}");

                if (panel.ResourceMode != WallUpgradeResourceMode.GoldAndElixir ||
                    !panel.GoldInfo.CostRoiVerified || !panel.ElixirInfo.CostRoiVerified ||
                    !goldOk || !elixirOk || goldCost != expectedCosts[index] || elixirCost != expectedCosts[index])
                {
                    failures.Add(
                        $"L{index + 4} expected {expectedCosts[index]:N0}; " +
                        $"Mode={panel.ResourceMode}, Gold={goldCost:N0}, Elixir={elixirCost:N0}, " +
                        $"GoldRoiVerified={panel.GoldInfo.CostRoiVerified}, ElixirRoiVerified={panel.ElixirInfo.CostRoiVerified}");
                }
            }

            Assert.True(failures.Count == 0, string.Join(Environment.NewLine, failures));
        }

        [Fact]
        public void TestUserScreenshotsAndSaveCrops()
        {
            string inputDir = Path.Combine(Directory.GetCurrentDirectory(), "scratch", "walltestimage");
            if (!Directory.Exists(inputDir))
            {
                inputDir = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "scratch", "walltestimage"));
            }
            string[] imagePaths = Directory
                .GetFiles(inputDir, "wall_level*_batch_20260806_2109.png")
                .OrderBy(path => int.Parse(
                    Path.GetFileName(path).Split('_')[1]["level".Length..],
                    System.Globalization.CultureInfo.InvariantCulture))
                .ToArray();
            int[] expectedCosts =
            {
                1_000, 5_000, 10_000, 20_000, 30_000, 50_000, 75_000, 100_000, 200_000,
                500_000, 1_000_000, 1_500_000, 2_000_000, 3_000_000, 4_000_000,
                5_000_000, 7_000_000, 10_000_000
            };
            Assert.Equal(expectedCosts.Length, imagePaths.Length);

            string outputDir = Path.Combine(inputDir, "button_crops", "batch_20260806_2109");
            Directory.CreateDirectory(outputDir);
            using var vision = CreateVisionEngine();

            for (int imgIdx = 0; imgIdx < imagePaths.Length; imgIdx++)
            {
                string path = imagePaths[imgIdx];
                if (!File.Exists(path))
                {
                    System.Console.WriteLine($"[USER TEST] Image {imgIdx + 1} NOT FOUND: {path}");
                    continue;
                }

                using Mat screenshot = Cv2.ImRead(path);
                if (screenshot.Empty())
                {
                    System.Console.WriteLine($"[USER TEST] Image {imgIdx + 1} EMPTY: {path}");
                    continue;
                }

                WallPanelLocalizationResult panel = WallDynamicLocalizer.LocalizePanelAndButtons(vision, screenshot);

                bool goldOk = WallUpdater.TryReadWallUpgradeCost(vision, screenshot, panel.GoldInfo.CostRoi, out int goldCost, out double goldConf);
                bool elixirOk = WallUpdater.TryReadWallUpgradeCost(vision, screenshot, panel.ElixirInfo.CostRoi, out int elixirCost, out double elixirConf);

                WallUpgradeResourceMode expectedMode = imgIdx < 3
                    ? WallUpgradeResourceMode.GoldOnly
                    : WallUpgradeResourceMode.GoldAndElixir;
                Assert.Equal(expectedMode, panel.ResourceMode);
                Assert.True(panel.GoldInfo.Found);
                Assert.True(panel.GoldInfo.CostRoiVerified);
                Assert.True(goldOk);
                Assert.Equal(expectedCosts[imgIdx], goldCost);
                if (expectedMode == WallUpgradeResourceMode.GoldAndElixir)
                {
                    Assert.True(panel.ElixirInfo.Found);
                    Assert.True(panel.ElixirInfo.CostRoiVerified);
                    Assert.True(elixirOk);
                    Assert.Equal(expectedCosts[imgIdx], elixirCost);
                }
                else
                {
                    Assert.False(panel.ElixirInfo.Found);
                }

                System.Console.WriteLine($"[USER TEST RESULT] Image {imgIdx + 1} ({System.IO.Path.GetFileName(path)}): Mode={panel.ResourceMode}, Buttons={panel.DetectedButtons.Count}");
                System.Console.WriteLine($"  - GOLD OCR:   Found={panel.GoldInfo.Found}, Read={goldOk}, Cost={goldCost:N0}, Conf={goldConf:F2}, CostRoi=({panel.GoldInfo.CostRoi.X},{panel.GoldInfo.CostRoi.Y},{panel.GoldInfo.CostRoi.Width},{panel.GoldInfo.CostRoi.Height})");
                System.Console.WriteLine($"  - ELIXIR OCR: Found={panel.ElixirInfo.Found}, Read={elixirOk}, Cost={elixirCost:N0}, Conf={elixirConf:F2}, CostRoi=({panel.ElixirInfo.CostRoi.X},{panel.ElixirInfo.CostRoi.Y},{panel.ElixirInfo.CostRoi.Width},{panel.ElixirInfo.CostRoi.Height})");

                for (int btnIdx = 0; btnIdx < panel.DetectedButtons.Count; btnIdx++)
                {
                    Rect btnRect = panel.DetectedButtons[btnIdx];
                    System.Console.WriteLine($"  - Button {btnIdx + 1}: Rect=({btnRect.X},{btnRect.Y},{btnRect.Width},{btnRect.Height})");

                    Rect clamped = ImageUtils.ClampRect(btnRect, screenshot.Width, screenshot.Height);
                    if (clamped.Width > 0 && clamped.Height > 0)
                    {
                        using Mat crop = new Mat(screenshot, clamped);
                        string cropFileName = $"desktop_img{imgIdx + 1}_btn{btnIdx + 1}.png";
                        string cropPath = System.IO.Path.Combine(outputDir, cropFileName);
                        Cv2.ImWrite(cropPath, crop);
                    }
                }
            }
        }

        [Fact]
        public void CropAllWallTestImageButtons()
        {
            string inputDir = Path.Combine(Directory.GetCurrentDirectory(), "scratch", "walltestimage");
            if (!Directory.Exists(inputDir))
            {
                inputDir = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "scratch", "walltestimage"));
            }

            string cropsDir = Path.Combine(inputDir, "button_crops");
            if (Directory.Exists(cropsDir))
            {
                Directory.Delete(cropsDir, recursive: true);
            }
            Directory.CreateDirectory(cropsDir);

            string[] imagePaths = Directory.GetFiles(inputDir, "*.png")
                .OrderBy(path => Path.GetFileName(path), StringComparer.Ordinal)
                .ToArray();

            using var vision = CreateVisionEngine();
            var csvLines = new List<string>
            {
                "source,mode,button_count,index,x,y,width,height,label,crop_path"
            };

            int totalButtonsCropped = 0;
            int totalImagesProcessed = 0;

            foreach (string imagePath in imagePaths)
            {
                string fileName = Path.GetFileName(imagePath);
                string folderName = Path.GetFileNameWithoutExtension(imagePath);

                using Mat screenshot = Cv2.ImRead(imagePath);
                if (screenshot.Empty()) continue;

                totalImagesProcessed++;
                WallPanelLocalizationResult panel = WallDynamicLocalizer.LocalizePanelAndButtons(vision, screenshot);

                string imageSubDir = Path.Combine(cropsDir, folderName);
                Directory.CreateDirectory(imageSubDir);

                for (int btnIdx = 0; btnIdx < panel.DetectedButtons.Count; btnIdx++)
                {
                    Rect btnRect = panel.DetectedButtons[btnIdx];
                    string label;
                    if (panel.GoldInfo.Found && panel.GoldInfo.ButtonRect == btnRect)
                    {
                        label = "gold_upgrade";
                    }
                    else if (panel.ElixirInfo.Found && panel.ElixirInfo.ButtonRect == btnRect)
                    {
                        label = "elixir_upgrade";
                    }
                    else
                    {
                        label = $"button_{(btnIdx + 1):D2}";
                    }

                    string cropFileName = $"{(btnIdx + 1):D2}_{label}_x{btnRect.X}_y{btnRect.Y}_w{btnRect.Width}_h{btnRect.Height}.png";
                    string cropPath = Path.Combine(imageSubDir, cropFileName);
                    string relativeCropPath = Path.Combine(folderName, cropFileName);

                    Rect clamped = ImageUtils.ClampRect(btnRect, screenshot.Width, screenshot.Height);
                    if (clamped.Width > 0 && clamped.Height > 0)
                    {
                        using Mat crop = new Mat(screenshot, clamped);
                        Cv2.ImWrite(cropPath, crop);
                        totalButtonsCropped++;
                    }

                    csvLines.Add($"\"{fileName}\",{panel.ResourceMode},{panel.DetectedButtons.Count},{btnIdx + 1},{btnRect.X},{btnRect.Y},{btnRect.Width},{btnRect.Height},{label},\"{relativeCropPath}\"");
                }
            }

            File.WriteAllLines(Path.Combine(cropsDir, "summary.csv"), csvLines);
            File.WriteAllText(Path.Combine(cropsDir, "README.txt"), $"Button crops generated from all wall test images in {inputDir}.\nTotal images processed: {totalImagesProcessed}\nTotal buttons cropped: {totalButtonsCropped}\n");
        }

        [Fact]
        public void VerifyOcrAccuracyOnAllTestImages()
        {
            string inputDir = Path.Combine(Directory.GetCurrentDirectory(), "scratch", "walltestimage");
            if (!Directory.Exists(inputDir))
            {
                inputDir = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "scratch", "walltestimage"));
            }

            string[] imagePaths = Directory.GetFiles(inputDir, "wall_*.png")
                .OrderBy(path => Path.GetFileName(path), StringComparer.Ordinal)
                .ToArray();

            using var vision = CreateVisionEngine();
            int totalImages = imagePaths.Length;
            int totalOcrEvaluations = 0;
            int successfulOcr = 0;
            int failedOcr = 0;
            double totalConfidenceSum = 0;

            var details = new List<string>();

            foreach (string imagePath in imagePaths)
            {
                string name = Path.GetFileName(imagePath);
                using Mat screenshot = Cv2.ImRead(imagePath);
                if (screenshot.Empty()) continue;

                WallPanelLocalizationResult panel = WallDynamicLocalizer.LocalizePanelAndButtons(vision, screenshot);

                if (panel.ResourceMode == WallUpgradeResourceMode.FullyUpgraded)
                {
                    details.Add($"[PASS] {name}: FullyUpgraded (No OCR expected)");
                    continue;
                }

                if (panel.GoldInfo.Found && panel.GoldInfo.CostRoiVerified)
                {
                    totalOcrEvaluations++;
                    bool ok = WallUpdater.TryReadWallUpgradeCost(vision, screenshot, panel.GoldInfo.CostRoi, out int goldCost, out double conf);
                    if (ok && goldCost > 0)
                    {
                        successfulOcr++;
                        totalConfidenceSum += conf;
                        details.Add($"[PASS] {name} | GOLD: {goldCost:N0} (conf={conf:F2})");
                    }
                    else
                    {
                        failedOcr++;
                        details.Add($"[FAIL] {name} | GOLD OCR FAILED: read={ok}, cost={goldCost}, conf={conf:F2}");
                    }
                }

                if (panel.ResourceMode == WallUpgradeResourceMode.GoldAndElixir && panel.ElixirInfo.Found && panel.ElixirInfo.CostRoiVerified)
                {
                    totalOcrEvaluations++;
                    bool ok = WallUpdater.TryReadWallUpgradeCost(vision, screenshot, panel.ElixirInfo.CostRoi, out int elixirCost, out double conf);
                    if (ok && elixirCost > 0)
                    {
                        successfulOcr++;
                        totalConfidenceSum += conf;
                        details.Add($"[PASS] {name} | ELIXIR: {elixirCost:N0} (conf={conf:F2})");
                    }
                    else
                    {
                        failedOcr++;
                        details.Add($"[FAIL] {name} | ELIXIR OCR FAILED: read={ok}, cost={elixirCost}, conf={conf:F2}");
                    }
                }
            }

            double passRate = (double)successfulOcr / totalOcrEvaluations * 100.0;
            double avgConf = totalConfidenceSum / Math.Max(1, successfulOcr);

            System.Console.WriteLine($"=== OCR ACCURACY REPORT ===");
            System.Console.WriteLine($"Total Images: {totalImages}");
            System.Console.WriteLine($"Total OCR Evaluated ROI Slots: {totalOcrEvaluations}");
            System.Console.WriteLine($"Successful Reads: {successfulOcr}");
            System.Console.WriteLine($"Failed Reads: {failedOcr}");
            System.Console.WriteLine($"Accuracy Pass Rate: {passRate:F2}%");
            System.Console.WriteLine($"Average Confidence Score: {avgConf:F2}");

            Assert.Equal(0, failedOcr);
        }

    }
}
