using System;
using System.Collections.Generic;
using System.Threading;
using OpenCvSharp;

namespace CvAut.Adb
{
    /// <summary>
    /// Thin facade over independently testable ADB capabilities.
    /// </summary>
    internal sealed class AdbCapabilityCoordinator : IDisposable
    {
        private readonly AdbDeviceConnection _connection;
        private readonly IAdbShellExecutor _shell;
        private readonly IAdbInputController _input;
        private readonly IAdbScreenCapturer _screenCapturer;
        private readonly IUiAutomatorGestureClient _gestureClient;
        private bool _disposed;

        internal AdbCapabilityCoordinator(
            AdbDeviceConnection connection,
            IAdbShellExecutor shell,
            IAdbInputController input,
            IAdbScreenCapturer screenCapturer,
            IUiAutomatorGestureClient gestureClient)
        {
            _connection = connection ?? throw new ArgumentNullException(nameof(connection));
            _shell = shell ?? throw new ArgumentNullException(nameof(shell));
            _input = input ?? throw new ArgumentNullException(nameof(input));
            _screenCapturer = screenCapturer ?? throw new ArgumentNullException(nameof(screenCapturer));
            _gestureClient = gestureClient ?? throw new ArgumentNullException(nameof(gestureClient));
        }

        public string Host => _connection.Host;
        public int Port => _connection.Port;
        public string DeviceAddress => _connection.DeviceAddress;
        public bool IsConnected => _connection.IsConnected;
        public Func<bool>? BeforeInputAction { get; set; }
        public FramePacer FramePacer { get; } = new();

        public static AdbCapabilityCoordinator Connect(
            string host = "127.0.0.1",
            int port = 5556,
            string? preferredSerial = null)
        {
            var runner = new AdbProcessRunner();
            var connector = new AdbDeviceConnector(runner);
            AdbDeviceConnection connection = connector.Connect(host, port, preferredSerial);
            var shell = new SharpAdbShellExecutor(connection.Device);
            return new AdbCapabilityCoordinator(
                connection,
                shell,
                new AdbInputController(shell),
                new AdbScreenCapturer(),
                new UiAutomatorGestureClient(connection.DeviceAddress, runner));
        }

        public string ExecuteShell(string command)
        {
            ThrowIfDisposed();
            return _shell.Execute(command);
        }

        public bool Tap(int x, int y)
        {
            ThrowIfDisposed();
            if (IsInputBlocked()) return false;
            return IsSuccess(_input.Tap(x, y));
        }

        public bool Swipe(int x1, int y1, int x2, int y2, int durationMs = 300)
        {
            ThrowIfDisposed();
            if (IsInputBlocked()) return false;
            return IsSuccess(_input.Swipe(x1, y1, x2, y2, durationMs));
        }

        public bool TapSequence(IEnumerable<Point> points)
        {
            ThrowIfDisposed();
            ArgumentNullException.ThrowIfNull(points);
            if (IsInputBlocked()) return false;
            return IsSuccess(_input.TapSequence(points));
        }

        public Mat? TakeScreenshot(CancellationToken token = default)
        {
            ThrowIfDisposed();
            return _screenCapturer.Capture(DeviceAddress, FramePacer, token);
        }

        public bool PinchInZoomOut(
            int count = 5,
            int durationMs = 450,
            int intervalMs = 350,
            CancellationToken token = default)
        {
            ThrowIfDisposed();
            if (IsInputBlocked()) return false;
            if (_gestureClient.PinchIn(count, 100, 20, intervalMs, token)) return true;

            bool anySuccess = false;
            for (int index = 0; index < count && !token.IsCancellationRequested; index++)
            {
                string result = _shell.Execute(
                    "sh -c \"input swipe 360 450 790 450 " + durationMs +
                    " & input swipe 1240 450 810 450 " + durationMs +
                    " & wait\"");
                anySuccess |= IsSuccess(result);
                if (index < count - 1 && token.WaitHandle.WaitOne(intervalMs)) break;
            }
            return anySuccess;
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _gestureClient.Dispose();
        }

        private bool IsInputBlocked() => BeforeInputAction?.Invoke() == true;

        private static bool IsSuccess(string result)
            => !result.StartsWith("Error:", StringComparison.OrdinalIgnoreCase);

        private void ThrowIfDisposed()
            => ObjectDisposedException.ThrowIf(_disposed, this);
    }
}
