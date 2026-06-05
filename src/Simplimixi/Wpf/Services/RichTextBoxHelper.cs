using System;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Documents;
using RichTextBox = System.Windows.Controls.RichTextBox;
using FontFamily = System.Windows.Media.FontFamily;
using Color = System.Windows.Media.Color;
using Brush = System.Windows.Media.Brush;
using SolidColorBrush = System.Windows.Media.SolidColorBrush;

namespace CvAut.WpfApp.Services
{
    /// <summary>
    /// Lớp hỗ trợ tĩnh (Helper Class) cung cấp các Dependency Property đính kèm (Attached Property)
    /// cho điều khiển RichTextBox của WPF, hỗ trợ tự động định dạng màu sắc (syntax highlighting)
    /// cho nhật ký hoạt động thời gian thực dựa trên các thẻ phân loại (Tag, Level) và mốc thời gian (Timestamp).
    /// </summary>
    public static class RichTextBoxHelper
    {
        /// <summary>
        /// Đăng ký Dependency Property đính kèm "LogText" dùng để liên kết chuỗi log văn bản thô (Data Binding)
        /// từ ViewModel vào RichTextBox, đồng thời kích hoạt hàm xử lý định dạng khi văn bản thay đổi.
        /// </summary>
        public static readonly DependencyProperty LogTextProperty =
            DependencyProperty.RegisterAttached(
                "LogText",
                typeof(string),
                typeof(RichTextBoxHelper),
                new PropertyMetadata(string.Empty, OnLogTextChanged));

        /// <summary>
        /// Phương thức Getter để lấy giá trị thuộc tính LogText từ đối tượng WPF chỉ định.
        /// </summary>
        /// <param name="obj">Đối tượng WPF đích chứa thuộc tính.</param>
        /// <returns>Chuỗi log hiện tại.</returns>
        public static string GetLogText(DependencyObject obj)
        {
            return (string)obj.GetValue(LogTextProperty);
        }

        /// <summary>
        /// Phương thức Setter để gán giá trị thuộc tính LogText cho đối tượng WPF chỉ định.
        /// </summary>
        /// <param name="obj">Đối tượng WPF đích.</param>
        /// <param name="value">Chuỗi log mới.</param>
        public static void SetLogText(DependencyObject obj, string value)
        {
            obj.SetValue(LogTextProperty, value);
        }

        /// <summary>
        /// Hàm xử lý sự kiện xảy ra khi thuộc tính LogText được liên kết thay đổi giá trị.
        /// Thực hiện phân tích cú pháp chuỗi log thô, chuyển đổi thành tài liệu FlowDocument có định dạng màu sắc
        /// và tự động cuộn xuống cuối màn hình.
        /// </summary>
        private static void OnLogTextChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is not RichTextBox richTextBox) return;

            string text = e.NewValue as string ?? string.Empty;

            // Khởi tạo một FlowDocument mới cho RichTextBox để lưu các dòng log định dạng
            var document = new FlowDocument
            {
                FontFamily = new FontFamily("Consolas, Cascadia Mono, Courier New"), // Font chữ đơn khoảng cách giống Console
                FontSize = 12,
                LineHeight = 17,
                PagePadding = new Thickness(0)
            };

            var paragraph = new Paragraph { Margin = new Thickness(0) };
            
            // Chia chuỗi văn bản log thành các dòng đơn
            string[] lines = text.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None);
            
            // Giới hạn chỉ hiển thị tối đa 1000 dòng log cuối cùng để đảm bảo hiệu suất render UI mượt mà
            int startIndex = Math.Max(0, lines.Length - 1000);

            for (int i = startIndex; i < lines.Length; i++)
            {
                string line = lines[i];
                if (string.IsNullOrWhiteSpace(line)) continue;

                // Định dạng và thêm dòng log vào paragraph
                AddFormattedLine(paragraph, line);
            }

            document.Blocks.Add(paragraph);
            richTextBox.Document = document;
            
            // Tự động cuộn xuống cuối RichTextBox để xem log mới nhất
            richTextBox.ScrollToEnd();
        }

        /// <summary>
        /// Phân tích một dòng log thô bằng các biểu thức chính quy (Regex)
        /// để tách mốc thời gian, nhãn phân loại (Tag) và mức độ log (Level), sau đó tô màu sắc tương ứng.
        /// </summary>
        /// <param name="paragraph">Đối tượng Paragraph của FlowDocument để thêm các đoạn văn bản (Inlines).</param>
        /// <param name="line">Dòng log thô cần phân tích.</param>
        private static void AddFormattedLine(Paragraph paragraph, string line)
        {
            // 1. Dò tìm và định dạng mốc thời gian ở đầu dòng dạng [HH:mm:ss]
            Match timestampMatch = Regex.Match(line, @"^(\[\d{2}:\d{2}:\d{2}\])\s*");
            if (timestampMatch.Success)
            {
                paragraph.Inlines.Add(new Run(timestampMatch.Groups[1].Value)
                {
                    Foreground = BrushFromRgb(100, 116, 139) // Màu xám đá phiến nhẹ nhàng
                });

                // Cắt bỏ phần mốc thời gian khỏi chuỗi log để tiếp tục phân tích
                line = line.Substring(timestampMatch.Length);
                paragraph.Inlines.Add(new Run(" ") { Foreground = BodyBrush });
            }

            // 2. Dò tìm và định dạng thẻ nhãn (Tag) nằm trong dấu ngoặc vuông (ví dụ: [BOT], [ADB], [VISION])
            Match bracketMatch = Regex.Match(line, @"^(\[[^\]]+\])\s*");
            if (bracketMatch.Success)
            {
                string tag = bracketMatch.Groups[1].Value;
                Brush tagBrush = BrushForToken(tag);

                paragraph.Inlines.Add(new Run(tag)
                {
                    Foreground = tagBrush,
                    FontWeight = FontWeights.Bold
                });

                line = line.Substring(bracketMatch.Length);
                paragraph.Inlines.Add(new Run(" ") { Foreground = BodyBrush });
            }

            // 3. Dò tìm và định dạng mức độ nhật ký (Level) (ví dụ: INFO, WARNING, ERROR, SUCCESS)
            Match levelMatch = Regex.Match(line, @"^(INFO|WAIT|WARNING|WARN|READY|SUCCESS|ERROR|ERR|FAIL|PAUSED|RUNNING|IDLE)\b\s*", RegexOptions.IgnoreCase);
            if (levelMatch.Success)
            {
                string level = levelMatch.Groups[1].Value.ToUpperInvariant();
                paragraph.Inlines.Add(new Run(level.PadRight(8)) // Căn chỉnh lề phải cho cột Level đều nhau
                {
                    Foreground = BrushForToken(level),
                    FontWeight = FontWeights.Bold
                });

                line = line.Substring(levelMatch.Length);
            }

            // 4. Phần còn lại của dòng log chính là nội dung tin nhắn log (Message Body)
            paragraph.Inlines.Add(new Run(line + "\r\n") { Foreground = BodyBrush });
        }

        /// <summary>
        /// Quyết định màu sắc đại diện (Brush) cho các thẻ/từ khóa dựa vào mức độ nghiêm trọng
        /// hoặc phân hệ tạo ra log.
        /// </summary>
        /// <param name="token">Từ khóa hoặc thẻ log cần tô màu.</param>
        /// <returns>Đối tượng Brush màu sắc tương ứng.</returns>
        private static Brush BrushForToken(string token)
        {
            string upper = token.ToUpperInvariant();

            // Nhóm lỗi nghiêm trọng (Màu Đỏ nhạt)
            if (upper.Contains("ERROR") || upper.Contains("ERR") || upper.Contains("FAIL"))
            {
                return BrushFromRgb(248, 113, 113); // #F87171
            }

            // Nhóm cảnh báo/chờ đợi/tạm dừng (Màu Vàng neon)
            if (upper.Contains("WARN") || upper.Contains("WAIT") || upper.Contains("PAUSED"))
            {
                return BrushFromRgb(250, 204, 21); // #FACC15
            }

            // Nhóm hoạt động thành công/hoạt động tốt (Màu Xanh lá cây)
            if (upper.Contains("SUCCESS") || upper.Contains("READY") || upper.Contains("RUNNING") || upper.Contains("DONE"))
            {
                return BrushFromRgb(74, 222, 128); // #4ADE80
            }

            // Nhóm ADB, xử lý ảnh Vision, OCR (Màu Xanh dương)
            if (upper.Contains("ADB") || upper.Contains("VISION") || upper.Contains("OCR"))
            {
                return BrushFromRgb(96, 165, 250); // #60A5FA
            }

            // Nhóm Máy trạng thái FSM, điều khiển Bot (Màu Xanh lục lam)
            if (upper.Contains("FSM") || upper.Contains("BOT"))
            {
                return BrushFromRgb(45, 212, 191); // #2DD4BF
            }

            // Mặc định (Màu Xám Slate)
            return BrushFromRgb(148, 163, 184); // #94A3B8
        }

        // Màu sắc chữ mặc định cho phần nội dung chính của log (Màu Trắng xám)
        private static Brush BodyBrush { get; } = BrushFromRgb(226, 232, 240); // #E2E8F0

        /// <summary>
        /// Khởi tạo và tối ưu hóa cọ vẽ SolidColorBrush bằng cách đóng băng (Freeze) tài nguyên,
        /// giúp tăng tốc độ render UI trong WPF và giảm tiêu thụ tài nguyên đồ họa.
        /// </summary>
        private static SolidColorBrush BrushFromRgb(byte r, byte g, byte b)
        {
            var brush = new SolidColorBrush(Color.FromRgb(r, g, b));
            brush.Freeze(); // Đóng băng brush để tối ưu hiệu năng render WPF
            return brush;
        }
    }
}
