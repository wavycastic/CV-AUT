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
    public static class RichTextBoxHelper
    {
        public static readonly DependencyProperty LogTextProperty =
            DependencyProperty.RegisterAttached(
                "LogText",
                typeof(string),
                typeof(RichTextBoxHelper),
                new PropertyMetadata(string.Empty, OnLogTextChanged));

        public static string GetLogText(DependencyObject obj)
        {
            return (string)obj.GetValue(LogTextProperty);
        }

        public static void SetLogText(DependencyObject obj, string value)
        {
            obj.SetValue(LogTextProperty, value);
        }

        private static void OnLogTextChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is not RichTextBox richTextBox) return;

            string text = e.NewValue as string ?? string.Empty;

            var document = new FlowDocument
            {
                FontFamily = new FontFamily("Consolas, Cascadia Mono, Courier New"),
                FontSize = 12,
                LineHeight = 17,
                PagePadding = new Thickness(0)
            };

            var paragraph = new Paragraph { Margin = new Thickness(0) };
            string[] lines = text.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None);
            int startIndex = Math.Max(0, lines.Length - 1000);

            for (int i = startIndex; i < lines.Length; i++)
            {
                string line = lines[i];
                if (string.IsNullOrWhiteSpace(line)) continue;

                AddFormattedLine(paragraph, line);
            }

            document.Blocks.Add(paragraph);
            richTextBox.Document = document;
            richTextBox.ScrollToEnd();
        }

        private static void AddFormattedLine(Paragraph paragraph, string line)
        {
            Match timestampMatch = Regex.Match(line, @"^(\[\d{2}:\d{2}:\d{2}\])\s*");
            if (timestampMatch.Success)
            {
                paragraph.Inlines.Add(new Run(timestampMatch.Groups[1].Value)
                {
                    Foreground = BrushFromRgb(100, 116, 139)
                });

                line = line.Substring(timestampMatch.Length);
                paragraph.Inlines.Add(new Run(" ") { Foreground = BodyBrush });
            }

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

            Match levelMatch = Regex.Match(line, @"^(INFO|WAIT|WARNING|WARN|READY|SUCCESS|ERROR|ERR|FAIL|PAUSED|RUNNING|IDLE)\b\s*", RegexOptions.IgnoreCase);
            if (levelMatch.Success)
            {
                string level = levelMatch.Groups[1].Value.ToUpperInvariant();
                paragraph.Inlines.Add(new Run(level.PadRight(8))
                {
                    Foreground = BrushForToken(level),
                    FontWeight = FontWeights.Bold
                });

                line = line.Substring(levelMatch.Length);
            }

            paragraph.Inlines.Add(new Run(line + "\r\n") { Foreground = BodyBrush });
        }

        private static Brush BrushForToken(string token)
        {
            string upper = token.ToUpperInvariant();

            if (upper.Contains("ERROR") || upper.Contains("ERR") || upper.Contains("FAIL"))
            {
                return BrushFromRgb(248, 113, 113);
            }

            if (upper.Contains("WARN") || upper.Contains("WAIT") || upper.Contains("PAUSED"))
            {
                return BrushFromRgb(250, 204, 21);
            }

            if (upper.Contains("SUCCESS") || upper.Contains("READY") || upper.Contains("RUNNING") || upper.Contains("DONE"))
            {
                return BrushFromRgb(74, 222, 128);
            }

            if (upper.Contains("ADB") || upper.Contains("VISION") || upper.Contains("OCR"))
            {
                return BrushFromRgb(96, 165, 250);
            }

            if (upper.Contains("FSM") || upper.Contains("BOT"))
            {
                return BrushFromRgb(45, 212, 191);
            }

            return BrushFromRgb(148, 163, 184);
        }

        private static Brush BodyBrush { get; } = BrushFromRgb(226, 232, 240);

        private static SolidColorBrush BrushFromRgb(byte r, byte g, byte b)
        {
            var brush = new SolidColorBrush(Color.FromRgb(r, g, b));
            brush.Freeze();
            return brush;
        }
    }
}
