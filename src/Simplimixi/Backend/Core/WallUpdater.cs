using System;
using System.Collections.Generic;
using System.Linq;
using OpenCvSharp;
using Point = OpenCvSharp.Point;
using Size = OpenCvSharp.Size;

namespace CvAut
{
    /// <summary>
    /// Bộ nâng cấp tường (Wall Updater):
    /// - Quét tìm các đoạn tường trên màn hình Làng chính bằng phương pháp so khớp mẫu.
    /// - Thực hiện lọc trùng lặp tọa độ để tránh bấm nhầm cùng một bức tường.
    /// - Bấm chọn tường, xác thực giao diện nâng cấp, tính toán và nâng cấp bằng Vàng hoặc Dầu hồng tùy điều kiện tài nguyên.
    /// </summary>
    internal sealed class WallUpdater
    {
        // Vùng ROI tìm kiếm tường trên bản đồ (Tránh phần rìa chứa các nút UI cản trở)
        private static readonly Rect WallSearchRoi = Rect.FromLTRB(270, 100, 1339, 785);

        // Bảng giá nâng cấp tường tiêu chuẩn theo cấp độ (Clash of Clans)
        private static readonly Dictionary<int, int> WallCosts = new()
        {
            { 8, 500_000 },     { 9, 1_000_000 },   { 10, 2_000_000 },
            { 11, 3_000_000 },  { 12, 4_000_000 },  { 13, 5_000_000 },
            { 14, 6_000_000 },  { 15, 7_000_000 },  { 16, 8_000_000 },
            { 17, 9_000_000 }
        };

        // Vùng hiển thị giá tiền nâng cấp trên bảng thông tin ở đáy màn hình
        private static readonly Rect UpgradeCostRoi = new(680, 730, 240, 45);

        // Tọa độ điểm kiểm tra màu nền xám/trắng nhạt để xác nhận bảng nâng cấp đang mở
        private static readonly Point PanelCheckPoint = new(800, 750);

        // Nút bấm gợi ý Thợ xây ở top-center (độ phân giải 1600x900)
        private static readonly Point BuilderMenuPoint = new(738, 36);

        // Điểm an toàn ngoài rìa bản đồ để bấm giải tỏa các menu/popup
        private static readonly Point HomeMenuPoint = new(140, 606);

        // Tọa độ vuốt bản đồ để tìm tường ở các góc xa
        private static readonly Point SwipeStart = new(809, 648);
        private static readonly Point SwipeEnd = new(809, 115);

        // Tọa độ vuốt cuộn bảng gợi ý Thợ xây
        private static readonly Point RetrySwipeStart = new(977, 157);
        private static readonly Point RetrySwipeEnd = new(999, 432);

        // Các điểm chạm điều hướng giao diện nâng cấp tường
        private static readonly Point DismissPoint = new(1143, 209);
        private static readonly Point FixedGoldUpgradePoint = new(920, 707);
        private static readonly Point FixedElixirUpgradePoint = new(1095, 702);
        private static readonly Point AddMoreButton = new(800, 720);
        private static readonly Point ConfirmUpgradePoint = new(1115, 782);
        private static readonly Point ConfirmMultiPoint = new(990, 620);
        private static readonly Point SafeClosePoint = new(1229, 25);

        // Ngưỡng so khớp mẫu để tìm tường (cần độ tin cậy cao để tránh nhận diện nhầm các vật thể khác)
        private const double WallSearchThreshold = 0.90;
        private const int SwipeDurationMs = 600;

        private readonly ADBHelper _adb;
        private readonly VisionEngine _vision;
        private readonly string _templatesPath;
        private const int MinSupportedWallLevel = 8;
        private const int MaxSupportedWallLevel = 17;

        // Lưu trữ vị trí index bù của bức tường nâng cấp gần nhất để tăng tốc độ chọn ở chu kỳ tiếp theo
        private int? _savedWallOffset;

        /// <summary>
        /// Khởi tạo bộ cập nhật nâng cấp tường.
        /// </summary>
        /// <param name="adb">Đối tượng ADBHelper.</param>
        /// <param name="vision">Đối tượng VisionEngine.</param>
        /// <param name="templatesPath">Thư mục chứa tệp mẫu template.</param>
        public WallUpdater(ADBHelper adb, VisionEngine vision, string templatesPath)
        {
            _adb = adb;
            _vision = vision;
            _templatesPath = templatesPath;
        }

        /// <summary>
        /// Kiểm tra lượng tài nguyên Vàng và Dầu hồng hiện tại ở Làng chính,
        /// nếu vượt ngưỡng tối thiểu (do người dùng cấu hình), thực hiện nâng cấp tường.
        /// </summary>
        /// <param name="wallLevel">Cấp độ tường đích muốn nâng cấp.</param>
        /// <param name="wallGoldThreshold">Ngưỡng Vàng tối thiểu để bắt đầu nâng tường.</param>
        /// <param name="wallElixirThreshold">Ngưỡng Dầu hồng tối thiểu để bắt đầu nâng tường.</param>
        public int HandleHomeResources(int wallLevel, int wallGoldThreshold, int wallElixirThreshold)
        {
            if (!IsSupportedWallLevel(wallLevel))
            {
                Console.WriteLine($"[WALL RESULT] phase=read_resources status=skip reason=unsupported_wall_level level={wallLevel}");
                return 0;
            }

            if (!WallCosts.TryGetValue(wallLevel, out int targetCost))
            {
                Console.WriteLine($"[WALL RESULT] phase=read_resources status=skip reason=missing_wall_cost level={wallLevel}");
                return 0;
            }

            var (gold, elixir, _) = IsTarget.ExtractHomeResources(_adb, _vision);
            Console.WriteLine($"[WALL] phase=read_resources gold={gold:N0} elixir={elixir:N0} level={wallLevel} cost={targetCost:N0} status=ok");

            int availableGold = Math.Max(0, gold - 100_000);
            int availableElixir = elixir;
            int affordGold = availableGold / targetCost;
            int affordElixir = availableElixir / targetCost;

            if (gold < wallGoldThreshold)
            {
                affordGold = 0;
            }

            if (elixir < wallElixirThreshold)
            {
                affordElixir = 0;
            }

            Console.WriteLine($"[WALL DECISION] phase=read_resources afford_gold={affordGold} afford_elixir={affordElixir} gthr={wallGoldThreshold:N0} ethr={wallElixirThreshold:N0} status=check");

            if (affordGold == 0 && affordElixir == 0)
            {
                Console.WriteLine("[WALL RESULT] phase=read_resources status=skip count=0 reason=cannot_afford_or_below_threshold");
                return 0;
            }

            string bestResource;
            int qtyToDo;
            if (affordGold >= affordElixir && affordGold > 0)
            {
                bestResource = "gold";
                qtyToDo = affordGold;
            }
            else
            {
                bestResource = "elixir";
                qtyToDo = affordElixir;
            }

            Console.WriteLine($"[WALL DECISION] phase=decide resource={bestResource} qty={qtyToDo} status=selected");
            bool success = UpgradeWallBulk(bestResource, wallLevel, gold, elixir);
            Console.WriteLine($"[WALL RESULT] phase=read_resources status={(success ? "upgraded" : "skip")} count={(success ? qtyToDo : 0)} reason={(success ? "wall_upgraded" : "upgrade_failed")}");
            return success ? qtyToDo : 0;
        }

        /// <summary>
        /// Thực hiện quy trình nâng cấp hàng loạt tường lên cấp độ chỉ định bằng tài nguyên vàng hoặc elixir.
        /// Thử nghiệm tối đa 3 bức tường cho đến khi tìm được bức tường xác thực hợp lệ.
        /// </summary>
        /// <returns>True nếu thao tác xác nhận nâng cấp thành công, ngược lại False.</returns>
        private bool UpgradeWallBulk(string resource, int wallLevel, int currentGold, int currentElixir)
        {
            Console.WriteLine($"[WALL] phase=attempt_upgrade resource={resource} level={wallLevel} status=start");

            var triedCoords = new List<Point>();
            Point? validCoord = null;

            for (int attempt = 0; attempt < 3; attempt++)
            {
                // Lấy tất cả các tường tìm thấy trong bảng gợi ý Thợ xây
                List<Point> coords = FindAllWallCoords()
                    .Where(point => !triedCoords.Any(tried => Math.Abs(point.Y - tried.Y) <= 20))
                    .ToList();

                if (coords.Count == 0)
                {
                    Console.WriteLine($"[WALL RESULT] phase=attempt_upgrade resource={resource} level={wallLevel} status=skip reason=no_candidates");
                    _adb.Tap(422, 68); // Tap an toàn giải tỏa
                    return false;
                }

                Point candidate;
                // Nếu đã lưu offset thành công từ lần trước, ưu tiên chọn tường quanh khu vực đó
                if (_savedWallOffset.HasValue && _savedWallOffset.Value >= -coords.Count && _savedWallOffset.Value < coords.Count)
                {
                    candidate = coords[IndexFromEnd(coords, _savedWallOffset.Value)];
                }
                else
                {
                    candidate = coords[coords.Count - 1];
                }

                triedCoords.Add(candidate);

                // Nhấp chọn biểu tượng Wall trong bảng gợi ý Thợ xây để game tự định vị và chọn tường
                _adb.Tap(candidate.X, candidate.Y);
                Thread.Sleep(1000);

                // Tắt bảng gợi ý Thợ xây để lộ giao diện nâng cấp dưới đáy màn hình
                _adb.Tap(BuilderMenuPoint.X, BuilderMenuPoint.Y);
                Thread.Sleep(500);

                // Kiểm tra xem bảng nâng cấp đã mở và khớp giá tiền cấp độ tương ứng hay không
                if (ValidateWallTapNew(wallLevel))
                {
                    validCoord = candidate;
                    _savedWallOffset ??= -1 - attempt;
                    break;
                }

                // Nếu không đúng tường (hoặc chạm nhầm công trình khác), tắt menu đi thử lại
                _adb.Tap(DismissPoint.X, DismissPoint.Y);
                Thread.Sleep(500);

                // Nếu thử sai khi đang dùng vị trí lưu từ trước, xóa lưu vị trí để thử các tọa độ khác
                _savedWallOffset = null;
            }

            if (!validCoord.HasValue)
            {
                Console.WriteLine($"[WALL RESULT] phase=attempt_upgrade resource={resource} level={wallLevel} status=skip reason=unvalidated");
                return false;
            }

            int targetCost = WallCosts[wallLevel];
            int available = resource.Equals("gold", StringComparison.OrdinalIgnoreCase)
                ? Math.Max(0, currentGold - 100_000)
                : currentElixir;
            int qtyToDo = available / targetCost;

            if (qtyToDo <= 0)
            {
                Console.WriteLine($"[WALL RESULT] phase=attempt_upgrade resource={resource} level={wallLevel} status=skip reason=insufficient_resources_virtual cost={targetCost:N0}");
                _adb.Tap(DismissPoint.X, DismissPoint.Y);
                return false;
            }

            if (qtyToDo > 1)
            {
                Console.WriteLine($"[WALL] phase=add_more resource={resource} level={wallLevel} count={qtyToDo - 1} status=start");
                for (int i = 0; i < qtyToDo - 1; i++)
                {
                    _adb.Tap(AddMoreButton.X, AddMoreButton.Y);
                    Thread.Sleep(350);
                }
            }

            Point upgradePoint = resource.Equals("gold", StringComparison.OrdinalIgnoreCase)
                ? FixedGoldUpgradePoint
                : FixedElixirUpgradePoint;
            _adb.Tap(upgradePoint.X, upgradePoint.Y);
            Thread.Sleep(1000);

            Point confirmPoint = qtyToDo > 1 ? ConfirmMultiPoint : ConfirmUpgradePoint;
            _adb.Tap(confirmPoint.X, confirmPoint.Y);
            Thread.Sleep(500);

            _adb.Tap(SafeClosePoint.X, SafeClosePoint.Y);

            Console.WriteLine($"[WALL RESULT] phase=attempt_upgrade resource={resource} level={wallLevel} qty={qtyToDo} status=upgraded reason=confirmed");
            Thread.Sleep(1000);
            return true;
        }

        /// <summary>
        /// Tìm kiếm tất cả các tọa độ đoạn tường hiển thị trên màn hình hiện tại.
        /// Hỗ trợ vuốt trượt tìm kiếm tối đa 7 lần nếu chưa tìm thấy ứng viên tường nào.
        /// </summary>
        /// <param name="wallLevel">Cấp độ tường hiện tại cần tìm để nâng cấp.</param>
        private List<Point> FindAllWallCoords()
        {
            PrepareWallSearch();

            string[] templateNames = new[]
            {
                "wall.png",
                "wall_2.png",
                "wall_3.png",
                "wall_4.png"
            }.Where(name => TemplateAssetLoader.Exists(_templatesPath, name)).ToArray();

            if (templateNames.Length == 0)
            {
                Console.WriteLine("[WALL WARN] No generic wall templates found in Templates directory.");
                return new List<Point>();
            }

            Console.WriteLine($"[WALL] phase=search_templates count={templateNames.Length} status=ok reason=loaded");

            for (int attempt = 0; attempt < 7; attempt++)
            {
                if (attempt > 0)
                {
                    // Vuốt bảng gợi ý Thợ xây đi một chút để tìm dòng gợi ý nâng tường tiếp theo
                    _adb.Swipe(RetrySwipeStart.X, RetrySwipeStart.Y, RetrySwipeEnd.X, RetrySwipeEnd.Y, SwipeDurationMs);
                    Thread.Sleep(800);
                }

                using Mat? screenshot = _adb.TakeScreenshot();
                if (screenshot == null || screenshot.Empty())
                {
                    Console.WriteLine("[WALL WARN] Screenshot failed while searching walls.");
                    continue;
                }

                Rect roi = ImageUtils.ClampRect(WallSearchRoi, screenshot.Width, screenshot.Height);
                if (roi.Width <= 0 || roi.Height <= 0)
                {
                    Console.WriteLine("[WALL WARN] Wall ROI is empty; check screenshot size.");
                    return new List<Point>();
                }

                using Mat roiBgr = new Mat(screenshot, roi);
                using Mat roiGray = new Mat();
                Cv2.CvtColor(roiBgr, roiGray, ColorConversionCodes.BGR2GRAY);

                var merged = new List<Point>();
                // Chạy so khớp cho từng template mẫu biểu tượng Tường trong bảng gợi ý
                foreach (string templateName in templateNames)
                {
                    merged.AddRange(MatchWallTemplate(roiGray, templateName));
                }

                // Loại bỏ các tọa độ bị trùng lặp sát nhau (bán kính 10px) và sắp xếp tăng dần theo trục Y
                List<Point> coords = DedupeCoords(merged, 10)
                    .OrderBy(point => point.Y)
                    .ThenBy(point => point.X)
                    .ToList();

                if (coords.Count > 0)
                {
                    Console.WriteLine($"[WALL] phase=search_candidates count={coords.Count} status=ok reason=matched");
                    return coords;
                }
            }

            return new List<Point>();
        }

        private static bool IsSupportedWallLevel(int wallLevel)
        {
            return wallLevel >= MinSupportedWallLevel && wallLevel <= MaxSupportedWallLevel;
        }

        /// <summary>
        /// Chuẩn bị giao diện để bắt đầu tìm tường (Mở bảng gợi ý thợ xây và vuốt map chuẩn).
        /// </summary>
        private void PrepareWallSearch()
        {
            Thread.Sleep(500);
            _adb.Tap(BuilderMenuPoint.X, BuilderMenuPoint.Y); // Bấm mở bảng gợi ý Thợ xây
            Thread.Sleep(1000);

            // Vuốt kéo bản đồ 6 lần về hướng rìa bản đồ
            for (int i = 0; i < 6; i++)
            {
                _adb.Swipe(SwipeStart.X, SwipeStart.Y, SwipeEnd.X, SwipeEnd.Y, SwipeDurationMs);
            }

            Thread.Sleep(500);
        }

        /// <summary>
        /// Thực hiện so khớp mẫu ảnh tường (có hỗ trợ kênh Alpha làm mặt nạ mask nếu tệp ảnh 4 kênh).
        /// </summary>
        private IEnumerable<Point> MatchWallTemplate(Mat grayRoi, string templateName)
        {
            using Mat raw = TemplateAssetLoader.Load(_templatesPath, templateName, ImreadModes.Unchanged);
            if (raw.Empty())
            {
                yield break;
            }

            using Mat templateGray = new Mat();
            using Mat? mask = BuildTemplateGrayAndMask(raw, templateGray);
            if (grayRoi.Width < templateGray.Width || grayRoi.Height < templateGray.Height)
            {
                yield break;
            }

            using Mat result = new Mat();
            if (mask != null && !mask.Empty())
            {
                // Khớp mẫu có mặt nạ (Masked Template Matching) để bỏ qua phần nền trong suốt của viên tường mẫu
                Cv2.MatchTemplate(grayRoi, templateGray, result, TemplateMatchModes.CCoeffNormed, mask);
            }
            else
            {
                Cv2.MatchTemplate(grayRoi, templateGray, result, TemplateMatchModes.CCoeffNormed);
            }

            // Áp dụng phép dãn nở ảnh (Dilate) để lọc lấy giá trị cực đại địa phương (Local Maxima)
            using Mat dilated = new Mat();
            using Mat kernel = Cv2.GetStructuringElement(MorphShapes.Rect, new Size(3, 3));
            Cv2.Dilate(result, dilated, kernel);

            for (int y = 0; y < result.Rows; y++)
            {
                for (int x = 0; x < result.Cols; x++)
                {
                    float value = result.At<float>(y, x);
                    // Chỉ giữ lại tọa độ có độ tin cậy vượt ngưỡng và là cực đại cục bộ
                    if (value >= WallSearchThreshold && Math.Abs(value - dilated.At<float>(y, x)) < 0.0001)
                    {
                        yield return new Point(
                            WallSearchRoi.X + x + templateGray.Width / 2,
                            WallSearchRoi.Y + y + templateGray.Height / 2);
                    }
                }
            }
        }

        /// <summary>
        /// Xác thực bảng nâng cấp tường bằng màu panel và OCR giá tiền, không phụ thuộc template ảnh theo cấp.
        /// </summary>
        private bool ValidateWallTapNew(int wallLevel)
        {
            if (!IsSupportedWallLevel(wallLevel))
            {
                Console.WriteLine($"[WALL WARN] phase=validate status=skip level={wallLevel} reason=unsupported_wall_level supported={MinSupportedWallLevel}-{MaxSupportedWallLevel}");
                return false;
            }

            using Mat? screenshot = _adb.TakeScreenshot();
            if (screenshot == null || screenshot.Empty())
            {
                Console.WriteLine("[WALL RESULT] phase=validate status=fail reason=screenshot_failed");
                return false;
            }

            int width = screenshot.Width;
            int height = screenshot.Height;
            int px = Math.Clamp(PanelCheckPoint.X, 0, width - 1);
            int py = Math.Clamp(PanelCheckPoint.Y, 0, height - 1);
            Vec3b pixel = screenshot.At<Vec3b>(py, px);
            bool panelOpen = pixel.Item0 >= 200 && pixel.Item1 >= 200 && pixel.Item2 >= 200;

            if (!panelOpen)
            {
                Console.WriteLine($"[WALL RESULT] phase=validate status=fail reason=panel_not_open pixel_bgr=[{pixel.Item0},{pixel.Item1},{pixel.Item2}]");
                return false;
            }

            if (!WallCosts.TryGetValue(wallLevel, out int expectedCost))
            {
                Console.WriteLine($"[WALL RESULT] phase=validate status=fail reason=missing_wall_cost level={wallLevel}");
                return false;
            }

            Rect safeRoi = ImageUtils.ClampRect(UpgradeCostRoi, width, height);
            if (safeRoi.Width <= 0 || safeRoi.Height <= 0)
            {
                Console.WriteLine($"[WALL RESULT] phase=validate status=fail reason=empty_cost_roi width={width} height={height}");
                return false;
            }

            if (_vision.TryExtractNumericalMetrics(screenshot, safeRoi, out int readCost, out double confidence, useRgbThresh: true))
            {
                double error = Math.Abs(readCost - expectedCost) / (double)expectedCost;
                if (error <= 0.15)
                {
                    Console.WriteLine($"[WALL RESULT] phase=validate level={wallLevel} status=pass read={readCost:N0} expected={expectedCost:N0} conf={confidence:F2} reason=cost_matched");
                    return true;
                }

                Console.WriteLine($"[WALL RESULT] phase=validate level={wallLevel} status=retry read={readCost:N0} expected={expectedCost:N0} error={error:P2} reason=cost_mismatch");
            }
            else
            {
                Console.WriteLine($"[WALL RESULT] phase=validate level={wallLevel} status=retry reason=ocr_failed_to_extract");
            }

            return false;
        }


        /// <summary>
        /// Phân tách ảnh nguồn 4 kênh (có alpha) thành ảnh xám và ảnh mặt nạ nhị phân (mask) để phục vụ so khớp mẫu trong suốt.
        /// </summary>
        private static Mat? BuildTemplateGrayAndMask(Mat raw, Mat templateGray)
        {
            if (raw.Channels() == 4)
            {
                Mat[] channels = Cv2.Split(raw);
                try
                {
                    using Mat bgr = new Mat();
                    Cv2.Merge(channels.Take(3).ToArray(), bgr);
                    Cv2.CvtColor(bgr, templateGray, ColorConversionCodes.BGR2GRAY);

                    // Tạo mặt nạ nhị phân dựa trên kênh alpha (Kênh 3)
                    Mat mask = new Mat();
                    Cv2.Threshold(channels[3], mask, 0, 255, ThresholdTypes.Binary);
                    return mask;
                }
                finally
                {
                    foreach (Mat ch in channels) ch.Dispose();
                }
            }

            Cv2.CvtColor(raw, templateGray, ColorConversionCodes.BGR2GRAY);
            return null;
        }

        /// <summary>
        /// Loại bỏ các điểm tọa độ quá gần nhau để loại trừ trường hợp nhận diện nhiều điểm khớp trên cùng một bức tường.
        /// </summary>
        private static List<Point> DedupeCoords(IEnumerable<Point> coords, int tolerance)
        {
            var result = new List<Point>();
            foreach (Point point in coords)
            {
                if (result.Any(existing =>
                        Math.Abs(point.X - existing.X) <= tolerance &&
                        Math.Abs(point.Y - existing.Y) <= tolerance))
                {
                    continue;
                }

                result.Add(point);
            }

            return result;
        }

        /// <summary>
        /// Quy đổi chỉ số index âm (giống cú pháp Python -1, -2) sang chỉ số dương tương ứng trong List.
        /// </summary>
        private static int IndexFromEnd<T>(IReadOnlyList<T> list, int negativeIndex)
        {
            return negativeIndex < 0 ? list.Count + negativeIndex : negativeIndex;
        }

        /// <summary>
        /// Xóa bỏ vị trí bức tường đã lưu để bắt đầu tìm kiếm lại từ đầu ở chu kỳ sau.
        /// </summary>
        public void ResetSavedOffset()
        {
            _savedWallOffset = null;
        }
    }
}
