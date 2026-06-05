using System;
using System.Collections.Generic;
using OpenCvSharp;

namespace CvAut
{
    /// <summary>
    /// Lớp tiện ích cung cấp các toạ độ và chức năng để quét, phân tích tài nguyên (Vàng, Dầu hồng, Dầu đen)
    /// từ các màn hình của Clash of Clans. Được thiết kế tối ưu cho độ phân giải giả lập chuẩn 1600x900px.
    /// </summary>
    public static class IsTarget
    {
        // Tọa độ vùng quét tài nguyên đối thủ khi đang đi tìm trận (Scout Screen)
        // Dựa trên chuẩn giao diện độ phân giải 1600x900px
        private static readonly Dictionary<string, Rect> Coords = new()
        {
            { "Gold", new Rect(55, 117, 196, 44) },        // Vàng: x=55, y=117, rộng=196, cao=44 (Vùng hiển thị số lượng vàng cướp được)
            { "Elixir", new Rect(60, 167, 201, 41) },      // Dầu hồng: x=60, y=167, rộng=201, cao=41
            { "Dark Elixir", new Rect(73, 214, 110, 34) }  // Dầu đen: x=73, y=214, rộng=110, cao=34
        };

        // Lề cắt rộng bù trừ margin để đảm bảo lấy trọn chữ số, tránh bị mất viền của ký tự đầu hoặc cuối
        private static readonly Dictionary<string, Padding> Margins = new()
        {
            { "Gold", new Padding { L = 60, R = 15, T = 5, B = 5 } }, // Thêm lề trái rộng do số vàng có thể dài
            { "Elixir", new Padding { L = 15, R = 15, T = 5, B = 5 } },
            { "Dark Elixir", new Padding { L = 15, R = 15, T = 5, B = 5 } }
        };

        /// <summary>
        /// Cấu trúc lưu trữ khoảng lề bù trừ xung quanh vùng quét.
        /// </summary>
        private struct Padding
        {
            public int L { get; set; } // Left (Lề Trái)
            public int R { get; set; } // Right (Lề Phải)
            public int T { get; set; } // Top (Lề Trên)
            public int B { get; set; } // Bottom (Lề Dưới)
        }

        /// <summary>
        /// Thực hiện chụp ảnh màn hình giả lập và trích xuất các chỉ số tài nguyên hiện tại của nhà đối thủ (khi tìm trận).
        /// </summary>
        /// <param name="adb">Đối tượng ADBHelper để thực hiện giao tiếp với thiết bị.</param>
        /// <param name="vision">Đối tượng VisionEngine chứa thuật toán OCR.</param>
        /// <returns>Bộ ba số nguyên biểu thị lượng (Vàng, Dầu hồng, Dầu đen) nhận diện được.</returns>
        public static (int Gold, int Elixir, int DarkElixir) ExtractResources(ADBHelper adb, VisionEngine vision)
        {
            Console.WriteLine("[SCOUT] Reading target resources...");
            using Mat? screenshot = adb.TakeScreenshot();
            if (screenshot == null || screenshot.Empty())
            {
                Console.WriteLine("[SCOUT ERROR] Screenshot unavailable.");
                return (0, 0, 0);
            }

            int hImg = screenshot.Height;
            int wImg = screenshot.Width;
            var results = new Dictionary<string, int>();

            foreach (var kvp in Coords)
            {
                string label = kvp.Key;
                Rect r = kvp.Value;
                Padding p = Margins[label];

                // Tính toán tọa độ cắt bù trừ an toàn chống lỗi tràn biên (Out of bounds)
                int x1p = Math.Max(0, r.X - p.L);
                int y1p = Math.Max(0, r.Y - p.T);
                int x2p = Math.Min(wImg, r.X + r.Width + p.R);
                int y2p = Math.Min(hImg, r.Y + r.Height + p.B);

                Rect roi = new Rect(x1p, y1p, x2p - x1p, y2p - y1p);

                // Gọi bộ phân tích OCR của VisionEngine để đọc số trong vùng chọn (ROI)
                int val = vision.ExtractNumericalMetrics(screenshot, roi);
                results[label] = val;
            }

            int gold = results.GetValueOrDefault("Gold", 0);
            int elixir = results.GetValueOrDefault("Elixir", 0);
            int darkElixir = results.GetValueOrDefault("Dark Elixir", 0);

            Console.WriteLine($"[SCOUT] Resources: Gold={gold:N0}, Elixir={elixir:N0}, Dark={darkElixir:N0}");
            return (gold, elixir, darkElixir);
        }

        // Tọa độ vùng quét tài nguyên Làng chính (Home Base) chuẩn độ phân giải 1600x900px ở góc trên cùng bên phải màn hình
        private static readonly Dictionary<string, Rect> HomeCoords = new()
        {
            { "Gold", new Rect(1310, 30, 200, 36) },       // Vùng chứa thông số Vàng của Làng chính
            { "Elixir", new Rect(1310, 115, 200, 36) },     // Vùng chứa thông số Dầu hồng của Làng chính
            { "Dark Elixir", new Rect(1310, 200, 200, 32) } // Vùng chứa thông số Dầu đen của Làng chính
        };

        /// <summary>
        /// Thực hiện chụp ảnh màn hình giả lập và trích xuất chỉ số tài nguyên hiện có ở Làng chính (Home Base).
        /// Dùng để phục vụ các quyết định nâng cấp tường hoặc công trình.
        /// </summary>
        /// <param name="adb">Đối tượng ADBHelper.</param>
        /// <param name="vision">Đối tượng VisionEngine.</param>
        /// <returns>Bộ ba số nguyên biểu thị lượng tài nguyên làng chính.</returns>
        public static (int Gold, int Elixir, int DarkElixir) ExtractHomeResources(ADBHelper adb, VisionEngine vision)
        {
            Console.WriteLine("[SCOUT] Reading home resources...");
            using Mat? screenshot = adb.TakeScreenshot();
            if (screenshot == null || screenshot.Empty())
            {
                Console.WriteLine("[SCOUT ERROR] Screenshot unavailable.");
                return (0, 0, 0);
            }

            var results = new Dictionary<string, int>();

            foreach (var kvp in HomeCoords)
            {
                string label = kvp.Key;
                Rect roi = kvp.Value;

                // Sử dụng RGB thresholding (useRgbThresh: true) để loại bỏ nền vàng/hồng sáng màu của thanh chứa tài nguyên Làng chính,
                // chỉ giữ lại phần màu chữ số trắng/đen để chạy OCR chính xác hơn.
                int val = vision.ExtractNumericalMetrics(screenshot, roi, isOffline: false, useRgbThresh: true);
                results[label] = val;
            }

            int gold = results.GetValueOrDefault("Gold", 0);
            int elixir = results.GetValueOrDefault("Elixir", 0);
            int darkElixir = results.GetValueOrDefault("Dark Elixir", 0);

            Console.WriteLine($"[SCOUT] Home resources: Gold={gold:N0}, Elixir={elixir:N0}, Dark={darkElixir:N0}");
            return (gold, elixir, darkElixir);
        }
    }
}
