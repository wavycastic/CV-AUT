using System;
using System.IO;
using System.Text;

namespace CvAut
{
    /// <summary>
    /// Captures everything the backend writes to <see cref="Console"/> and re-broadcasts
    /// it line-by-line via <see cref="LineWritten"/> so the UI can show a live log without
    /// the backend having to know anything about the UI. AOT-safe (no reflection).
    /// </summary>
    public static class AppLog
    {
        private static readonly object Gate = new object();
        private static bool _installed;

        /// <summary>Raised on whatever thread the backend logged from. Subscribers must marshal to the UI thread.</summary>
        public static event Action<string>? LineWritten;

        /// <summary>Ambient device scope for the current execution context. A session sets this on the
        /// thread that starts the backend worker; because ExecutionContext flows across Task.Run, every
        /// line the worker logs carries the owning device id — enabling per-device log attribution even
        /// though the backend only writes to the shared Console (Phase 3 multi-device).</summary>
        public static readonly System.Threading.AsyncLocal<string?> DeviceContext = new();

        /// <summary>Like <see cref="LineWritten"/> but also carries the ambient device id (null when none).</summary>
        public static event Action<string, string?>? LineWrittenWithContext;

        /// <summary>
        /// Redirects <see cref="Console.Out"/> (and Error) through a tee writer once.
        /// Safe to call multiple times; only the first call takes effect.
        /// </summary>
        public static void Install()
        {
            lock (Gate)
            {
                if (_installed)
                {
                    return;
                }

                _installed = true;
                TextWriter original = Console.Out;
                var tee = new LineForwardingWriter(original, Raise);
                Console.SetOut(tee);
                Console.SetError(tee);
            }
        }

        internal static void Raise(string line)
        {
            LineWritten?.Invoke(line);
            LineWrittenWithContext?.Invoke(line, DeviceContext.Value);
        }

        /// <summary>
        /// A <see cref="TextWriter"/> that forwards to the original console writer and also
        /// emits each completed line to a callback.
        /// </summary>
        private sealed class LineForwardingWriter : TextWriter
        {
            private readonly TextWriter _inner;
            private readonly Action<string> _onLine;
            private readonly StringBuilder _buffer = new StringBuilder();
            private readonly object _bufferGate = new object();

            public LineForwardingWriter(TextWriter inner, Action<string> onLine)
            {
                _inner = inner;
                _onLine = onLine;
            }

            public override Encoding Encoding => _inner.Encoding;

            public override void Write(char value)
            {
                _inner.Write(value);
                lock (_bufferGate)
                {
                    if (value == '\n')
                    {
                        Flush(_buffer);
                    }
                    else if (value != '\r')
                    {
                        _buffer.Append(value);
                    }
                }
            }

            public override void Write(string? value)
            {
                _inner.Write(value);
                if (string.IsNullOrEmpty(value))
                {
                    return;
                }

                lock (_bufferGate)
                {
                    foreach (char c in value)
                    {
                        if (c == '\n')
                        {
                            Flush(_buffer);
                        }
                        else if (c != '\r')
                        {
                            _buffer.Append(c);
                        }
                    }
                }
            }

            public override void WriteLine(string? value)
            {
                Write(value);
                Write('\n');
            }

            private void Flush(StringBuilder sb)
            {
                string line = sb.ToString();
                sb.Clear();
                _onLine(line);
            }
        }
    }
}
