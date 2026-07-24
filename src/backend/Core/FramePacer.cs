using System;
using System.Diagnostics;

namespace CvAut
{
    /// <summary>
    /// Bộ đo nhịp frame (Frame Pacer) — đo thời gian chụp màn hình thực tế
    /// và tính hệ số giãn delay động (LagFactor) để bot tự điều chỉnh
    /// tốc độ thao tác phù hợp với hiệu năng giả lập.
    ///
    /// Thuật toán:
    /// - Đo CaptureTime (ms) qua Stopwatch bọc quanh ADB screencap + decode.
    /// - Làm mịn bằng EMA (Exponential Moving Average) với α = 0.3.
    /// - Tự động hiệu chuẩn BaselineCapture từ 5 mẫu đầu tiên.
    /// - LagFactor = Max(1.0, smoothedCapture / baseline), clamp [1.0, 4.0].
    /// - AdjustDelay(baseMs) = (int)(baseMs × LagFactor).
    /// </summary>
    public sealed class FramePacer
    {
        // Hệ số trọng số EMA: 0.3 = phản ứng nhanh nhưng không quá nhạy với spike đơn lẻ
        private const double EmaAlpha = 0.3;

        // Giới hạn LagFactor tối đa để tránh delay quá lớn gây timeout
        private const double MaxLagFactor = 4.0;

        // Số mẫu đầu tiên dùng để hiệu chuẩn baseline
        private const int CalibrationSamples = 5;

        // Baseline capture time mặc định (ms) — dùng trước khi hiệu chuẩn xong
        private const double DefaultBaselineCaptureMs = 100.0;

        // Giá trị capture time tối thiểu hợp lệ (ms) — bỏ qua mẫu quá nhanh (cache/lỗi)
        private const double MinValidCaptureMs = 10.0;

        private double _smoothedCaptureMs = DefaultBaselineCaptureMs;
        private double _baselineCaptureMs = DefaultBaselineCaptureMs;
        private int _sampleCount;
        private double _calibrationSum;
        private readonly object _lock = new();

        /// <summary>
        /// Hệ số giãn delay hiện tại. Giá trị 1.0 = bình thường, >1.0 = giả lập đang lag.
        /// </summary>
        public double LagFactor
        {
            get
            {
                lock (_lock)
                {
                    if (_baselineCaptureMs <= 0) return 1.0;
                    double raw = _smoothedCaptureMs / _baselineCaptureMs;
                    return Math.Clamp(Math.Max(1.0, raw), 1.0, MaxLagFactor);
                }
            }
        }

        /// <summary>
        /// Thời gian capture đã làm mịn bằng EMA (ms).
        /// </summary>
        public double SmoothedCaptureMs
        {
            get { lock (_lock) return _smoothedCaptureMs; }
        }

        /// <summary>
        /// Thời gian capture baseline sau khi hiệu chuẩn (ms).
        /// </summary>
        public double BaselineCaptureMs
        {
            get { lock (_lock) return _baselineCaptureMs; }
        }

        /// <summary>
        /// Ghi nhận thời gian capture mới nhất (ms) từ ADB screencap.
        /// Gọi sau mỗi lần chụp màn hình thành công.
        /// </summary>
        /// <param name="captureMs">Thời gian capture thực tế tính bằng millisecond.</param>
        public void RecordCapture(long captureMs)
        {
            if (captureMs < MinValidCaptureMs) return; // Bỏ qua mẫu không hợp lệ

            lock (_lock)
            {
                _sampleCount++;

                if (_sampleCount <= CalibrationSamples)
                {
                    // Giai đoạn hiệu chuẩn: tính trung bình cộng để xác lập baseline
                    _calibrationSum += captureMs;
                    _smoothedCaptureMs = _calibrationSum / _sampleCount;

                    if (_sampleCount == CalibrationSamples)
                    {
                        _baselineCaptureMs = _smoothedCaptureMs;
                        Console.WriteLine($"[FRAME-PACER] phase=calibration status=complete baseline_ms={_baselineCaptureMs:F0} samples={CalibrationSamples}");
                    }
                }
                else
                {
                    // Giai đoạn vận hành: cập nhật EMA
                    _smoothedCaptureMs = (EmaAlpha * captureMs) + ((1.0 - EmaAlpha) * _smoothedCaptureMs);
                }
            }
        }

        /// <summary>
        /// Tính delay đã điều chỉnh theo tải giả lập hiện tại.
        /// Khi giả lập mượt: trả về đúng baseDelayMs.
        /// Khi giả lập lag: trả về baseDelayMs × LagFactor (lớn hơn, chờ lâu hơn).
        /// </summary>
        /// <param name="baseDelayMs">Giá trị delay gốc (ms).</param>
        /// <returns>Delay đã được điều chỉnh (ms), luôn >= baseDelayMs.</returns>
        public int AdjustDelay(int baseDelayMs)
        {
            double factor = LagFactor;
            int adjusted = (int)(baseDelayMs * factor);
            return Math.Max(baseDelayMs, adjusted); // Đảm bảo không bao giờ nhỏ hơn delay gốc
        }

        /// <summary>
        /// Tạo Stopwatch mới để đo thời gian capture. Dùng kết hợp với RecordCapture().
        /// </summary>
        public static Stopwatch StartCaptureMeasurement() => Stopwatch.StartNew();
    }
}
