using System;
using System.Collections.Generic;
using System.Threading;
using CvAut.Adb;
using OpenCvSharp;
using Point = OpenCvSharp.Point;

namespace CvAut
{
    /// <summary>
    /// Backward-compatible ADB facade. Device connection, shell execution, input,
    /// screenshots, and UIAutomator gestures are delegated to focused capabilities.
    /// </summary>
    public class ADBHelper : IADBHelper
    {
        private readonly AdbCapabilityCoordinator _coordinator;

        public ADBHelper(string host = "127.0.0.1", int port = 5556, string? preferredSerial = null)
            : this(AdbCapabilityCoordinator.Connect(host, port, preferredSerial))
        {
        }

        internal ADBHelper(AdbCapabilityCoordinator coordinator)
        {
            _coordinator = coordinator ?? throw new ArgumentNullException(nameof(coordinator));
        }

        public string Host => _coordinator.Host;
        public int Port => _coordinator.Port;
        public string DeviceAddress => _coordinator.DeviceAddress;
        public FramePacer FramePacer => _coordinator.FramePacer;

        public Func<bool>? BeforeInputAction
        {
            get => _coordinator.BeforeInputAction;
            set => _coordinator.BeforeInputAction = value;
        }

        public bool IsDeviceConnected() => _coordinator.IsDeviceConnected();

        public bool EnsureConnectedOnline(int timeoutSeconds = 30)
            => _coordinator.EnsureConnectedOnline(timeoutSeconds);

        public string GetDeviceState() => _coordinator.GetDeviceState();

        public string ExecuteShell(string command) => _coordinator.ExecuteShell(command);

        public void Tap(int x, int y) => _coordinator.Tap(x, y);

        public void Swipe(int x1, int y1, int x2, int y2, int durationMs = 300)
            => _coordinator.Swipe(x1, y1, x2, y2, durationMs);

        public void TapSequence(IEnumerable<Point> points)
            => _coordinator.TapSequence(points);

        public void TapSequenceSafeFast(
            IEnumerable<Point> points,
            int batchSize = 4,
            int batchDelayMs = 90)
            => TapSequenceSafeFast(points, batchSize, batchDelayMs, CancellationToken.None);

        public void TapSequenceSafeFast(
            IEnumerable<Point> points,
            int batchSize,
            int batchDelayMs,
            CancellationToken token)
        {
            ArgumentNullException.ThrowIfNull(points);
            int effectiveBatchSize = Math.Max(1, batchSize);
            var batch = new List<Point>(effectiveBatchSize);

            foreach (Point point in points)
            {
                if (token.IsCancellationRequested) return;
                batch.Add(point);
                if (batch.Count < effectiveBatchSize) continue;

                TapSequence(batch);
                batch.Clear();
                int adaptedDelay = FramePacer.AdjustDelay(batchDelayMs);
                if (token.WaitHandle.WaitOne(adaptedDelay)) return;
            }

            if (batch.Count > 0 && !token.IsCancellationRequested)
                TapSequence(batch);
        }

        public Mat? TakeScreenshot() => _coordinator.TakeScreenshot();

        public void PinchIn(int centerX = 800, int centerY = 450)
            => PinchInZoomOut();

        public bool PinchInZoomOut(int count = 5, int durationMs = 450, int intervalMs = 350)
            => _coordinator.PinchInZoomOut(count, durationMs, intervalMs);

        public void Dispose() => _coordinator.Dispose();
    }
}
