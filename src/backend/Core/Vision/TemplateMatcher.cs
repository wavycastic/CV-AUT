using System;
using System.IO;
using OpenCvSharp;
using Point = OpenCvSharp.Point;
using Size = OpenCvSharp.Size;

namespace CvAut
{
    /// <summary>
    /// Chịu trách nhiệm duy nhất: nạp ảnh mẫu từ thư mục template và thực hiện so khớp mẫu
    /// (Template Matching) bằng OpenCV trên ảnh chụp màn hình.
    /// Tách ra từ VisionEngine để phần khớp mẫu không còn lẫn với phần OCR chữ số.
    /// </summary>
    internal sealed class TemplateMatcher
    {
        private readonly string _templatesDir;

        public TemplateMatcher(string templatesDir)
        {
            _templatesDir = templatesDir;
        }

        public string TemplatesDirectory => _templatesDir;

        /// <summary>
        /// Tìm kiếm một đối tượng trên ảnh chụp màn hình trong vùng ROI chỉ định,
        /// đồng thời trả về điểm số tương đồng lớn nhất.
        /// </summary>
        public Point? FindElement(Mat screenshot, string templateName, double threshold, Rect? roi, out double score)
        {
            score = 0;
            if (screenshot.Empty()) return null;

            if (!TemplateAssetLoader.Exists(_templatesDir, templateName))
            {
                Console.WriteLine($"[TRACE][VISION] phase=template_match action=match_candidate status=skipped reason=candidate_missing details=\"{templateName}\"");
                return null;
            }

            using Mat template = TemplateAssetLoader.Load(_templatesDir, templateName, ImreadModes.Color);
            if (template.Empty()) return null;

            // Giới hạn vùng ROI trong biên ảnh để đảm bảo an toàn
            Rect searchRect = roi.HasValue
                ? ImageUtils.ClampRect(roi.Value, screenshot.Width, screenshot.Height)
                : new Rect(0, 0, screenshot.Width, screenshot.Height);

            if (searchRect.Width < template.Width || searchRect.Height < template.Height)
            {
                return null;
            }

            // Thực hiện MatchTemplate và tìm tọa độ khớp nhất
            using Mat searchArea = new Mat(screenshot, searchRect);
            using Mat res = new Mat();
            Cv2.MatchTemplate(searchArea, template, res, TemplateMatchModes.CCoeffNormed);
            Cv2.MinMaxLoc(res, out _, out double maxVal, out _, out Point maxLoc);
            score = maxVal;

            if (maxVal >= threshold)
            {
                // Tính tọa độ điểm tâm tuyệt đối trên ảnh gốc
                int centerX = searchRect.X + maxLoc.X + template.Width / 2;
                int centerY = searchRect.Y + maxLoc.Y + template.Height / 2;
                return new Point(centerX, centerY);
            }

            return null;
        }

        /// <summary>
        /// Thử tìm kiếm mẫu hình ảnh theo đường dẫn (tương đối hoặc tuyệt đối) và trả về
        /// kết quả boolean, tọa độ điểm tâm và điểm số tương đồng.
        /// </summary>
        public bool TryFindTemplate(Mat source, string templatePath, Rect? roi, double threshold, out Point center, out double score)
        {
            center = default;
            score = 0;

            if (source == null || source.Empty()) return false;

            string fullPath = Path.IsPathRooted(templatePath) ? templatePath : Path.Combine(_templatesDir, templatePath);
            if (!File.Exists(fullPath)) return false;

            using Mat template = Cv2.ImRead(fullPath, ImreadModes.Grayscale);
            if (template.Empty()) return false;

            Rect safeRoi = roi.HasValue ? ImageUtils.ClampRect(roi.Value, source.Width, source.Height) : new Rect(0, 0, source.Width, source.Height);
            if (safeRoi.Width < template.Width || safeRoi.Height < template.Height) return false;

            using Mat crop = new Mat(source, safeRoi);
            using Mat gray = new Mat();
            if (crop.Channels() > 1)
                Cv2.CvtColor(crop, gray, ColorConversionCodes.BGR2GRAY);
            else
                crop.CopyTo(gray);

            using Mat res = new Mat();
            Cv2.MatchTemplate(gray, template, res, TemplateMatchModes.CCoeffNormed);
            Cv2.MinMaxLoc(res, out _, out double maxVal, out _, out Point maxLoc);
            score = maxVal;

            if (maxVal >= threshold)
            {
                center = new Point(safeRoi.X + maxLoc.X + template.Width / 2, safeRoi.Y + maxLoc.Y + template.Height / 2);
                return true;
            }

            return false;
        }

        /// <summary>
        /// So khớp mẫu hai tầng: cắt vùng nguồn trước, sau đó tìm trong một vùng con cục bộ.
        /// </summary>
        public bool TryFindTemplateRegion(Mat source, string templateFileName, Rect sourceRoi, Rect templateRoi, double threshold, out Point center, out double score)
        {
            center = default; score = 0;
            if (source.Empty()) return false;
            string fullPath = Path.Combine(_templatesDir, templateFileName);
            if (!File.Exists(fullPath)) return false;

            using Mat template = Cv2.ImRead(fullPath, ImreadModes.Grayscale);
            if (template.Empty()) return false;

            Rect safeSourceRoi = ImageUtils.ClampRect(sourceRoi, source.Width, source.Height);
            if (safeSourceRoi.Width < 1 || safeSourceRoi.Height < 1) return false;
            using Mat sourceCrop = new Mat(source, safeSourceRoi);
            using Mat gray = new Mat();
            if (sourceCrop.Channels() > 1) Cv2.CvtColor(sourceCrop, gray, ColorConversionCodes.BGR2GRAY);
            else sourceCrop.CopyTo(gray);

            Rect safeTemplateRoi = ImageUtils.ClampRect(templateRoi, gray.Width, gray.Height);
            if (safeTemplateRoi.Width < template.Width || safeTemplateRoi.Height < template.Height) return false;
            using Mat searchArea = new Mat(gray, safeTemplateRoi);
            using Mat res = new Mat();
            Cv2.MatchTemplate(searchArea, template, res, TemplateMatchModes.CCoeffNormed);
            Cv2.MinMaxLoc(res, out _, out double maxVal, out _, out Point maxLoc);
            score = maxVal;

            if (maxVal >= threshold)
            {
                center = new Point(safeSourceRoi.X + safeTemplateRoi.X + maxLoc.X + template.Width / 2, safeSourceRoi.Y + safeTemplateRoi.Y + maxLoc.Y + template.Height / 2);
                return true;
            }
            return false;
        }

        /// <summary>
        /// So khớp mẫu ở nhiều tỷ lệ scale khác nhau để chịu được thay đổi độ phóng đại.
        /// </summary>
        public bool TryFindTemplateRegionMultiScale(Mat source, string templateFileName, Rect roi, double threshold, out Point center, out double score)
        {
            center = default; score = 0;
            if (source.Empty()) return false;
            string fullPath = Path.Combine(_templatesDir, templateFileName);
            if (!File.Exists(fullPath)) return false;

            using Mat template = Cv2.ImRead(fullPath, ImreadModes.Grayscale);
            if (template.Empty()) return false;

            Rect safeRoi = ImageUtils.ClampRect(roi, source.Width, source.Height);
            if (safeRoi.Width < 1 || safeRoi.Height < 1) return false;
            using Mat crop = new Mat(source, safeRoi);
            using Mat gray = new Mat();
            if (crop.Channels() > 1) Cv2.CvtColor(crop, gray, ColorConversionCodes.BGR2GRAY);
            else crop.CopyTo(gray);

            double maxScore = 0; Point bestLoc = default; bool found = false;
            double[] scales = { 1.0, 0.9, 0.8, 1.1 };
            foreach (double scale in scales)
            {
                Size scaledSize = new((int)(template.Width * scale), (int)(template.Height * scale));
                if (scaledSize.Width < 4 || scaledSize.Height < 4) continue;
                if (scaledSize.Width > gray.Width || scaledSize.Height > gray.Height) continue;

                using Mat scaledTemplate = new Mat();
                Cv2.Resize(template, scaledTemplate, scaledSize, 0, 0, InterpolationFlags.Area);
                using Mat res = new Mat();
                Cv2.MatchTemplate(gray, scaledTemplate, res, TemplateMatchModes.CCoeffNormed);
                Cv2.MinMaxLoc(res, out _, out double val, out _, out Point loc);

                if (val > maxScore)
                {
                    maxScore = val; bestLoc = loc; found = true;
                }
            }

            score = maxScore;
            if (found && maxScore >= threshold)
            {
                center = new Point(safeRoi.X + bestLoc.X + template.Width / 2, safeRoi.Y + bestLoc.Y + template.Height / 2);
                return true;
            }
            return false;
        }
    }
}
