using System;
using System.Collections.Generic;
using OpenCvSharp;

namespace CvAut
{
    public static class IsTarget
    {
        // Tọa độ vùng quét tài nguyên chuẩn độ phân giải 1600x900px
        private static readonly Dictionary<string, Rect> Coords = new()
        {
            { "Gold", new Rect(55, 117, 196, 44) },        // w = 251-55 = 196, h = 161-117 = 44
            { "Elixir", new Rect(60, 167, 201, 41) },      // w = 261-60 = 201, h = 208-167 = 41
            { "Dark Elixir", new Rect(73, 214, 110, 34) }  // w = 183-73 = 110, h = 248-214 = 34
        };

        // Lề cắt rộng bù trừ margin
        private static readonly Dictionary<string, Padding> Margins = new()
        {
            { "Gold", new Padding { L = 60, R = 15, T = 5, B = 5 } },
            { "Elixir", new Padding { L = 15, R = 15, T = 5, B = 5 } },
            { "Dark Elixir", new Padding { L = 15, R = 15, T = 5, B = 5 } }
        };

        private struct Padding
        {
            public int L { get; set; }
            public int R { get; set; }
            public int T { get; set; }
            public int B { get; set; }
        }

        public static (int Gold, int Elixir, int DarkElixir) ExtractResources(ADBHelper adb, VisionEngine vision)
        {
            Console.WriteLine("[SCOUT-CS] Đang chụp màn hình giả lập để quét tài nguyên...");
            using Mat? screenshot = adb.TakeScreenshot();
            if (screenshot == null || screenshot.Empty())
            {
                Console.WriteLine("[SCOUT-CS ERROR] Không thể chụp ảnh màn hình hoặc ảnh trống.");
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

                // Tính toán tọa độ cắt bù trừ an toàn chống lỗi tràn biên
                int x1p = Math.Max(0, r.X - p.L);
                int y1p = Math.Max(0, r.Y - p.T);
                int x2p = Math.Min(wImg, r.X + r.Width + p.R);
                int y2p = Math.Min(hImg, r.Y + r.Height + p.B);

                Rect roi = new Rect(x1p, y1p, x2p - x1p, y2p - y1p);

                int val = vision.ExtractNumericalMetrics(screenshot, roi);
                results[label] = val;
            }

            int gold = results.GetValueOrDefault("Gold", 0);
            int elixir = results.GetValueOrDefault("Elixir", 0);
            int darkElixir = results.GetValueOrDefault("Dark Elixir", 0);

            Console.WriteLine($"[SCOUT-CS] Kết quả quét -> Vàng: {gold:N0} | Dầu hồng: {elixir:N0} | Dầu đen: {darkElixir:N0}");
            return (gold, elixir, darkElixir);
        }

        // Tọa độ vùng quét tài nguyên Làng chính (Home Base) chuẩn độ phân giải 1600x900px
        private static readonly Dictionary<string, Rect> HomeCoords = new()
        {
            { "Gold", new Rect(1310, 30, 200, 36) },
            { "Elixir", new Rect(1310, 115, 200, 36) },
            { "Dark Elixir", new Rect(1310, 200, 200, 32) }
        };

        public static (int Gold, int Elixir, int DarkElixir) ExtractHomeResources(ADBHelper adb, VisionEngine vision)
        {
            Console.WriteLine("[SCOUT-CS] Đang chụp màn hình giả lập để quét tài nguyên LÀNG CHÍNH...");
            using Mat? screenshot = adb.TakeScreenshot();
            if (screenshot == null || screenshot.Empty())
            {
                Console.WriteLine("[SCOUT-CS ERROR] Không thể chụp ảnh màn hình hoặc ảnh trống.");
                return (0, 0, 0);
            }

            var results = new Dictionary<string, int>();

            foreach (var kvp in HomeCoords)
            {
                string label = kvp.Key;
                Rect roi = kvp.Value;

                // Sử dụng RGB thresholding (useRgbThresh: true) để loại bỏ nền vàng/hồng sáng màu của thanh chứa tài nguyên Làng chính
                int val = vision.ExtractNumericalMetrics(screenshot, roi, isOffline: false, useRgbThresh: true);
                results[label] = val;
            }

            int gold = results.GetValueOrDefault("Gold", 0);
            int elixir = results.GetValueOrDefault("Elixir", 0);
            int darkElixir = results.GetValueOrDefault("Dark Elixir", 0);

            Console.WriteLine($"[SCOUT-CS] Kết quả quét Làng chính -> Vàng: {gold:N0} | Dầu hồng: {elixir:N0} | Dầu đen: {darkElixir:N0}");
            return (gold, elixir, darkElixir);
        }
    }
}
