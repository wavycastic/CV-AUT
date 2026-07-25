using System;
using System.Collections.Generic;
using System.Linq;
using OpenCvSharp;

namespace CvAut.Adb
{
    /// <summary>
    /// Translates device input operations into Android shell commands.
    /// </summary>
    internal sealed class AdbInputController : IAdbInputController
    {
        private readonly IAdbShellExecutor _shell;

        public AdbInputController(IAdbShellExecutor shell)
        {
            _shell = shell ?? throw new ArgumentNullException(nameof(shell));
        }

        public string Tap(int x, int y)
            => _shell.Execute($"input tap {x} {y}");

        public string Swipe(int x1, int y1, int x2, int y2, int durationMs = 300)
            => _shell.Execute($"input swipe {x1} {y1} {x2} {y2} {durationMs}");

        public string TapSequence(IEnumerable<Point> points)
        {
            ArgumentNullException.ThrowIfNull(points);

            string[] commands = points
                .Select(point => $"input tap {point.X} {point.Y}")
                .ToArray();

            return commands.Length == 0
                ? string.Empty
                : _shell.Execute(string.Join("; ", commands));
        }
    }
}
