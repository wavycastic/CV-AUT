using System;
using System.Collections.Generic;
using System.Threading;
using OpenCvSharp;

namespace CvAut.Adb
{
    /// <summary>
    /// Compatibility facade exposing the legacy helper surface while delegating work to capabilities.
    /// </summary>
    internal sealed class AdbHelperFacade : IADBHelper
    {
        private readonly AdbCapabilityCoordinator _coordinator;

        public AdbHelperFacade(string host = "127.0.0.1", int port = 5556, string? preferredSerial = null)
            : this(AdbCapabilityCoordinator.Connect(host, port, preferredSerial))
        {
        }

        internal AdbHelperFacade(AdbCapabilityCoordinator coordinator)
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

        public bool IsDeviceConnected() => _coordinator.IsConnected;

        public string ExecuteShell(string command) => _coordinator.ExecuteShell(command);

        public void Tap(int x, int y) => _coordinator.Tap(x, y);

        public void Swipe(int startX, int startY, int endX, int endY, int durationMs = 300)
            => _coordinator.Swipe(startX, startY, endX, endY, durationMs);

        public void TapSequence(IEnumerable<Point> points)
            => _coordinator.TapSequence(points);

        public void TapSequenceSafeFast(
            IEnumerable<Point> points,
            int batchSize = 4,
            int batchDelayMs = 90,
            CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(points);
            if (batchSize <= 0) throw new ArgumentOutOfRangeException(nameof(batchSize));
            if (batchDelayMs < 0) throw new ArgumentOutOfRangeException(nameof(batchDelayMs));

            var batch = new List<Point>(batchSize);
            foreach (Point point in points)
            {
                if (token.IsCancellationRequested) return;
                batch.Add(point);
                if (batch.Count < batchSize) continue;

                _coordinator.TapSequence(batch);
                batch.Clear();
                int adaptedDelay = FramePacer.AdjustDelay(batchDelayMs);
                if (token.WaitHandle.WaitOne(adaptedDelay)) return;
            }

            if (batch.Count > 0 && !token.IsCancellationRequested)
                _coordinator.TapSequence(batch);
        }

        public Mat? TakeScreenshot() => _coordinator.TakeScreenshot();

        public void PinchIn(int centerX = 800, int centerY = 450)
            => _coordinator.PinchInZoomOut(count: 1);

        public bool PinchInZoomOut(int count = 5, int durationMs = 450, int intervalMs = 350)
            => _coordinator.PinchInZoomOut(count, durationMs, intervalMs);

        public void Dispose() => _coordinator.Dispose();
    }
}
