using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using OpenCvSharp;
using Point = OpenCvSharp.Point;
using Size = OpenCvSharp.Size;

namespace CvAut
{
    /// <summary>
    /// Phân hệ Xử lý Ảnh (Vision Engine):
    /// - Quản lý và thực hiện so khớp mẫu (Template Matching) sử dụng OpenCV.
    /// - Thực hiện nhận diện chữ số nhị phân (Light OCR) bằng thuật toán so khớp chỉ số IoU (Intersection over Union)
    ///   với ma trận nhị phân 12x16 của font chữ Supercell Magic chuyên dụng cho game Clash of Clans.
    /// </summary>
    internal class VisionEngine : IDisposable
    {
        // Thư mục chứa các mẫu hình ảnh template PNG
        private readonly string _templatesDir;

        // Từ điển lưu trữ ma trận OpenCV (Mat) cho 11 chữ số nhị phân làm mẫu (0-9 và mẫu số 5 thay thế offline)
        private readonly Dictionary<int, Mat> _templates = new();
        private bool _disposed;

        /// <summary>
        /// Khởi tạo VisionEngine với đường dẫn thư mục chứa các tệp mẫu.
        /// </summary>
        /// <param name="templatesDir">Đường dẫn thư mục chứa template.</param>
        public string TemplatesDirectory => _templatesDir;

        public VisionEngine(string templatesDir = "Templates")
        {
            _templatesDir = templatesDir;
            if (!Directory.Exists(templatesDir))
            {
                Directory.CreateDirectory(templatesDir);
            }
            InitializeDigitTemplates();
        }

        /// <summary>
        /// Khởi tạo ma trận nhị phân mẫu kích thước 12x16 pixel đại diện cho các chữ số từ 0 đến 9.
        /// Font chữ sử dụng là Supercell Magic trong game Clash of Clans.
        /// Sử dụng mảng byte tĩnh 1 và 0 để tạo Mat trực tiếp nhằm giảm phụ thuộc vào tệp bên ngoài khi OCR số lượng.
        /// </summary>
        private void InitializeDigitTemplates()
        {
            // Mảng ma trận nhị phân 16 hàng x 12 cột cho các chữ số
            byte[][,] rawTemplates = new byte[11][,]
            {
                // Chữ số 0
                new byte[16, 12] {
                    {0,0,0,1,1,1,1,1,1,1,0,0}, {0,0,1,1,1,1,1,1,1,1,1,0}, {0,1,1,1,1,1,1,1,1,1,1,1}, {1,1,1,1,1,1,1,1,1,1,1,1},
                    {1,1,1,1,1,0,0,0,1,1,1,1}, {1,1,1,1,0,0,0,0,0,1,1,1}, {1,1,1,1,0,0,0,0,0,1,1,1}, {1,1,1,1,0,0,0,0,0,1,1,1},
                    {1,1,1,1,0,0,0,0,0,1,1,1}, {1,1,1,1,0,0,0,0,0,1,1,1}, {1,1,1,1,0,0,0,0,0,1,1,1}, {1,1,1,1,0,0,0,0,0,1,1,1},
                    {1,1,1,1,1,0,0,0,1,1,1,1}, {0,1,1,1,1,1,1,1,1,1,1,1}, {0,0,1,1,1,1,1,1,1,1,1,0}, {0,0,0,1,1,1,1,1,1,1,0,0}
                },
                // Chữ số 1
                new byte[16, 12] {
                    {0,0,1,1,1,1,1,1,1,1,1,1}, {1,1,1,1,1,1,1,1,1,1,1,1}, {1,1,1,1,1,1,1,1,1,1,1,1}, {1,1,1,1,1,1,1,1,1,1,1,1},
                    {0,0,0,0,1,1,1,1,1,1,1,1}, {0,0,0,0,1,1,1,1,1,1,1,1}, {0,0,0,0,1,1,1,1,1,1,1,1}, {0,0,0,0,1,1,1,1,1,1,1,1},
                    {0,0,0,0,1,1,1,1,1,1,1,1}, {0,0,0,0,1,1,1,1,1,1,1,1}, {0,0,0,0,1,1,1,1,1,1,1,1}, {0,0,0,0,1,1,1,1,1,1,1,1},
                    {0,0,0,0,1,1,1,1,1,1,1,1}, {0,0,0,0,1,1,1,1,1,1,1,1}, {0,0,0,0,1,1,1,1,1,1,1,1}, {0,0,0,0,1,1,1,1,1,1,1,1}
                },
                // Chữ số 2
                new byte[16, 12] {
                    {0,0,0,0,1,1,1,1,1,1,0,0}, {0,0,1,1,1,1,1,1,1,1,1,1}, {0,1,1,1,1,1,1,1,1,1,1,1}, {1,1,1,1,1,1,1,1,1,1,1,1},
                    {1,1,1,1,0,0,0,0,1,1,1,1}, {0,0,0,0,0,0,0,1,1,1,1,1}, {0,0,0,0,0,0,1,1,1,1,1,0}, {0,0,0,0,0,1,1,1,1,1,0,0},
                    {0,0,0,0,1,1,1,1,1,0,0,0}, {0,0,0,1,1,1,1,1,0,0,0,0}, {0,0,1,1,1,1,1,0,0,0,0,0}, {0,1,1,1,1,1,0,0,0,0,0,0},
                    {1,1,1,1,1,1,1,1,1,1,1,1}, {1,1,1,1,1,1,1,1,1,1,1,1}, {1,1,1,1,1,1,1,1,1,1,1,1}, {1,1,1,1,1,1,1,1,1,1,1,1}
                },
                // Chữ số 3
                new byte[16, 12] {
                    {0,0,1,1,1,1,1,1,0,0,0,0}, {0,1,1,1,1,1,1,1,1,1,1,0}, {0,1,1,1,1,1,1,1,1,1,1,0}, {0,1,1,1,1,1,1,1,1,1,1,1},
                    {0,0,0,0,0,0,1,1,1,1,1,1}, {0,0,0,0,0,0,1,1,1,1,1,1}, {0,0,0,0,0,0,1,1,1,1,1,0}, {0,0,0,1,1,1,1,1,1,1,0,0},
                    {0,0,0,1,1,1,1,1,1,1,1,1}, {0,0,0,0,0,0,1,1,1,1,1,1}, {0,0,0,0,0,0,0,1,1,1,1,1}, {0,0,0,0,0,0,0,1,1,1,1,1},
                    {0,0,0,1,1,1,1,1,1,1,1,1}, {1,1,1,1,1,1,1,1,1,1,1,1}, {1,1,1,1,1,1,1,1,1,1,1,1}, {1,1,1,1,1,1,1,1,1,1,0,0}
                },
                // Chữ số 4
                new byte[16, 12] {
                    {0,0,0,0,1,1,1,1,1,1,0,0}, {0,0,0,0,1,1,1,1,1,1,0,0}, {0,0,0,1,1,1,1,1,1,1,0,0}, {0,0,0,1,1,1,1,1,1,1,0,0},
                    {0,0,0,1,1,1,0,1,1,1,0,0}, {0,0,1,1,1,0,0,1,1,1,0,0}, {0,0,1,1,1,0,0,1,1,1,0,0}, {0,1,1,1,1,0,0,1,1,1,0,0},
                    {1,1,1,1,1,0,0,1,1,1,0,0}, {1,1,1,1,1,1,1,1,1,1,1,1}, {1,1,1,1,1,1,1,1,1,1,1,1}, {1,1,1,1,1,1,1,1,1,1,1,1},
                    {1,1,1,1,1,1,1,1,1,1,1,1}, {0,0,0,0,0,0,0,1,1,1,0,0}, {0,0,0,0,0,0,0,1,1,1,0,0}, {0,0,0,0,0,0,0,1,1,1,0,0}
                },
                // Chữ số 5
                new byte[16, 12] {
                    {0,1,1,1,1,1,1,1,1,1,1,1}, {0,1,1,1,1,1,1,1,1,1,1,1}, {1,1,1,1,1,1,1,1,1,1,1,1}, {1,1,1,1,1,1,1,1,1,0,0,0},
                    {1,1,1,1,1,1,0,0,0,0,0,0}, {1,1,1,1,1,1,0,0,0,0,0,0}, {1,1,1,1,1,1,1,1,0,0,0,0}, {1,1,1,1,1,1,1,1,1,1,1,1},
                    {1,1,1,1,1,1,1,1,1,1,1,1}, {0,0,1,1,1,1,1,1,1,1,1,1}, {0,0,0,0,0,0,1,1,1,1,1,1}, {0,0,0,0,0,0,1,1,1,1,1,1},
                    {0,0,0,1,1,1,1,1,1,1,1,1}, {0,1,1,1,1,1,1,1,1,1,1,1}, {0,1,1,1,1,1,1,1,1,1,0,0}, {0,1,1,1,1,1,1,1,0,0,0,0}
                },
                // Chữ số 6
                new byte[16, 12] {
                    {0,0,0,0,0,1,1,1,1,1,1,0}, {0,0,1,1,1,1,1,1,1,1,1,1}, {0,1,1,1,1,1,1,1,1,1,1,1}, {0,1,1,1,1,1,1,1,1,1,1,1},
                    {0,1,1,1,1,1,1,0,0,0,0,0}, {0,1,1,1,1,0,0,0,0,0,0,0}, {1,1,1,1,1,1,0,0,0,0,0,0}, {1,1,1,1,1,1,1,1,1,1,1,0},
                    {1,1,1,1,1,1,1,1,1,1,1,1}, {1,1,1,1,1,1,1,1,1,1,1,1}, {1,1,1,1,1,1,1,1,1,1,1,1}, {1,1,1,1,1,0,0,0,1,1,1,1},
                    {1,1,1,1,1,0,0,0,1,1,1,1}, {1,1,1,1,1,1,1,1,1,1,1,1}, {1,1,1,1,1,1,1,1,1,1,1,1}, {0,1,1,1,1,1,1,1,1,1,1,1}
                },
                // Chữ số 7
                new byte[16, 12] {
                    {0,0,0,0,0,1,1,1,1,1,0,0}, {1,1,1,1,1,1,1,1,1,1,1,1}, {1,1,1,1,1,1,1,1,1,1,1,1}, {1,1,1,1,1,1,1,1,1,1,1,1},
                    {0,0,0,0,0,0,1,1,1,1,1,1}, {0,0,0,0,0,0,1,1,1,1,1,1}, {0,0,0,0,0,1,1,1,1,1,1,1}, {0,0,0,0,0,1,1,1,1,1,1,0},
                    {0,0,0,0,1,1,1,1,1,1,1,0}, {0,0,0,0,1,1,1,1,1,1,0,0}, {0,0,0,0,1,1,1,1,1,1,0,0}, {0,0,0,1,1,1,1,1,1,0,0,0},
                    {0,0,1,1,1,1,1,1,0,0,0,0}, {0,0,1,1,1,1,1,1,0,0,0,0}, {0,1,1,1,1,1,1,0,0,0,0,0}, {0,1,1,1,1,1,1,0,0,0,0,0}
                },
                // Chữ số 8
                new byte[16, 12] {
                    {0,0,0,0,1,1,1,1,1,0,0,0}, {0,0,1,1,1,1,1,1,1,1,1,0}, {0,1,1,1,1,1,1,1,1,1,1,1}, {0,1,1,1,1,1,1,1,1,1,1,1},
                    {0,1,1,1,1,0,0,1,1,1,1,1}, {1,1,1,1,1,0,0,1,1,1,1,1}, {0,1,1,1,1,1,1,1,1,1,1,0}, {0,0,1,1,1,1,1,1,1,1,0,0},
                    {0,0,0,1,1,1,1,1,1,1,1,0}, {0,0,1,1,1,1,1,1,1,1,1,1}, {0,1,1,1,1,1,1,1,1,1,1,1}, {1,1,1,1,0,0,0,0,1,1,1,1},
                    {1,1,1,1,1,0,0,0,1,1,1,1}, {1,1,1,1,1,0,0,1,1,1,1,1}, {1,1,1,1,1,1,1,1,1,1,1,1}, {0,1,1,1,1,1,1,1,1,1,1,1}
                },
                // Chữ số 9
                new byte[16, 12] {
                    {0,0,1,1,1,1,1,1,1,1,1,0}, {0,1,1,1,1,1,1,1,1,1,1,1}, {0,1,1,1,1,1,1,1,1,1,1,1}, {0,1,1,1,1,1,1,1,1,1,1,1},
                    {1,1,1,1,1,0,0,0,1,1,1,1}, {1,1,1,1,1,0,0,0,1,1,1,1}, {1,1,1,1,0,0,0,0,1,1,1,1}, {1,1,1,1,1,1,1,1,1,1,1,1},
                    {1,1,1,1,1,1,1,1,1,1,1,1}, {1,1,1,1,1,1,1,1,1,1,1,1}, {0,0,0,0,0,0,0,0,1,1,1,1}, {0,0,0,0,0,0,0,0,1,1,1,1},
                    {0,0,0,0,0,0,0,1,1,1,1,1}, {0,0,0,0,1,1,1,1,1,1,1,1}, {0,0,1,1,1,1,1,1,1,1,1,1}, {0,0,1,1,1,1,1,1,1,1,0,0}
                },
                // Chữ số 10 (Mẫu dự phòng chữ số 5 đặc biệt tương thích chế độ offline)
                new byte[16, 12] {
                    {0,0,0,0,0,1,1,1,1,1,1,0}, {0,0,1,1,1,1,1,1,1,1,1,1}, {0,1,1,1,1,1,1,1,1,1,1,1}, {0,1,1,1,1,1,1,1,1,1,1,1},
                    {0,1,1,1,1,1,1,1,0,0,0,0}, {0,1,1,1,1,0,0,0,0,0,0,0}, {1,1,1,1,1,1,0,0,0,0,0,0}, {1,1,1,1,1,1,1,1,1,1,1,1},
                    {1,1,1,1,1,1,1,1,1,1,1,1}, {1,1,1,1,1,1,1,1,1,1,1,1}, {1,1,1,1,1,1,1,1,1,1,1,1}, {1,1,1,1,1,0,0,0,1,1,1,1},
                    {1,1,1,1,1,0,0,0,1,1,1,1}, {1,1,1,1,1,1,1,1,1,1,1,1}, {1,1,1,1,1,1,1,1,1,1,1,1}, {0,1,1,1,1,1,1,1,1,1,1,1}
                }
            };

            // Khởi tạo các đối tượng Mat từ mảng tĩnh ở trên
            for (int i = 0; i < 11; i++)
            {
                Mat mat = new Mat(16, 12, MatType.CV_8UC1);
                var indexer = mat.GetGenericIndexer<byte>();
                for (int r = 0; r < 16; r++)
                {
                    for (int c = 0; c < 12; c++)
                    {
                        indexer[r, c] = (byte)(rawTemplates[i][r, c] * 255);
                    }
                }
                _templates[i] = mat;
            }
        }

        /// <summary>
        /// Tìm kiếm một đối tượng (giao diện, nút, icon) trên màn hình bằng phương pháp so khớp mẫu (Template Matching).
        /// </summary>
        /// <param name="screenshot">Ảnh chụp màn hình nguồn.</param>
        /// <param name="templateName">Tên tệp template (không gồm phần mở rộng .png).</param>
        /// <param name="threshold">Ngưỡng tin cậy chấp nhận khớp mẫu (mặc định 0.70).</param>
        /// <returns>Tọa độ tâm Point của điểm khớp tốt nhất, hoặc null nếu không khớp.</returns>
        public Point? FindElement(Mat screenshot, string templateName, double threshold = 0.70)
        {
            return FindElement(screenshot, templateName, threshold, null, out _);
        }

        /// <summary>
        /// Tìm kiếm một đối tượng trên ảnh chụp màn hình trong vùng ROI chỉ định, đồng thời trả về điểm số tương đồng lớn nhất.
        /// </summary>
        /// <param name="screenshot">Ảnh chụp màn hình nguồn.</param>
        /// <param name="templateName">Tên tệp template.</param>
        /// <param name="threshold">Ngưỡng tin cậy.</param>
        /// <param name="roi">Vùng giới hạn tìm kiếm trên màn hình (để tăng tốc độ và tránh khớp nhầm).</param>
        /// <param name="score">Điểm số tương đồng tối đa tìm thấy (0.0 đến 1.0).</param>
        /// <returns>Tọa độ tâm khớp hoặc null.</returns>
        public Point? FindElement(Mat screenshot, string templateName, double threshold, Rect? roi, out double score)
        {
            score = 0;
            if (screenshot.Empty()) return null;

            if (!TemplateAssetLoader.Exists(_templatesDir, templateName))
            {
                Console.WriteLine($"[VISION] phase=template_match status=fail reason=missing_file details=\"{templateName}\"");
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
        /// Cố gắng trích xuất chuỗi chữ số từ vùng hình ảnh chỉ định (ROI) sang giá trị số nguyên nguyên bản.
        /// Sử dụng thuật toán nhận diện chữ số nhị phân IoU (Light OCR):
        /// 1. Cắt vùng ảnh (Crop), nhị phân hóa ảnh bằng Thresholding (hoặc InRange đối với ảnh màu).
        /// 2. Tìm contours bên ngoài để tách riêng các chữ số đơn lẻ.
        /// 3. Sắp xếp các chữ số theo chiều ngang từ trái qua phải.
        /// 4. Resize mỗi chữ số về kích thước 12x16 pixel để chuẩn hóa.
        /// 5. Tính toán IoU của chữ số đó với 10 mẫu ma trận số của Supercell Magic để tìm số tương thích nhất.
        /// </summary>
        /// <param name="screenshot">Ảnh chụp màn hình nguồn.</param>
        /// <param name="roi">Vùng chứa chữ số cần đọc.</param>
        /// <param name="value">Giá trị số nguyên đọc được (output).</param>
        /// <param name="confidence">Độ tin cậy trung bình của các ký tự chữ số (output, từ 0.0 đến 1.0).</param>
        /// <param name="isOffline">Bật chế độ đọc offline (dùng mẫu chữ số dự phòng 10).</param>
        /// <param name="useRgbThresh">True để dùng phân ngưỡng màu RGB (trắng sáng), False để dùng nhị phân hóa thang xám.</param>
        /// <returns>True nếu đọc và chuyển đổi thành công ít nhất một số, ngược lại False.</returns>
        public bool TryExtractNumericalMetrics(Mat screenshot, Rect roi, out int value, out double confidence, bool isOffline = false, bool useRgbThresh = false, bool invert = false)
        {
            value = 0;
            confidence = 0;
            if (screenshot.Empty()) return false;

            Rect safeRoi = ImageUtils.ClampRect(roi, screenshot.Width, screenshot.Height);
            if (safeRoi.Width <= 0 || safeRoi.Height <= 0) return false;

            using Mat crop = new Mat(screenshot, safeRoi);
            using Mat thresh = new Mat();

            if (useRgbThresh)
            {
                // Lọc vùng màu trắng sáng (giá trị kênh từ 180 đến 255) nhằm loại bỏ các tạp chất màu vàng/hồng nền
                Cv2.InRange(crop, new Scalar(180, 180, 180), new Scalar(255, 255, 255), thresh);
                if (invert)
                {
                    Cv2.BitwiseNot(thresh, thresh);
                }
            }
            else
            {
                // Chuyển sang ảnh xám rồi nhị phân hóa
                using Mat gray = new Mat();
                Cv2.CvtColor(crop, gray, ColorConversionCodes.BGR2GRAY);
                Cv2.Threshold(gray, thresh, 180, 255, invert ? ThresholdTypes.BinaryInv : ThresholdTypes.Binary);
            }

            // Tìm các đường bao quanh (contour) ký tự chữ số đơn lẻ
            Cv2.FindContours(thresh, out Point[][] contours, out _, RetrievalModes.External, ContourApproximationModes.ApproxSimple);

            var rects = new List<Rect>();
            foreach (var c in contours)
            {
                Rect r = Cv2.BoundingRect(c);
                // Lọc bỏ nhiễu nhỏ hoặc các vệt dài không phải chữ số dựa trên kích thước thực nghiệm
                if (r.Height >= 13 && r.Width > 2 && r.Height < 45 && r.Width < 30)
                {
                    rects.Add(r);
                }
            }

            // Sắp xếp các chữ số thu được theo thứ tự x từ trái qua phải để đọc đúng hàng đơn vị, chục, trăm...
            var sortedRects = rects.OrderBy(r => r.X).ToList();

            string digits = "";
            var scores = new List<double>();
            foreach (var r in sortedRects)
            {
                using Mat charImg = new Mat(thresh, r);
                using Mat resized = new Mat();
                // Resize chữ số đơn lẻ về kích thước chuẩn 12x16 giống kích thước template mẫu
                Cv2.Resize(charImg, resized, new Size(12, 16), 0, 0, InterpolationFlags.Nearest);

                int bestDigit = 0;
                double bestScore = -1;

                var charIndexer = resized.GetGenericIndexer<byte>();

                int maxTemplates = isOffline ? 11 : 10;
                for (int d = 0; d < maxTemplates; d++)
                {
                    var tplIndexer = _templates[d].GetGenericIndexer<byte>();

                    int intersection = 0; // Số pixel trùng khớp màu trắng
                    int union = 0;        // Tổng số pixel trắng của cả hai

                    for (int row = 0; row < 16; row++)
                    {
                        for (int col = 0; col < 12; col++)
                        {
                            bool charPixel = charIndexer[row, col] > 127;
                            bool tplPixel = tplIndexer[row, col] > 127;

                            if (charPixel && tplPixel) intersection++;
                            if (charPixel || tplPixel) union++;
                        }
                    }

                    // Điểm IoU trùng khớp tỷ lệ phần giao trên phần hợp
                    double score = union == 0 ? 0 : (double)intersection / union;

                    if (score > bestScore)
                    {
                        bestScore = score;
                        bestDigit = d;
                    }
                }

                // Nếu độ khớp IoU vượt ngưỡng tin cậy 60% thì chấp nhận chữ số này
                if (bestScore > 0.60)
                {
                    int actualDigit = bestDigit == 10 ? 5 : bestDigit;
                    digits += actualDigit.ToString();
                    scores.Add(bestScore);
                }
            }

            if (string.IsNullOrEmpty(digits)) return false;

            if (!int.TryParse(digits, out value))
            {
                Console.WriteLine($"[VISION] phase=ocr status=fail reason=unparseable details=\"{digits}\"");
                return false;
            }
            confidence = scores.Count == 0 ? 0 : scores.Average();
            return true;
        }

        /// <summary>
        /// Trích xuất chỉ số số nguyên đơn giản từ vùng ROI ảnh mà không cần lấy chi tiết độ tin cậy.
        /// </summary>
        public int ExtractNumericalMetrics(Mat screenshot, Rect roi, bool isOffline = false, bool useRgbThresh = false, bool invert = false)
        {
            return TryExtractNumericalMetrics(screenshot, roi, out int value, out _, isOffline, useRgbThresh, invert)
                ? value
                : 0;
        }

        /// <summary>
        /// Giải phóng tài nguyên các ma trận chữ số OpenCV đã khởi tạo.
        /// </summary>
        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            foreach (var kvp in _templates)
            {
                kvp.Value?.Dispose();
            }
            _templates.Clear();
        }
    }
}
