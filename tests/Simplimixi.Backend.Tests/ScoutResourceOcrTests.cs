using System;
using System.Collections.Generic;
using System.Reflection;
using OpenCvSharp;
using Xunit;

namespace CvAut.Backend.Tests;

public sealed class ScoutResourceOcrTests
{
    [Fact]
    public void ExtractResources_KnownFixture_ReadsNonZeroValues()
    {
        string templatesPath = System.IO.Path.Combine(AppContext.BaseDirectory, "assets", "Templates");
        string fixturePath = System.IO.Path.Combine(templatesPath, "ui", "enemy_resources.png");
        using Mat screenshot = Cv2.ImRead(fixturePath, ImreadModes.Color);
        using var vision = new VisionEngine(templatesPath);

        Assert.False(screenshot.Empty());
        var result = IsTarget.ExtractResources(screenshot, vision);

        Assert.InRange(result.Gold, 1, 10_000_000);
        Assert.InRange(result.Elixir, 1, 10_000_000);
        Assert.InRange(result.DarkElixir, 1, 100_000);
    }

    [Fact]
    public void TryExtractNumericalMetrics_DigitsShiftedBelowTopOfRoi_StillReadsValue()
    {
        using var ocr = new DigitOcrReader();
        using Mat screenshot = Mat.Zeros(60, 120, MatType.CV_8UC3);
        DrawDigits(ocr, screenshot, "420", startX: 10, startY: 22);

        bool read = ocr.TryExtractNumericalMetrics(
            screenshot,
            new Rect(0, 0, screenshot.Width, screenshot.Height),
            out int value,
            out double confidence,
            allowVerticalShift: true);

        Assert.True(read);
        Assert.Equal(420, value);
        Assert.True(confidence > 0.60);
    }

    private static void DrawDigits(DigitOcrReader ocr, Mat image, string digits, int startX, int startY)
    {
        var templates = (Dictionary<int, Mat>)typeof(DigitOcrReader)
            .GetField("_templates", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(ocr)!;

        foreach (char character in digits)
        {
            int digit = character - '0';
            for (int y = 0; y < 16; y++)
            {
                for (int x = 0; x < 12; x++)
                {
                    if (templates[digit].At<byte>(y, x) > 0)
                    {
                        image.Set(startY + y, startX + x, new Vec3b(255, 255, 255));
                    }
                }
            }

            startX += 14;
        }
    }
}
