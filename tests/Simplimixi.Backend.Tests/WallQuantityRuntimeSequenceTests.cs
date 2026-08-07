using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using CvAut;
using OpenCvSharp;
using Xunit;

namespace CvAut.Backend.Tests;

public sealed class WallQuantityRuntimeSequenceTests
{
    private static string FixtureDir => Path.Combine(AppContext.BaseDirectory, "Fixtures", "WallQuantity");
    private static string TemplatesDir => Path.Combine(AppContext.BaseDirectory, "assets", "Templates");

    [Fact]
    public void MultiX11_ToX21_UsesLocalizedAddTenAndVerifiesDelta()
    {
        using var adb = SequenceAdb.FromFixtures("multi_x11_total220k.png", "multi_x11_total220k.png", "multi_x21_total420k.png");
        using var vision = new VisionEngine(TemplatesDir);
        using Mat before = Load("multi_x11_total220k.png");
        WallQuantityPanelInfo localized = WallQuantityControlLocalizer.Localize(vision, before);
        Point expectedTap = Assert.Single(localized.Controls, c => c.Role == WallQuantityControlRole.AddTen).TapPoint;

        int selected = new WallQuantityAdjuster(adb, vision).AddWallsSafely(
            "gold", 21, 255, CancellationToken.None, "sequence_test", "add10", 1,
            new WallResourceButtonInfo { Found = true }, 20_000);

        Assert.Equal(21, selected);
        Assert.Equal(expectedTap, Assert.Single(adb.Taps));
        Assert.Equal(3, adb.ScreenshotCount);
    }

    [Fact]
    public void MultiX2_ToX3_UsesLocalizedAddOneAndVerifiesDelta()
    {
        using var adb = SequenceAdb.FromFixtures("multi_x2_total40k.png", "multi_x2_total40k.png", "multi_x3_total60k.png");
        using var vision = new VisionEngine(TemplatesDir);
        using Mat before = Load("multi_x2_total40k.png");
        WallQuantityPanelInfo localized = WallQuantityControlLocalizer.Localize(vision, before);
        Point expectedTap = Assert.Single(localized.Controls, c => c.Role == WallQuantityControlRole.AddOne).TapPoint;

        int selected = new WallQuantityAdjuster(adb, vision).AddWallsSafely(
            "elixir", 3, 255, CancellationToken.None, "sequence_test", "add1", 1,
            new WallResourceButtonInfo { Found = true }, 20_000);

        Assert.Equal(3, selected);
        Assert.Equal(expectedTap, Assert.Single(adb.Taps));
        Assert.Equal(3, adb.ScreenshotCount);
    }

    [Fact]
    public void HeaderCountUnchangedAfterTap_FailsClosedWithoutSecondTap()
    {
        using var adb = SequenceAdb.FromFixtures("multi_x2_total40k.png", "multi_x2_total40k.png", "multi_x2_total40k.png");
        using var vision = new VisionEngine(TemplatesDir);

        int selected = new WallQuantityAdjuster(adb, vision).AddWallsSafely(
            "gold", 3, 255, CancellationToken.None, "sequence_test", "unchanged", 1,
            new WallResourceButtonInfo { Found = true }, 20_000);

        Assert.Equal(0, selected);
        Assert.Single(adb.Taps);
        Assert.Equal(3, adb.ScreenshotCount);
    }

    [Fact]
    public void BatchTotalMismatchAfterVerifiedHeaderDelta_FailsClosed()
    {
        using var adb = SequenceAdb.FromFixtures("multi_x2_total40k.png", "multi_x2_total40k.png", "multi_x3_total60k.png");
        using var vision = new VisionEngine(TemplatesDir);

        int selected = new WallQuantityAdjuster(adb, vision).AddWallsSafely(
            "gold", 3, 255, CancellationToken.None, "sequence_test", "bad_total", 1,
            new WallResourceButtonInfo { Found = true }, 30_000);

        Assert.Equal(0, selected);
        Assert.Single(adb.Taps);
        Assert.Equal(3, adb.ScreenshotCount);
    }

    [Fact]
    public void ScreenshotMissingAfterTap_FailsClosedWithoutRetryTap()
    {
        using var adb = SequenceAdb.FromSequence(Load("multi_x2_total40k.png"), Load("multi_x2_total40k.png"), null);
        using var vision = new VisionEngine(TemplatesDir);

        int selected = new WallQuantityAdjuster(adb, vision).AddWallsSafely(
            "gold", 3, 255, CancellationToken.None, "sequence_test", "missing_after", 1,
            new WallResourceButtonInfo { Found = true }, 20_000);

        Assert.Equal(0, selected);
        Assert.Single(adb.Taps);
        Assert.Equal(3, adb.ScreenshotCount);
    }

    [Fact]
    public void CancelledBeforePlanning_DoesNotTap()
    {
        using var adb = SequenceAdb.FromFixtures("multi_x2_total40k.png");
        using var vision = new VisionEngine(TemplatesDir);
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        int selected = new WallQuantityAdjuster(adb, vision).AddWallsSafely(
            "gold", 3, 255, cts.Token, "sequence_test", "cancelled", 1,
            new WallResourceButtonInfo { Found = true }, 20_000);

        Assert.Equal(0, selected);
        Assert.Empty(adb.Taps);
        Assert.Equal(1, adb.ScreenshotCount);
    }

    private static Mat Load(string file) => FixtureLoader.LoadMandatory(Path.Combine(FixtureDir, file));

    private sealed class SequenceAdb : IADBHelper
    {
        private readonly Queue<Mat?> _screenshots;
        private readonly List<Mat> _owned;

        private SequenceAdb(IEnumerable<Mat?> screenshots)
        {
            _screenshots = new Queue<Mat?>(screenshots);
            _owned = new List<Mat>();
            foreach (Mat? screenshot in _screenshots)
                if (screenshot != null) _owned.Add(screenshot);
        }

        public static SequenceAdb FromFixtures(params string[] files)
            => FromSequence(Array.ConvertAll(files, file => (Mat?)Load(file)));

        public static SequenceAdb FromSequence(params Mat?[] screenshots) => new(screenshots);

        public List<Point> Taps { get; } = new();
        public int ScreenshotCount { get; private set; }
        public string Host => "127.0.0.1";
        public int Port => 5555;
        public string DeviceAddress => "127.0.0.1:5555";
        public FramePacer FramePacer { get; } = new();
        public Func<bool>? BeforeInputAction { get; set; }
        public bool IsDeviceConnected() => true;
        public bool EnsureConnectedOnline(int timeoutSeconds = 30) => true;
        public string GetDeviceState() => "device";
        public string ExecuteShell(string command) => string.Empty;
        public void Tap(int x, int y) => Taps.Add(new Point(x, y));
        public void TapSequence(IEnumerable<Point> points) => Taps.AddRange(points);
        public void TapSequenceSafeFast(IEnumerable<Point> points, int batchSize = 4, int batchDelayMs = 90) => Taps.AddRange(points);
        public void TapSequenceSafeFast(IEnumerable<Point> points, int batchSize, int batchDelayMs, CancellationToken token) => Taps.AddRange(points);
        public void Swipe(int startX, int startY, int endX, int endY, int durationMs = 300) { }
        public Mat? TakeScreenshot()
        {
            ScreenshotCount++;
            if (_screenshots.Count == 0) return null;
            return _screenshots.Dequeue()?.Clone();
        }
        public void PinchIn(int centerX = 800, int centerY = 450) { }
        public bool PinchInZoomOut(int count = 5, int durationMs = 450, int intervalMs = 350) => true;
        public void Dispose()
        {
            foreach (Mat screenshot in _owned) screenshot.Dispose();
            _owned.Clear();
        }
    }
}
