using System;
using System.IO;
using System.Text;

namespace CvAut.WpfApp.Services.Logging
{
    /// <summary>
    /// Lớp ghi văn bản tùy chỉnh (TextWriter) giúp chuyển hướng (redirect) luồng ghi chuẩn
    /// của Console (stdout/stderr) sang giao diện đồ họa WPF của ứng dụng.
    /// Hỗ trợ lọc các dòng log nhiễu và dịch nội dung log sang ngôn ngữ chuẩn hóa.
    /// </summary>
    public sealed class UiLogTextWriter : TextWriter
    {
        // Bộ ghi gốc (thường là Console.Out mặc định) để vẫn xuất log ra cửa sổ console CLI
        private readonly TextWriter _inner;
        
        // Hành động ủy nhiệm để đẩy log đã xử lý lên giao diện (ví dụ: gán vào RichTextBox/TextBox)
        private readonly Action<string> _append;
        
        // Bộ lọc điều kiện để xác định xem dòng log nào nên được bỏ qua, không hiển thị trên UI
        private readonly Func<string, bool> _shouldIgnore;
        
        // Hàm dịch hoặc chuẩn hóa dòng log trước khi đẩy lên UI
        private readonly Func<string, string> _translateLog;
        
        // Bộ đệm dòng lưu tạm các ký tự trước khi gặp ký tự xuống dòng (\n)
        private readonly StringBuilder _lineBuffer = new();

        /// <summary>
        /// Khởi tạo một thực thể mới của UiLogTextWriter.
        /// </summary>
        /// <param name="inner">TextWriter gốc của hệ thống Console.</param>
        /// <param name="append">Hành động đẩy dữ liệu log sau khi xử lý lên UI.</param>
        /// <param name="shouldIgnore">Hàm lọc để bỏ qua các log không cần thiết.</param>
        /// <param name="translateLog">Hàm dịch log sang định dạng hiển thị UI.</param>
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

        /// <summary>
        /// Trả về mã hóa ký tự (Encoding) của TextWriter gốc.
        /// </summary>
        public override Encoding Encoding => _inner.Encoding;

        /// <summary>
        /// Ghi một ký tự đơn vào luồng console gốc và tích lũy vào bộ đệm dòng để lọc/dịch khi xuống dòng.
        /// </summary>
        /// <param name="value">Ký tự cần ghi.</param>
        public override void Write(char value)
        {
            // Vẫn ghi ra console gốc để tiện chẩn đoán
            _inner.Write(value);

            // Khi gặp ký tự xuống dòng, thực hiện giải phóng bộ đệm dòng để đẩy lên UI
            if (value == '\n')
            {
                FlushBufferedLine();
                return;
            }

            // Bỏ qua ký tự về đầu dòng (\r), chỉ tích lũy các ký tự thông thường
            if (value != '\r')
            {
                _lineBuffer.Append(value);
            }
        }

        /// <summary>
        /// Ghi một chuỗi văn bản vào luồng console gốc và xử lý trích xuất các dòng để lọc/dịch.
        /// </summary>
        /// <param name="value">Chuỗi văn bản cần ghi.</param>
        public override void Write(string? value)
        {
            _inner.Write(value);

            if (string.IsNullOrEmpty(value))
            {
                return;
            }

            // Duyệt qua từng ký tự của chuỗi để tìm ký tự xuống dòng
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

        /// <summary>
        /// Ghi một dòng văn bản hoàn chỉnh kèm ký tự xuống dòng.
        /// </summary>
        /// <param name="value">Chuỗi văn bản dòng cần ghi.</param>
        public override void WriteLine(string? value)
        {
            _inner.WriteLine(value);

            if (!string.IsNullOrEmpty(value))
            {
                _lineBuffer.Append(value);
            }

            FlushBufferedLine();
        }

        /// <summary>
        /// Đẩy toàn bộ dữ liệu đang chờ trong luồng gốc ra thiết bị đầu ra.
        /// </summary>
        public override void Flush()
        {
            _inner.Flush();
        }

        /// <summary>
        /// Xử lý dòng log trong bộ đệm: kiểm tra bộ lọc bỏ qua nhiễu, dịch chuỗi và đẩy lên UI.
        /// </summary>
        private void FlushBufferedLine()
        {
            if (_lineBuffer.Length == 0)
            {
                return;
            }

            // Chuyển đổi bộ đệm ký tự thành chuỗi dòng hoàn chỉnh
            string line = _lineBuffer.ToString();
            _lineBuffer.Clear();

            // Nếu dòng log thuộc diện lọc bỏ qua, không đưa lên UI
            if (_shouldIgnore(line))
            {
                return;
            }

            // Thực hiện dịch log và chuyển tiếp lên UI
            _append(_translateLog(line));
        }
    }
}
