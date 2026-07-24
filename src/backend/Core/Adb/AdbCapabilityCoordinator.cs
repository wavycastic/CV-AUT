using System;
using System.Collections.Generic;
using System.Threading;
using OpenCvSharp;

namespace CvAut.Adb
{
    /// <summary>
    /// Coordinates independently testable ADB capabilities behind the legacy helper surface.
    /// </summary>
    internal sealed class AdbCapabilityCoordinator : IDisposable
    {
        private readonly AdbDeviceConnection _connection;
        private readonly IAdbShellExecutor _shell;
        private readonly IAdbInputController _input;
        private readonly IAdbScreenCapturer _screenCapturer;
        private readonly IUiAutomatorGestureClient _gestureClient;
        private readonly IAdbCommandRunner? _commandRunner;
        private bool _disposed;

        internal AdbCapabilityCoordinator(
            AdbDeviceConnection connection,
            IAdbShellExecutor shell,
            IAdbInputController input,
            IAdbScreenCapturer screenCapturer,
            IUiAutomatorGestureClient gestureClient,
            IAdbCommandRunner? commandRunner = null)
        {
            _connection = connection ?? throw new ArgumentNullException(nameof(connection));
            _shell = shell ?? throw new ArgumentNullException(nameof(shell));
            _input = input ?? throw new ArgumentNullException(nameof(input));
            _screenCapturer = screenCapturer ?? throw new ArgumentNullException(nameof(screenCapturer));
            _gestureClient = gestureClient ?? throw new ArgumentNullException(nameof(gestureClient));
            _commandRunner = commandRunner;
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
                new UiAutomatorGestureClient(connection.DeviceAddress, runner),
                runner);
        }

        public bool IsDeviceConnected()
            => string.Equals(GetDeviceState(), "device", StringComparison.OrdinalIgnoreCase);

        public string GetDeviceState()
        {
            ThrowIfDisposed();
            if (_commandRunner is null)
                return _connection.IsConnected ? "device" : "unknown";

            string result = _commandRunner.RunAdbCommand(DeviceAddress, "get-state").Trim();
            return string.IsNullOrWhiteSpace(result) || IsError(result) ? "unknown" : result;
        }

        public bool EnsureConnectedOnline(int timeoutSeconds = 30)
        {
            ThrowIfDisposed();
            if (timeoutSeconds <= 0) return false;

            DateTime deadline = DateTime.UtcNow.AddSeconds(timeoutSeconds);
            while (DateTime.UtcNow < deadline)
            {
                _commandRunner?.RunRawAdbCommand($"connect {Host}:{Port}");
                if (IsDeviceConnected()) return true;
                ThreadingUtil.InterruptibleSleep(1000);
            }
            return false;
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
            string details = $"x={x} y={y}";
            LogInput("bot_tap", "send", details);
            return HandleInputResult("bot_tap", details, _input.Tap(x, y));
        }

        public bool Swipe(int x1, int y1, int x2, int y2, int durationMs = 300)
        {
            ThrowIfDisposed();
            if (IsInputBlocked()) return false;
            string details = $"x1={x1} y1={y1} x2={x2} y2={y2} duration_ms={durationMs}";
            LogInput("bot_swipe", "send", details);
            return HandleInputResult("bot_swipe", details, _input.Swipe(x1, y1, x2, y2, durationMs));
        }

        public bool TapSequence(IEnumerable<Point> points)
        {
            ThrowIfDisposed();
            ArgumentNullException.ThrowIfNull(points);
            if (IsInputBlocked()) return false;
            return !IsError(_input.TapSequence(points));
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
            if (IsInputBlocked() || count <= 0) return false;
            if (_gestureClient.PinchIn(count, 100, 20, intervalMs, token)) return true;

            Console.WriteLine("[ADB WARNING] phase=pinch status=retry action=pinch reason=\"pinch_unsupported\" details=\"swipe_fallback\"");
            bool anySuccess = false;
            for (int index = 0; index < count && !token.IsCancellationRequested; index++)
            {
                string result = _shell.Execute(
                    "sh -c \"input swipe 360 450 790 450 " + durationMs +
                    " & input swipe 1240 450 810 450 " + durationMs +
                    " & wait\"");
                anySuccess |= !IsError(result);
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

        private static bool HandleInputResult(string action, string details, string result)
        {
            if (!IsError(result)) return true;
            LogInput(action, "fail", details + " reason=\"" + result + "\"");
            return false;
        }

        private static bool IsError(string result)
            => result.StartsWith("Error:", StringComparison.OrdinalIgnoreCase);

        private static void LogInput(string action, string status, string details)
            => Console.WriteLine($"[ADB] phase=input status={status} action={action} {details}");

        private void ThrowIfDisposed()
            => ObjectDisposedException.ThrowIf(_disposed, this);
    }
}
