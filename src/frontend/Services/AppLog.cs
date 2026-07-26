using System;
using System.IO;
using System.Text;

namespace CvAut.Services
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
        private static StreamWriter? _fileWriter;

        /// <summary>Absolute path of the repo-local log file created for this process.</summary>
        public static string? CurrentLogFilePath { get; private set; }

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
                TextWriter output = CreateRepoLogWriter(original);
                var tee = new LineForwardingWriter(output, Raise);
                Console.SetOut(tee);
                Console.SetError(tee);

                if (!string.IsNullOrWhiteSpace(CurrentLogFilePath))
                {
                    Console.WriteLine($"[APP_LOG] phase=startup status=ready path=\"{CurrentLogFilePath}\"");
                }
            }
        }

        private static TextWriter CreateRepoLogWriter(TextWriter original)
        {
            try
            {
                string repoRoot = FindRepoRoot();
                string logDir = Path.Combine(repoRoot, "logs");
                Directory.CreateDirectory(logDir);

                string fileName = GetNextLogFileName(logDir, DateTime.Now);
                string logPath = Path.Combine(logDir, fileName);
                _fileWriter = new StreamWriter(new FileStream(logPath, FileMode.CreateNew, FileAccess.Write, FileShare.ReadWrite), new UTF8Encoding(encoderShouldEmitUTF8Identifier: false))
                {
                    AutoFlush = true
                };

                CurrentLogFilePath = logPath;
                return new TeeTextWriter(original, _fileWriter);
            }
            catch (Exception ex)
            {
                original.WriteLine($"[APP_LOG] phase=startup status=fail reason=\"{ex.Message}\"");
                return original;
            }
        }

        public static string GetNextLogFileName(string logDir, DateTime now)
        {
            string prefix = $"{now:dd-MM-yy_HH}h{now:mm}_";
            int index = 1;
            while (index <= 999)
            {
                string candidate = $"{prefix}{index:D3}.log";
                string fullPath = Path.Combine(logDir, candidate);
                if (!File.Exists(fullPath))
                {
                    return candidate;
                }
                index++;
            }
            return $"{prefix}{index:D3}_{now:fff}.log";
        }

        private static string FindRepoRoot()
        {
            string? dir = Environment.CurrentDirectory;
            while (!string.IsNullOrWhiteSpace(dir))
            {
                if (File.Exists(Path.Combine(dir, "CV-AUT.slnx")) || Directory.Exists(Path.Combine(dir, ".git")) || File.Exists(Path.Combine(dir, ".git")))
                {
                    return dir;
                }

                dir = Directory.GetParent(dir)?.FullName;
            }

            dir = AppContext.BaseDirectory;
            while (!string.IsNullOrWhiteSpace(dir))
            {
                if (File.Exists(Path.Combine(dir, "CV-AUT.slnx")) || Directory.Exists(Path.Combine(dir, ".git")) || File.Exists(Path.Combine(dir, ".git")))
                {
                    return dir;
                }

                dir = Directory.GetParent(dir)?.FullName;
            }

            return Environment.CurrentDirectory;
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

        private sealed class TeeTextWriter : TextWriter
        {
            private readonly TextWriter _first;
            private readonly TextWriter _second;
            private readonly object _gate = new object();

            public TeeTextWriter(TextWriter first, TextWriter second)
            {
                _first = first;
                _second = second;
            }

            public override Encoding Encoding => _first.Encoding;

            public override void Write(char value)
            {
                lock (_gate)
                {
                    _first.Write(value);
                    _second.Write(value);
                }
            }

            public override void Write(string? value)
            {
                lock (_gate)
                {
                    _first.Write(value);
                    _second.Write(value);
                }
            }

            public override void WriteLine(string? value)
            {
                lock (_gate)
                {
                    _first.WriteLine(value);
                    _second.WriteLine(value);
                }
            }

            public override void Flush()
            {
                lock (_gate)
                {
                    _first.Flush();
                    _second.Flush();
                }
            }
        }
    }
}
