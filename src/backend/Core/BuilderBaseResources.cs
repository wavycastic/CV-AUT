using System;
using System.Collections.Generic;
using System.Threading;
using OpenCvSharp;
using Point = OpenCvSharp.Point;

namespace CvAut
{
    /// <summary>
    /// Thu hoạch tài nguyên Builder Base / Làng đêm.
    /// Port theo hướng C# native từ MBR CollectBuilderBase: tìm dấu hiệu collector có thể collect,
    /// bỏ qua storage đầy nếu phát hiện icon full, tap từng collector và thử nút Collect chung.
    /// Elixir Cart chỉ dùng template cart thật; các template claim_elixir_n* chỉ được dùng làm nút claim
    /// sau khi đã xác nhận cart, tránh tap nhầm vì chúng không phải hình cart.
    /// </summary>
    internal sealed class BuilderBaseResources
    {
        private readonly ADBHelper _adb;
        private readonly VisionEngine _vision;
        private readonly BuilderBaseNavigator _navigator;

        private const double CollectorThreshold = 0.62;
        private const double ElixirCartThreshold = 0.66;
        private const double FullStorageThreshold = 0.72;
        private const double CollectButtonThreshold = 0.66;
        private const int MinTapDistancePx = 70;

        private static readonly Rect MapRoi = Rect.FromLTRB(180, 80, 1420, 790);
        // MBR gốc tìm ElixirCart tại 470..620 x 90..190 trên base 860x732.
        // Scale ngang lên 1600 giữ vùng hẹp quanh khu vực xe cart; nới nhẹ y để chịu lệch layout.
        private static readonly Rect ElixirCartRoi = Rect.FromLTRB(850, 90, 1185, 330);
        private static readonly Rect CollectButtonRoi = Rect.FromLTRB(520, 450, 1080, 780);
        private static readonly Rect TopStorageRoi = Rect.FromLTRB(980, 0, 1600, 170);

        private static readonly string[] GoldCollectorTemplates =
        {
            @"resources\gold_collector",
            @"ui\gold_collector"
        };

        private static readonly string[] ElixirCollectorTemplates =
        {
            @"resources\elixir_collector",
            @"ui\elixir_collector"
        };

        private static readonly string[] FullGoldTemplates =
        {
            @"resources\full_gold_builder"
        };

        private static readonly string[] FullElixirTemplates =
        {
            @"resources\full_elixir_builder"
        };

        private static readonly string[] CollectButtonTemplates =
        {
            @"resources\collect",
            @"ui\collect"
        };

        private static readonly string[] ElixirCartCollectButtonTemplates =
        {
            @"resources\collect_elixir_cart",
            @"resources\collect_elixir",
            @"resources\collect",
            @"ui\collect",
            @"resources\claim_elixir_n1",
            @"resources\claim_elixir_n2",
            @"resources\claim_elixir_n3",
            @"resources\claim_elixir_n4",
            @"resources\claim_elixir_n5",
            @"resources\claim_elixir_n6",
            @"resources\claim_elixir_n7",
            @"resources\claim_elixir_n8"
        };

        private static readonly string[] ExactElixirCartTemplates =
        {
            @"resources\elixir_cart",
            @"resources\elixir_cart_filled",
            @"resources\elixircart",
            @"resources\elix_cart",
            @"resources\ElixCart",
            @"resources\ElixCart1",
            @"resources\ElixCart2",
            @"resources\builder_elixir_cart",
            @"ui\elixir_cart",
            @"ui\elixir_cart_filled",
            @"ui\builder_elixir_cart"
        };

        public BuilderBaseResources(ADBHelper adb, VisionEngine vision, BuilderBaseNavigator navigator)
        {
            _adb = adb;
            _vision = vision;
            _navigator = navigator;
        }

        public int Collect(CancellationToken token)
        {
            Console.WriteLine("[BB-CS] phase=collect_resources status=start");
            int collected = CollectSingleStage(isOttoVillage: false, token);

            // Chuyển sang Stage 2 (Làng Otto) để thu hoạch tiếp
            if (_navigator.SwitchToOttoVillage(token))
            {
                collected += CollectSingleStage(isOttoVillage: true, token);
                _navigator.SwitchToBuilderBaseStage1(token);
            }

            Console.WriteLine($"[BB-CS] phase=collect_resources status=success total_taps={collected}");
            return collected;
        }

        private int CollectSingleStage(bool isOttoVillage, CancellationToken token)
        {
            using Mat? screenshot = _adb.TakeScreenshot();
            if (screenshot == null || screenshot.Empty())
            {
                Console.WriteLine("[BB-CS WARNING] phase=collect_resources status=fail reason=screenshot_failed");
                return 0;
            }

            bool goldFull = AnyTemplateVisible(screenshot, FullGoldTemplates, FullStorageThreshold, TopStorageRoi, out string goldFullTemplate, out double goldFullScore);
            bool elixirFull = AnyTemplateVisible(screenshot, FullElixirTemplates, FullStorageThreshold, TopStorageRoi, out string elixirFullTemplate, out double elixirFullScore);

            if (goldFull)
            {
                Console.WriteLine($"[BB-CS] phase=collect_resources status=skip item=gold reason=storage_full template=\"{goldFullTemplate}\" score={goldFullScore:F2}");
            }

            if (elixirFull)
            {
                Console.WriteLine($"[BB-CS] phase=collect_resources status=skip item=elixir reason=storage_full template=\"{elixirFullTemplate}\" score={elixirFullScore:F2}");
            }

            var tapPoints = new List<Point>();
            if (!goldFull)
            {
                AddVisibleCollectors(screenshot, GoldCollectorTemplates, "gold", tapPoints);
            }

            if (!elixirFull)
            {
                AddVisibleCollectors(screenshot, ElixirCollectorTemplates, "elixir", tapPoints);
            }

            int collected = 0;
            foreach (Point point in tapPoints)
            {
                if (token.IsCancellationRequested) break;

                Console.WriteLine($"[BB-CS] phase=collect_resources status=pending action=tap_collector x={point.X} y={point.Y}");
                _adb.Tap(point.X, point.Y);
                if (Sleep(450, token)) break;
                collected++;
            }

            // Một số giao diện mở panel có nút Collect riêng. Thử bấm 1 lần nếu thấy.
            collected += TapCollectButtonIfVisible(token);

            if (!isOttoVillage && !elixirFull)
            {
                collected += CollectElixirCartIfAvailable(token);
            }

            Console.WriteLine($"[BB-CS] phase=collect_resources_stage status=success is_otto={isOttoVillage} taps={collected}");
            return collected;
        }

        private void AddVisibleCollectors(Mat screenshot, string[] templates, string resourceName, List<Point> tapPoints)
        {
            foreach (string template in templates)
            {
                Point? center = _vision.FindElement(screenshot, template, CollectorThreshold, MapRoi, out double score);
                if (center == null) continue;

                if (IsNearExisting(tapPoints, center.Value))
                {
                    Console.WriteLine($"[BB-CS] phase=collect_resources status=skip item={resourceName} reason=duplicate template=\"{template}\" score={score:F2}");
                    continue;
                }

                tapPoints.Add(center.Value);
                Console.WriteLine($"[BB-CS] phase=collect_resources status=found item={resourceName} template=\"{template}\" score={score:F2} center=({center.Value.X},{center.Value.Y})");
            }
        }

        private int TapCollectButtonIfVisible(CancellationToken token)
        {
            using Mat? screenshot = _adb.TakeScreenshot();
            if (screenshot == null || screenshot.Empty()) return 0;

            foreach (string template in CollectButtonTemplates)
            {
                Point? center = _vision.FindElement(screenshot, template, CollectButtonThreshold, CollectButtonRoi, out double score);
                if (center == null) continue;

                Console.WriteLine($"[BB-CS] phase=collect_resources status=pending action=tap_collect_button template=\"{template}\" score={score:F2} center=({center.Value.X},{center.Value.Y})");
                _adb.Tap(center.Value.X, center.Value.Y);
                Sleep(700, token);
                return 1;
            }

            return 0;
        }

        private int CollectElixirCartIfAvailable(CancellationToken token)
        {
            using Mat? screenshot = _adb.TakeScreenshot();
            if (screenshot == null || screenshot.Empty()) return 0;

            foreach (string template in ExactElixirCartTemplates)
            {
                Point? center = _vision.FindElement(screenshot, template, ElixirCartThreshold, ElixirCartRoi, out double score);
                if (center == null) continue;

                Console.WriteLine($"[BB-CS] phase=collect_resources status=found item=elixir_cart template=\"{template}\" score={score:F2} center=({center.Value.X},{center.Value.Y})");
                // MBR click vào thân xe +16 px ở layout 860x732; scale 1600x900 ~ +25..30 px.
                _adb.Tap(center.Value.X, center.Value.Y + 28);
                if (Sleep(700, token)) return 1;
                return 1 + TapElixirCartCollectButtonIfVisible(token);
            }

            Console.WriteLine("[BB-CS] phase=collect_resources status=skip item=elixir_cart reason=exact_cart_template_not_found note=claim_elixir_templates_not_used_as_cart");
            return 0;
        }

        private int TapElixirCartCollectButtonIfVisible(CancellationToken token)
        {
            for (int attempt = 1; attempt <= 10 && !token.IsCancellationRequested; attempt++)
            {
                using Mat? screenshot = _adb.TakeScreenshot();
                if (screenshot == null || screenshot.Empty()) return 0;

                foreach (string template in ElixirCartCollectButtonTemplates)
                {
                    Point? center = _vision.FindElement(screenshot, template, CollectButtonThreshold, CollectButtonRoi, out double score);
                    if (center == null) continue;

                    Console.WriteLine($"[BB-CS] phase=collect_resources status=pending action=tap_elixir_cart_collect template=\"{template}\" score={score:F2} center=({center.Value.X},{center.Value.Y}) attempt={attempt}");
                    _adb.Tap(center.Value.X, center.Value.Y);
                    Sleep(1000, token);
                    return 1;
                }

                if (Sleep(250, token)) return 0;
            }

            Console.WriteLine("[BB-CS] phase=collect_resources status=warning item=elixir_cart reason=collect_button_not_found");
            return 0;
        }

        private bool AnyTemplateVisible(Mat screenshot, string[] templates, double threshold, Rect? roi, out string matchedTemplate, out double matchedScore)
        {
            matchedTemplate = string.Empty;
            matchedScore = 0;

            foreach (string template in templates)
            {
                Point? center = _vision.FindElement(screenshot, template, threshold, roi, out double score);
                if (center == null) continue;

                matchedTemplate = template;
                matchedScore = score;
                return true;
            }

            return false;
        }

        private static bool IsNearExisting(IEnumerable<Point> points, Point candidate)
        {
            foreach (Point point in points)
            {
                int dx = point.X - candidate.X;
                int dy = point.Y - candidate.Y;
                if (dx * dx + dy * dy <= MinTapDistancePx * MinTapDistancePx)
                {
                    return true;
                }
            }

            return false;
        }

        private static bool Sleep(int milliseconds, CancellationToken token)
        {
            return token.WaitHandle.WaitOne(milliseconds);
        }
    }
}
