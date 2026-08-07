using System;
using System.IO;
using OpenCvSharp;
using Point = OpenCvSharp.Point;

namespace CvAut
{
    /// <summary>
    /// Phân hệ Xử lý Ảnh (Vision Engine).
    /// Đây là một facade mỏng: toàn bộ logic so khớp mẫu nằm ở <see cref="TemplateMatcher"/>,
    /// còn logic nhận diện chữ số nhị phân nằm ở <see cref="DigitOcrReader"/>.
    /// Facade chỉ chịu trách nhiệm ghép nối và diễn giải kết quả cho các consumer.
    /// </summary>
    internal class VisionEngine : IVisionEngine, IDisposable
    {
        // Thư mục chứa các mẫu hình ảnh template PNG
        private readonly string _templatesDir;
        private readonly TemplateMatcher _matcher;
        private readonly DigitOcrReader _ocr;
        private bool _disposed;

        public string TemplatesPath => _templatesDir;

        public string TemplatesDirectory => _templatesDir;

        /// <summary>
        /// Khởi tạo VisionEngine với đường dẫn thư mục chứa các tệp mẫu.
        /// </summary>
        /// <param name="templatesDir">Đường dẫn thư mục chứa template.</param>
        public VisionEngine(string templatesDir = "Templates")
        {
            _templatesDir = templatesDir;
            if (!Directory.Exists(templatesDir))
            {
                Directory.CreateDirectory(templatesDir);
            }
            _matcher = new TemplateMatcher(templatesDir);
            _ocr = new DigitOcrReader();
        }

        // --- So khớp mẫu: uỷ quyền cho TemplateMatcher ---

        public Point? FindElement(Mat screenshot, string templateName, double threshold = 0.70)
            => _matcher.FindElement(screenshot, templateName, threshold, null, out _);

        public Point? FindElement(Mat screenshot, string templateName, double threshold, Rect roi, out double maxVal)
            => _matcher.FindElement(screenshot, templateName, threshold, (Rect?)roi, out maxVal);

        public Point? FindElement(Mat screenshot, string templateName, double threshold, Rect? roi, out double score)
            => _matcher.FindElement(screenshot, templateName, threshold, roi, out score);

        public bool ContainsElement(Mat screenshot, string templateName, double threshold, Rect roi)
            => _matcher.FindElement(screenshot, templateName, threshold, (Rect?)roi, out _) != null;

        public bool TryFindTemplate(Mat source, string templatePath, Rect? roi, double threshold, out Point center, out double score)
            => _matcher.TryFindTemplate(source, templatePath, roi, threshold, out center, out score);

        public bool TryFindTemplateRegion(Mat source, string templateFileName, Rect sourceRoi, Rect templateRoi, double threshold, out Point center, out double score)
            => _matcher.TryFindTemplateRegion(source, templateFileName, sourceRoi, templateRoi, threshold, out center, out score);

        public bool TryFindTemplateRegionMultiScale(Mat source, string templateFileName, Rect roi, double threshold, out Point center, out double score)
            => _matcher.TryFindTemplateRegionMultiScale(source, templateFileName, roi, threshold, out center, out score);

        // --- OCR chữ số: uỷ quyền cho DigitOcrReader ---

        public bool TryExtractNumericalMetrics(Mat screenshot, Rect roi, out int value, out double confidence, bool isOffline = false, bool useRgbThresh = false, bool invert = false, bool allowVerticalShift = false)
            => _ocr.TryExtractNumericalMetrics(screenshot, roi, out value, out confidence, isOffline, useRgbThresh, invert, allowVerticalShift);

        /// <summary>
        /// Trích xuất chỉ số số nguyên đơn giản từ vùng ROI ảnh mà không cần lấy chi tiết độ tin cậy.
        /// </summary>
        public int ExtractNumericalMetrics(Mat screenshot, Rect roi, bool isOffline = false, bool useRgbThresh = false, bool invert = false, bool allowVerticalShift = false)
            => _ocr.TryExtractNumericalMetrics(screenshot, roi, out int value, out _, isOffline, useRgbThresh, invert, allowVerticalShift)
                ? value
                : 0;

        public int OcrReadNumber(Mat croppedImage)
            => _ocr.TryExtractNumericalMetrics(croppedImage, new Rect(0, 0, croppedImage.Width, croppedImage.Height), out int val, out _)
                ? val
                : 0;

        // --- Diễn giải kết quả cấp cao ---

        public (int Gold, int Elixir, int DarkElixir) ExtractScoutedLoot(Mat screenshot) => IsTarget.ExtractResources(screenshot, this);

        /// <summary>
        /// Giải phóng tài nguyên của phần OCR chữ số.
        /// </summary>
        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _ocr.Dispose();
        }
    }
}
