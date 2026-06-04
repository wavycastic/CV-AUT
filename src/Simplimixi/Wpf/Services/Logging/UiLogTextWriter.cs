using System;
using System.IO;
using System.Text;

namespace CvAut.WpfApp.Services.Logging
{
    public sealed class UiLogTextWriter : TextWriter
    {
        private readonly TextWriter _inner;
        private readonly Action<string> _append;
        private readonly Func<string, bool> _shouldIgnore;
        private readonly Func<string, string> _translateLog;
        private readonly StringBuilder _lineBuffer = new();

        public UiLogTextWriter(
            TextWriter inner,
            Action<string> append,
            Func<string, bool> shouldIgnore,
            Func<string, string> translateLog)
        {
            _inner = inner;
            _append = append;
            _shouldIgnore = shouldIgnore;
            _translateLog = translateLog;
        }

        public override Encoding Encoding => _inner.Encoding;

        public override void Write(char value)
        {
            _inner.Write(value);

            if (value == '\n')
            {
                FlushBufferedLine();
                return;
            }

            if (value != '\r')
            {
                _lineBuffer.Append(value);
            }
        }

        public override void Write(string? value)
        {
            _inner.Write(value);

            if (string.IsNullOrEmpty(value))
            {
                return;
            }

            foreach (char ch in value)
            {
                if (ch == '\n')
                {
                    FlushBufferedLine();
                }
                else if (ch != '\r')
                {
                    _lineBuffer.Append(ch);
                }
            }
        }

        public override void WriteLine(string? value)
        {
            _inner.WriteLine(value);

            if (!string.IsNullOrEmpty(value))
            {
                _lineBuffer.Append(value);
            }

            FlushBufferedLine();
        }

        public override void Flush()
        {
            _inner.Flush();
        }

        private void FlushBufferedLine()
        {
            if (_lineBuffer.Length == 0)
            {
                return;
            }

            string line = _lineBuffer.ToString();
            _lineBuffer.Clear();

            if (_shouldIgnore(line))
            {
                return;
            }

            _append(_translateLog(line));
        }
    }
}
