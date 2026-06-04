using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using OpenCvSharp;
using Point = OpenCvSharp.Point;
using Size = OpenCvSharp.Size;

namespace CvAut
{
    public class VisionEngine : IDisposable
    {
        private readonly string _templatesDir;
        private readonly Dictionary<int, Mat> _templates = new();
        private bool _disposed;

        public VisionEngine(string templatesDir = "Templates")
        {
            _templatesDir = templatesDir;
            if (!Directory.Exists(templatesDir))
            {
                Directory.CreateDirectory(templatesDir);
            }
            InitializeDigitTemplates();
        }

        private void InitializeDigitTemplates()
        {
            // Predefined 12x16 binary templates for Clash of Clans Supercell Magic digits
            byte[][,] rawTemplates = new byte[11][,]
            {
                // Digit 0
                new byte[16, 12] {
                    {0,0,0,1,1,1,1,1,1,1,0,0}, {0,0,1,1,1,1,1,1,1,1,1,0}, {0,1,1,1,1,1,1,1,1,1,1,1}, {1,1,1,1,1,1,1,1,1,1,1,1},
                    {1,1,1,1,1,0,0,0,1,1,1,1}, {1,1,1,1,0,0,0,0,0,1,1,1}, {1,1,1,1,0,0,0,0,0,1,1,1}, {1,1,1,1,0,0,0,0,0,1,1,1},
                    {1,1,1,1,0,0,0,0,0,1,1,1}, {1,1,1,1,0,0,0,0,0,1,1,1}, {1,1,1,1,0,0,0,0,0,1,1,1}, {1,1,1,1,0,0,0,0,0,1,1,1},
                    {1,1,1,1,1,0,0,0,1,1,1,1}, {0,1,1,1,1,1,1,1,1,1,1,1}, {0,0,1,1,1,1,1,1,1,1,1,0}, {0,0,0,1,1,1,1,1,1,1,0,0}
                },
                // Digit 1
                new byte[16, 12] {
                    {0,0,1,1,1,1,1,1,1,1,1,1}, {1,1,1,1,1,1,1,1,1,1,1,1}, {1,1,1,1,1,1,1,1,1,1,1,1}, {1,1,1,1,1,1,1,1,1,1,1,1},
                    {0,0,0,0,1,1,1,1,1,1,1,1}, {0,0,0,0,1,1,1,1,1,1,1,1}, {0,0,0,0,1,1,1,1,1,1,1,1}, {0,0,0,0,1,1,1,1,1,1,1,1},
                    {0,0,0,0,1,1,1,1,1,1,1,1}, {0,0,0,0,1,1,1,1,1,1,1,1}, {0,0,0,0,1,1,1,1,1,1,1,1}, {0,0,0,0,1,1,1,1,1,1,1,1},
                    {0,0,0,0,1,1,1,1,1,1,1,1}, {0,0,0,0,1,1,1,1,1,1,1,1}, {0,0,0,0,1,1,1,1,1,1,1,1}, {0,0,0,0,1,1,1,1,1,1,1,1}
                },
                // Digit 2
                new byte[16, 12] {
                    {0,0,0,0,1,1,1,1,1,1,0,0}, {0,0,1,1,1,1,1,1,1,1,1,1}, {0,1,1,1,1,1,1,1,1,1,1,1}, {1,1,1,1,1,1,1,1,1,1,1,1},
                    {1,1,1,1,0,0,0,0,1,1,1,1}, {0,0,0,0,0,0,0,1,1,1,1,1}, {0,0,0,0,0,0,1,1,1,1,1,0}, {0,0,0,0,0,1,1,1,1,1,0,0},
                    {0,0,0,0,1,1,1,1,1,0,0,0}, {0,0,0,1,1,1,1,1,0,0,0,0}, {0,0,1,1,1,1,1,0,0,0,0,0}, {0,1,1,1,1,1,0,0,0,0,0,0},
                    {1,1,1,1,1,1,1,1,1,1,1,1}, {1,1,1,1,1,1,1,1,1,1,1,1}, {1,1,1,1,1,1,1,1,1,1,1,1}, {1,1,1,1,1,1,1,1,1,1,1,1}
                },
                // Digit 3
                new byte[16, 12] {
                    {0,0,1,1,1,1,1,1,0,0,0,0}, {0,1,1,1,1,1,1,1,1,1,1,0}, {0,1,1,1,1,1,1,1,1,1,1,0}, {0,1,1,1,1,1,1,1,1,1,1,1},
                    {0,0,0,0,0,0,1,1,1,1,1,1}, {0,0,0,0,0,0,1,1,1,1,1,1}, {0,0,0,0,0,0,1,1,1,1,1,0}, {0,0,0,1,1,1,1,1,1,1,0,0},
                    {0,0,0,1,1,1,1,1,1,1,1,1}, {0,0,0,0,0,0,1,1,1,1,1,1}, {0,0,0,0,0,0,0,1,1,1,1,1}, {0,0,0,0,0,0,0,1,1,1,1,1},
                    {0,0,0,1,1,1,1,1,1,1,1,1}, {1,1,1,1,1,1,1,1,1,1,1,1}, {1,1,1,1,1,1,1,1,1,1,1,1}, {1,1,1,1,1,1,1,1,1,1,0,0}
                },
                // Digit 4
                new byte[16, 12] {
                    {0,0,0,0,1,1,1,1,1,1,0,0}, {0,0,0,0,1,1,1,1,1,1,0,0}, {0,0,0,1,1,1,1,1,1,1,0,0}, {0,0,0,1,1,1,1,1,1,1,0,0},
                    {0,0,0,1,1,1,0,1,1,1,0,0}, {0,0,1,1,1,0,0,1,1,1,0,0}, {0,0,1,1,1,0,0,1,1,1,0,0}, {0,1,1,1,1,0,0,1,1,1,0,0},
                    {1,1,1,1,1,0,0,1,1,1,0,0}, {1,1,1,1,1,1,1,1,1,1,1,1}, {1,1,1,1,1,1,1,1,1,1,1,1}, {1,1,1,1,1,1,1,1,1,1,1,1},
                    {1,1,1,1,1,1,1,1,1,1,1,1}, {0,0,0,0,0,0,0,1,1,1,0,0}, {0,0,0,0,0,0,0,1,1,1,0,0}, {0,0,0,0,0,0,0,1,1,1,0,0}
                },
                // Digit 5 (Flat-top 5)
                new byte[16, 12] {
                    {0,1,1,1,1,1,1,1,1,1,1,1}, {0,1,1,1,1,1,1,1,1,1,1,1}, {1,1,1,1,1,1,1,1,1,1,1,1}, {1,1,1,1,1,1,1,1,1,0,0,0},
                    {1,1,1,1,1,1,0,0,0,0,0,0}, {1,1,1,1,1,1,0,0,0,0,0,0}, {1,1,1,1,1,1,1,1,0,0,0,0}, {1,1,1,1,1,1,1,1,1,1,1,1},
                    {1,1,1,1,1,1,1,1,1,1,1,1}, {0,0,1,1,1,1,1,1,1,1,1,1}, {0,0,0,0,0,0,1,1,1,1,1,1}, {0,0,0,0,0,0,1,1,1,1,1,1},
                    {0,0,0,1,1,1,1,1,1,1,1,1}, {0,1,1,1,1,1,1,1,1,1,1,1}, {0,1,1,1,1,1,1,1,1,1,0,0}, {0,1,1,1,1,1,1,1,0,0,0,0}
                },
                // Digit 6 (Curved-top 6)
                new byte[16, 12] {
                    {0,0,0,0,0,1,1,1,1,1,1,0}, {0,0,1,1,1,1,1,1,1,1,1,1}, {0,1,1,1,1,1,1,1,1,1,1,1}, {0,1,1,1,1,1,1,1,1,1,1,1},
                    {0,1,1,1,1,1,1,0,0,0,0,0}, {0,1,1,1,1,0,0,0,0,0,0,0}, {1,1,1,1,1,1,0,0,0,0,0,0}, {1,1,1,1,1,1,1,1,1,1,1,0},
                    {1,1,1,1,1,1,1,1,1,1,1,1}, {1,1,1,1,1,1,1,1,1,1,1,1}, {1,1,1,1,1,1,1,1,1,1,1,1}, {1,1,1,1,1,0,0,0,1,1,1,1},
                    {1,1,1,1,1,0,0,0,1,1,1,1}, {1,1,1,1,1,1,1,1,1,1,1,1}, {1,1,1,1,1,1,1,1,1,1,1,1}, {0,1,1,1,1,1,1,1,1,1,1,1}
                },
                // Digit 7
                new byte[16, 12] {
                    {0,0,0,0,0,1,1,1,1,1,0,0}, {1,1,1,1,1,1,1,1,1,1,1,1}, {1,1,1,1,1,1,1,1,1,1,1,1}, {1,1,1,1,1,1,1,1,1,1,1,1},
                    {0,0,0,0,0,0,1,1,1,1,1,1}, {0,0,0,0,0,0,1,1,1,1,1,1}, {0,0,0,0,0,1,1,1,1,1,1,1}, {0,0,0,0,0,1,1,1,1,1,1,0},
                    {0,0,0,0,1,1,1,1,1,1,1,0}, {0,0,0,0,1,1,1,1,1,1,0,0}, {0,0,0,0,1,1,1,1,1,1,0,0}, {0,0,0,1,1,1,1,1,1,0,0,0},
                    {0,0,1,1,1,1,1,1,0,0,0,0}, {0,0,1,1,1,1,1,1,0,0,0,0}, {0,1,1,1,1,1,1,0,0,0,0,0}, {0,1,1,1,1,1,1,0,0,0,0,0}
                },
                // Digit 8
                new byte[16, 12] {
                    {0,0,0,0,1,1,1,1,1,0,0,0}, {0,0,1,1,1,1,1,1,1,1,1,0}, {0,1,1,1,1,1,1,1,1,1,1,1}, {0,1,1,1,1,1,1,1,1,1,1,1},
                    {0,1,1,1,1,0,0,1,1,1,1,1}, {1,1,1,1,1,0,0,1,1,1,1,1}, {0,1,1,1,1,1,1,1,1,1,1,0}, {0,0,1,1,1,1,1,1,1,1,0,0},
                    {0,0,0,1,1,1,1,1,1,1,1,0}, {0,0,1,1,1,1,1,1,1,1,1,1}, {0,1,1,1,1,1,1,1,1,1,1,1}, {1,1,1,1,0,0,0,0,1,1,1,1},
                    {1,1,1,1,1,0,0,0,1,1,1,1}, {1,1,1,1,1,0,0,1,1,1,1,1}, {1,1,1,1,1,1,1,1,1,1,1,1}, {0,1,1,1,1,1,1,1,1,1,1,1}
                },
                // Digit 9
                new byte[16, 12] {
                    {0,0,1,1,1,1,1,1,1,1,1,0}, {0,1,1,1,1,1,1,1,1,1,1,1}, {0,1,1,1,1,1,1,1,1,1,1,1}, {0,1,1,1,1,1,1,1,1,1,1,1},
                    {1,1,1,1,1,0,0,0,1,1,1,1}, {1,1,1,1,1,0,0,0,1,1,1,1}, {1,1,1,1,0,0,0,0,1,1,1,1}, {1,1,1,1,1,1,1,1,1,1,1,1},
                    {1,1,1,1,1,1,1,1,1,1,1,1}, {1,1,1,1,1,1,1,1,1,1,1,1}, {0,0,0,0,0,0,0,0,1,1,1,1}, {0,0,0,0,0,0,0,0,1,1,1,1},
                    {0,0,0,0,0,0,0,1,1,1,1,1}, {0,0,0,0,1,1,1,1,1,1,1,1}, {0,0,1,1,1,1,1,1,1,1,1,1}, {0,0,1,1,1,1,1,1,1,1,0,0}
                },
                // Digit 10 (Offline-compatible Curved-top 5)
                new byte[16, 12] {
                    {0,0,0,0,0,1,1,1,1,1,1,0}, {0,0,1,1,1,1,1,1,1,1,1,1}, {0,1,1,1,1,1,1,1,1,1,1,1}, {0,1,1,1,1,1,1,1,1,1,1,1},
                    {0,1,1,1,1,1,1,1,0,0,0,0}, {0,1,1,1,1,0,0,0,0,0,0,0}, {1,1,1,1,1,1,0,0,0,0,0,0}, {1,1,1,1,1,1,1,1,1,1,1,1},
                    {1,1,1,1,1,1,1,1,1,1,1,1}, {1,1,1,1,1,1,1,1,1,1,1,1}, {1,1,1,1,1,1,1,1,1,1,1,1}, {1,1,1,1,1,0,0,0,1,1,1,1},
                    {1,1,1,1,1,0,0,0,1,1,1,1}, {1,1,1,1,1,1,1,1,1,1,1,1}, {1,1,1,1,1,1,1,1,1,1,1,1}, {0,1,1,1,1,1,1,1,1,1,1,1}
                }
            };

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

        public Point? FindElement(Mat screenshot, string templateName, double threshold = 0.70)
        {
            return FindElement(screenshot, templateName, threshold, null, out _);
        }

        public Point? FindElement(Mat screenshot, string templateName, double threshold, Rect? roi, out double score)
        {
            score = 0;
            if (screenshot.Empty()) return null;

            string templatePath = Path.Combine(_templatesDir, $"{templateName}.png");
            if (!File.Exists(templatePath))
            {
                Console.WriteLine($"[VISION WARNING] Template không tồn tại: {templatePath}");
                return null;
            }

            using Mat template = Cv2.ImRead(templatePath, ImreadModes.Color);
            if (template.Empty()) return null;

            Rect searchRect = roi.HasValue
                ? ImageUtils.ClampRect(roi.Value, screenshot.Width, screenshot.Height)
                : new Rect(0, 0, screenshot.Width, screenshot.Height);

            if (searchRect.Width < template.Width || searchRect.Height < template.Height)
            {
                return null;
            }

            using Mat searchArea = new Mat(screenshot, searchRect);
            using Mat res = new Mat();
            Cv2.MatchTemplate(searchArea, template, res, TemplateMatchModes.CCoeffNormed);
            Cv2.MinMaxLoc(res, out _, out double maxVal, out _, out Point maxLoc);
            score = maxVal;

            if (maxVal >= threshold)
            {
                int centerX = searchRect.X + maxLoc.X + template.Width / 2;
                int centerY = searchRect.Y + maxLoc.Y + template.Height / 2;
                return new Point(centerX, centerY);
            }

            return null;
        }

        public bool TryExtractNumericalMetrics(Mat screenshot, Rect roi, out int value, out double confidence, bool isOffline = false, bool useRgbThresh = false)
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
                Cv2.InRange(crop, new Scalar(180, 180, 180), new Scalar(255, 255, 255), thresh);
            }
            else
            {
                using Mat gray = new Mat();
                Cv2.CvtColor(crop, gray, ColorConversionCodes.BGR2GRAY);
                Cv2.Threshold(gray, thresh, 180, 255, ThresholdTypes.Binary);
            }

            Cv2.FindContours(thresh, out Point[][] contours, out _, RetrievalModes.External, ContourApproximationModes.ApproxSimple);

            var rects = new List<Rect>();
            foreach (var c in contours)
            {
                Rect r = Cv2.BoundingRect(c);
                if (r.Height >= 13 && r.Width > 2 && r.Height < 45 && r.Width < 30)
                {
                    rects.Add(r);
                }
            }

            // Sắp xếp các chữ số từ trái qua phải
            var sortedRects = rects.OrderBy(r => r.X).ToList();

            string digits = "";
            var scores = new List<double>();
            foreach (var r in sortedRects)
            {
                using Mat charImg = new Mat(thresh, r);
                using Mat resized = new Mat();
                Cv2.Resize(charImg, resized, new Size(12, 16), 0, 0, InterpolationFlags.Nearest);

                int bestDigit = 0;
                double bestScore = -1;

                var charIndexer = resized.GetGenericIndexer<byte>();

                int maxTemplates = isOffline ? 11 : 10;
                for (int d = 0; d < maxTemplates; d++)
                {
                    var tplIndexer = _templates[d].GetGenericIndexer<byte>();

                    int intersection = 0;
                    int union = 0;

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

                    double score = union == 0 ? 0 : (double)intersection / union;

                    if (score > bestScore)
                    {
                        bestScore = score;
                        bestDigit = d;
                    }
                }

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
                Console.WriteLine($"[VISION] Cảnh báo: không parse được giá trị OCR '{digits}'");
                return false;
            }
            confidence = scores.Count == 0 ? 0 : scores.Average();
            return true;
        }

        public int ExtractNumericalMetrics(Mat screenshot, Rect roi, bool isOffline = false, bool useRgbThresh = false)
        {
            return TryExtractNumericalMetrics(screenshot, roi, out int value, out _, isOffline, useRgbThresh)
                ? value
                : 0;
        }

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
