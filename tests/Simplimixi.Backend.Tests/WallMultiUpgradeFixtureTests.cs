using System.Security.Cryptography;
using System.Text.Json;
using CvAut;
using OpenCvSharp;
using Xunit;

namespace CvAut.Backend.Tests;

public sealed class WallMultiUpgradeFixtureTests
{
    private static string FixtureDir => Path.Combine(AppContext.BaseDirectory, "Fixtures", "WallQuantity");
    private static string TemplatesDir => Path.Combine(AppContext.BaseDirectory, "assets", "Templates");

    [Fact]
    public void Manifest_CoversExactlySixteenVerifiedFixtures()
    {
        string manifestPath = Path.Combine(FixtureDir, "manifest.json");
        Assert.True(File.Exists(manifestPath));
        using JsonDocument doc = JsonDocument.Parse(File.ReadAllText(manifestPath));
        JsonElement fixtures = doc.RootElement.GetProperty("fixtures");
        Assert.Equal(16, fixtures.GetArrayLength());
        Assert.Equal(16, Directory.GetFiles(FixtureDir, "*.png").Length);
        foreach (JsonElement fixture in fixtures.EnumerateArray())
        {
            string file = fixture.GetProperty("file").GetString()!;
            string path = Path.Combine(FixtureDir, file);
            Assert.True(File.Exists(path), file);
            string actual = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))).ToLowerInvariant();
            Assert.Equal(fixture.GetProperty("sha256").GetString(), actual);
        }
    }

    [Fact]
    public void BatchTotal_X21_Reads420kWithoutSingleCostWhitelist()
    {
        using Mat screenshot = Load("multi_x21_total420k.png");
        using var vision = new VisionEngine(TemplatesDir);
        WallPanelLocalizationResult panel = WallDynamicLocalizer.LocalizePanelAndButtons(vision, screenshot);
        Assert.True(panel.GoldInfo.Found);
        Assert.True(WallBatchTotalReader.TryRead(vision, screenshot, panel.GoldInfo.CostRoi, out long total, out double confidence));
        Assert.Equal(420_000, total);
        Assert.True(confidence >= 0.70);
        Assert.True(WallBatchTotalReader.Validate(total, 20_000, 21));
        Assert.NotEqual(20_000, total);
    }

    [Fact]
    public void Header_X21_IsMultiAndReadsSelectedCount()
    {
        using Mat screenshot = Load("multi_x21_total420k.png");
        using var vision = new VisionEngine(TemplatesDir);
        WallHeaderInfo header = WallHeaderInspector.Inspect(vision, screenshot);
        Assert.True(header.Found, header.Reason);
        Assert.True(header.Mode == WallSelectionMode.Multi, header.Reason);
        Assert.True(header.SelectedCount == 21, $"count={header.SelectedCount}; {header.Reason}");
    }

    [Theory]
    [InlineData("single_level1_gold_1k.png")]
    [InlineData("single_level4_total20k.png")]
    public void SinglePanel_LocalizesUpgradeMore(string file)
    {
        using Mat screenshot = Load(file);
        using var vision = new VisionEngine(TemplatesDir);
        WallQuantityPanelInfo panel = WallQuantityControlLocalizer.Localize(vision, screenshot);
        Assert.True(panel.Header.Mode == WallSelectionMode.Single, panel.Header.Reason);
        WallQuantityControlInfo control = Assert.Single(panel.Controls, c => c.Role == WallQuantityControlRole.UpgradeMore);
        Assert.True(control.Found);
        Assert.True(control.ButtonRect.Contains(control.TapPoint));
    }

    [Fact]
    public void MultiPanel_X11_LocalizesIndependentAddTenAndAddOneControls()
    {
        using Mat screenshot = Load("multi_x11_total220k.png");
        using var vision = new VisionEngine(TemplatesDir);
        WallQuantityPanelInfo panel = WallQuantityControlLocalizer.Localize(vision, screenshot);
        WallPanelLocalizationResult raw = panel.Panel;
        WallUpdater.TryReadWallUpgradeCost(vision, screenshot, raw.GoldInfo.CostRoi, out int goldSingle, out _);
        WallBatchTotalReader.TryRead(vision, screenshot, raw.GoldInfo.CostRoi, out long goldBatch, out _);
        WallUpdater.TryReadWallUpgradeCost(vision, screenshot, raw.ElixirInfo.CostRoi, out int elixirSingle, out _);
        WallBatchTotalReader.TryRead(vision, screenshot, raw.ElixirInfo.CostRoi, out long elixirBatch, out _);
        Assert.True(panel.Header.Mode == WallSelectionMode.Multi,
            $"{panel.Header.Reason}; gold={goldSingle}/{goldBatch}; elixir={elixirSingle}/{elixirBatch}");
        Assert.Equal(11, panel.Header.SelectedCount);
        WallQuantityControlInfo addTen = Assert.Single(panel.Controls, c => c.Role == WallQuantityControlRole.AddTen);
        WallQuantityControlInfo addOne = Assert.Single(panel.Controls, c => c.Role == WallQuantityControlRole.AddOne);
        Assert.Equal(10, addTen.Delta);
        Assert.Equal(1, addOne.Delta);
        Assert.NotEqual(addTen.TapPoint, addOne.TapPoint);
    }

    [Theory]
    [InlineData("single_confirm_gold.png", WallConfirmationKind.SingleConfirm)]
    [InlineData("single_confirm_elixir.png", WallConfirmationKind.SingleConfirm)]
    [InlineData("multi_confirm_gold_620k.png", WallConfirmationKind.MultiCancelOkay)]
    [InlineData("multi_confirm_elixir_620k.png", WallConfirmationKind.MultiCancelOkay)]
    public void ConfirmationKind_IsStructurallyDistinguished(string file, WallConfirmationKind expected)
    {
        using Mat screenshot = Load(file);
        WallConfirmDialogInfo info = WallConfirmDialogInspector.Inspect(screenshot);
        Assert.True(info.Found, info.Reason);
        Assert.Equal(expected, info.Kind);
        Assert.True(info.ConfirmButton.Contains(info.ConfirmPoint));
        Assert.Equal(expected == WallConfirmationKind.MultiCancelOkay, info.CancelButton.Width > 0);
    }

    [Theory]
    [InlineData("single_confirm_gold.png", "gold")]
    [InlineData("single_confirm_elixir.png", "elixir")]
    public void SingleConfirmationBranch_VerifiesKindAndResource(string file, string resource)
    {
        using Mat screenshot = Load(file);
        using var vision = new VisionEngine(TemplatesDir);
        WallConfirmationValidation result = WallConfirmationFlow.Validate(vision, screenshot, resource, 1, 20_000, 20_000);
        Assert.True(result.Valid, result.Reason);
        Assert.Equal(WallConfirmationKind.SingleConfirm, result.Kind);
        Assert.Equal(resource, result.Resource);
    }

    [Theory]
    [InlineData("multi_confirm_gold_620k.png", "gold")]
    [InlineData("multi_confirm_elixir_620k.png", "elixir")]
    public void MultiConfirmationBranch_VerifiesKindResourceAndTotal(string file, string resource)
    {
        using Mat screenshot = Load(file);
        using var vision = new VisionEngine(TemplatesDir);
        WallConfirmationValidation result = WallConfirmationFlow.Validate(vision, screenshot, resource, 31, 20_000, 620_000);
        Assert.True(result.Valid, $"{result.Reason}; resource={result.Resource}; total={result.Total}");
        Assert.Equal(WallConfirmationKind.MultiCancelOkay, result.Kind);
        Assert.Equal(620_000, result.Total);
    }

    [Fact]
    public void MultiConfirmationBranch_WrongExpectedTotal_FailsClosed()
    {
        using Mat screenshot = Load("multi_confirm_gold_620k.png");
        using var vision = new VisionEngine(TemplatesDir);
        WallConfirmationValidation result = WallConfirmationFlow.Validate(vision, screenshot, "gold", 30, 20_000, 620_000);
        Assert.False(result.Valid);
        Assert.Equal("confirmation_prevalidated_total_mismatch", result.Reason);
    }

    [Fact]
    public void ConfirmationBranch_WrongResource_FailsClosed()
    {
        using Mat screenshot = Load("multi_confirm_gold_620k.png");
        using var vision = new VisionEngine(TemplatesDir);
        WallConfirmationValidation result = WallConfirmationFlow.Validate(vision, screenshot, "elixir", 31, 20_000, 620_000);
        Assert.False(result.Valid);
        Assert.Equal("confirmation_resource_mismatch", result.Reason);
    }

    [Fact]
    public void ConfirmationResourceTemplates_UseStableProductionNames()
    {
        string directory = Path.Combine(TemplatesDir, "walls", "main_village", "confirmation");
        string[] required =
        {
            "single_gold_icon.png",
            "single_elixir_icon.png",
            "multi_gold_resource.png",
            "multi_elixir_resource.png"
        };
        foreach (string file in required)
        {
            Assert.DoesNotContain("UNUSED", file, StringComparison.OrdinalIgnoreCase);
            Assert.True(File.Exists(Path.Combine(directory, file)), file);
        }
    }

    [Theory]
    [InlineData("multi_confirm_gold_620k.png", "gold")]
    [InlineData("multi_confirm_elixir_620k.png", "elixir")]
    public void MultiResourceDetection_DoesNotDependOnAmountPixels(string file, string resource)
    {
        using Mat screenshot = Load(file);
        // Destroy the compact amount region while preserving the tight resource-only template ROI.
        Cv2.Rectangle(screenshot, new Rect(785, 398, 110, 55), new Scalar(150, 150, 150), -1);
        using var vision = new VisionEngine(TemplatesDir);
        WallConfirmationValidation result = WallConfirmationFlow.Validate(
            vision, screenshot, resource, 31, 20_000, 620_000);
        Assert.True(result.Valid, $"{result.Reason}; observed_resource={result.Resource}");
        Assert.Equal(resource, result.Resource);
    }

    private static Mat Load(string file)
    {
        string path = Path.Combine(FixtureDir, file);
        Assert.True(File.Exists(path), path);
        Mat image = Cv2.ImRead(path);
        Assert.False(image.Empty(), path);
        return image;
    }
}
