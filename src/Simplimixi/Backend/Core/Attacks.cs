using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using OpenCvSharp;
using Point = OpenCvSharp.Point;

namespace CvAut
{
    /// <summary>
    /// Thông tin tọa độ thả của một vị tướng (Hero) trong game.
    /// </summary>
    internal class HeroInfo
    {
        public string Name { get; set; } = "";
        public Point Coord { get; set; }
    }

    /// <summary>
    /// Phân hệ Tấn công (Attacks):
    /// - Quản lý tọa độ rải quân mặc định theo cánh trái/phải đối xứng.
    /// - Dò tìm các thẻ quân, phép, tướng hiện có trên giao diện chiến trận dưới đáy màn hình.
    /// - Thực hiện kịch bản rải quân (tạp biến ngẫu nhiên chống chống-bot), rải phép đóng băng/cuồng nộ.
    /// - Quét số lượng lính còn dư để tiến hành rải bù.
    /// </summary>
    internal class Attacks
    {
        private readonly ADBHelper _adb;
        private readonly VisionEngine _vision;
        private readonly Random _rand = new();

        private const int ScreenWidth = 1600;
        private const double MatchThreshold = 0.52;
        private const int TroopTabSelectDelayMs = 160;
        private const int SpellCastDelayMs = 650;
        private const int FreezeCastDelayMs = 850;
        private const int SpellPhaseDelayMs = 1200;
        private const int HeroAbilityDelayMs = 2500;
        private const int DuplicateTabDistancePx = 45;
        private const int RemainingTroopSettleDelayMs = 540;
        private const int MaxRemainingDeployPasses = 1;
        private const int SpellTabMinSeparationPx = 45;
        private const double SpellTabAmbiguityScoreDelta = 0.06;
        private static readonly bool VerboseTemplateLogs = false;

        // Vùng ROI của thanh thả quân dưới đáy màn hình (chứa thẻ lính, phép, tướng)
        private static readonly Rect DeployBarRoi = Rect.FromLTRB(70, 720, 1180, 890);

        // Tọa độ rải quân mẫu của Rồng ở cánh TRÁI (Left-side)
        private static readonly List<Point> DragonL = new()
        {
            new(170, 384), new(214, 348), new(246, 327), new(270, 306), new(305, 285), new(345, 255),
            new(368, 238), new(396, 216), new(417, 201), new(442, 182), new(487, 152), new(535, 121),
            new(640, 35),  new(442, 182)
        };

        // Tọa độ rải Balloon ở cánh TRÁI
        private static readonly List<Point> BalloonL = new()
        {
            new(170, 384), new(214, 348), new(246, 327), new(270, 306), new(305, 285), new(345, 255),
            new(368, 238), new(396, 216), new(417, 201), new(444, 183), new(486, 154), new(534, 122),
            new(345, 255), new(444, 183), new(368, 238), new(246, 327), new(417, 201)
        };

        // Tọa độ rải quân mẫu của Rồng ở cánh PHẢI (Right-side)
        private static readonly List<Point> DragonR = new()
        {
            new(1344, 346), new(1272, 295), new(1234, 261), new(1191, 229), new(1150, 200), new(1116, 173),
            new(1074, 138), new(1042, 114), new(1000, 91), new(946, 47), new(904, 18), new(1033, 108),
            new(1091, 152), new(1109, 172)
        };

        // Tọa độ rải quân Rồng dự phòng ở cánh TRÁI (dùng khi rải bù lính bị kẹt)
        private static readonly List<Point> DragonFallbackL = new()
        {
            new(145, 420), new(171, 384), new(214, 348), new(246, 327), new(270, 306), new(305, 285),
            new(345, 255), new(396, 216), new(442, 182), new(487, 152), new(535, 121), new(610, 66),
            new(185, 500), new(238, 562), new(304, 616), new(374, 670)
        };

        // Tọa độ rải Balloon dự phòng ở cánh TRÁI
        private static readonly List<Point> BalloonFallbackL = new()
        {
            new(145, 420), new(170, 384), new(214, 348), new(246, 327), new(270, 306), new(305, 285),
            new(345, 255), new(368, 238), new(396, 216), new(417, 201), new(444, 183), new(486, 154),
            new(534, 122), new(185, 500), new(238, 562), new(304, 616), new(374, 670)
        };

        // Tọa độ rải Balloon ở cánh PHẢI
        private static readonly List<Point> BalloonR = new()
        {
            new(1344, 346), new(1272, 295), new(1234, 261), new(1191, 229), new(1150, 200), new(1116, 173),
            new(1074, 138), new(1042, 114), new(1000, 91), new(946, 47), new(904, 18), new(1033, 108),
            new(1091, 152), new(1109, 172), new(1207, 209), new(1296, 273), new(1311, 256)
        };

        // Tọa độ thả phép Cuồng nộ (Rage Spell) cánh TRÁI
        private static readonly List<Point> RageL = new()
        {
            new(549, 353), new(674, 247), new(797, 317), new(690, 439), new(777, 403)
        };

        // Tọa độ thả phép Đóng băng (Freeze Spell) cánh TRÁI
        private static readonly List<Point> FreezeL = new()
        {
            new(614, 371), new(769, 276), new(770, 363), new(704, 494), new(798, 405), new(874, 405)
        };

        // Tọa độ thả Tướng (Heroes) ở cánh TRÁI
        private static readonly List<HeroInfo> HeroL = new()
        {
            new() { Name = "siege_machine", Coord = new Point(364, 236) },
            new() { Name = "queen",         Coord = new Point(364, 236) },
            new() { Name = "bk",            Coord = new Point(513, 135) },
            new() { Name = "warden",        Coord = new Point(445, 191) },
            new() { Name = "prince",        Coord = new Point(445, 191) },
            new() { Name = "rc",            Coord = new Point(426, 204) }
        };

        private Dictionary<string, List<Point>> _deployCoords = new();
        private Dictionary<string, List<Point>> _fallbackDeployCoords = new();
        private List<HeroInfo> _heroCoords = new();
        private Dictionary<string, Point> _tabs = new();
        private readonly Dictionary<string, Point> _heroAbilityTabs = new(StringComparer.OrdinalIgnoreCase);
        private string _side = "left"; // Cánh tấn công ("left" hoặc "right")
        private readonly HashSet<string> _requiredTabs = new(StringComparer.OrdinalIgnoreCase);

        private static bool IsStopRequested(CancellationToken token) => token.IsCancellationRequested;

        private static bool InterruptibleSleep(int milliseconds, CancellationToken token)
        {
            return token.WaitHandle.WaitOne(milliseconds);
        }

        /// <summary>
        /// Khởi tạo đối tượng Attacks điều khiển trận đánh.
        /// </summary>
        public Attacks(ADBHelper adb, VisionEngine vision)
        {
            _adb = adb;
            _vision = vision;
        }

        /// <summary>
        /// Thêm độ lệch ngẫu nhiên (Jitter) vào tọa độ chạm ban đầu.
        /// Việc này giúp tạo mô phỏng thao tác của con người, tránh các thuật toán phát hiện bot (Anti-Cheat) của nhà phát hành game.
        /// </summary>
        /// <param name="pt">Tọa độ đích ban đầu.</param>
        /// <returns>Tọa độ mới đã được thêm độ lệch ngẫu nhiên nhỏ.</returns>
        private Point JitterCoord(Point pt)
        {
            int dx, dy;
            if (_side == "left")
            {
                dx = _rand.Next(0, 40);       // Độ lệch X ngẫu nhiên [0, 39]
                dy = _rand.Next(-27, 1);      // Độ lệch Y ngẫu nhiên [-27, 0]
            }
            else if (_side == "right")
            {
                dx = _rand.Next(-44, 1);      // [-44, 0]
                dy = _rand.Next(-33, 1);      // [-33, 0]
            }
            else
            {
                dx = _rand.Next(-10, 11);     // [-10, 10]
                dy = _rand.Next(-10, 11);     // [-10, 10]
            }
            return new Point(pt.X + dx, pt.Y + dy);
        }

        /// <summary>
        /// Khởi tạo các mẫu rải quân. Nếu chọn tấn công cánh PHẢI, tự động quy đổi đối xứng tọa độ (mirror) theo chiều ngang của màn hình 1600px.
        /// </summary>
        private void InitializePatterns()
        {
            _deployCoords.Clear();
            _fallbackDeployCoords.Clear();
            _heroCoords = new List<HeroInfo>();

            if (_side == "left")
            {
                _deployCoords["dragon"] = DragonL;
                _deployCoords["e_drag"] = DragonL.GetRange(2, 10);
                _deployCoords["balloon"] = BalloonL;
                _deployCoords["rage"] = RageL;
                _deployCoords["freeze"] = FreezeL;
                _deployCoords["siege_machine"] = new List<Point> { HeroL[0].Coord };
                _deployCoords["azure_dragon"] = new List<Point> { HeroL[2].Coord };
                _deployCoords["ice_minion"] = DragonL.GetRange(2, 10);
                _deployCoords["ice_golem"] = DragonL.GetRange(4, 5);
                _fallbackDeployCoords["dragon"] = DragonFallbackL;
                _fallbackDeployCoords["e_drag"] = DragonFallbackL;
                _fallbackDeployCoords["balloon"] = BalloonFallbackL;

                _heroCoords = HeroL.ConvertAll(h => new HeroInfo { Name = h.Name, Coord = h.Coord });
            }
            else
            {
                // Quy đổi đối xứng tọa độ từ trái qua phải (1600 - x)
                Converter<Point, Point> mirror = pt => new Point(ScreenWidth - 1 - pt.X, pt.Y);

                _deployCoords["dragon"] = DragonR;
                _deployCoords["e_drag"] = DragonR.GetRange(2, 10);
                _deployCoords["balloon"] = BalloonR;
                _deployCoords["rage"] = RageL.ConvertAll(mirror);
                _deployCoords["freeze"] = FreezeL.ConvertAll(mirror);
                _deployCoords["siege_machine"] = new List<Point> { mirror(HeroL[0].Coord) };
                _deployCoords["azure_dragon"] = new List<Point> { mirror(HeroL[2].Coord) };
                _deployCoords["ice_minion"] = DragonR.GetRange(2, 10);
                _deployCoords["ice_golem"] = DragonR.GetRange(4, 5);
                _fallbackDeployCoords["dragon"] = DragonFallbackL.ConvertAll(mirror);
                _fallbackDeployCoords["e_drag"] = DragonFallbackL.ConvertAll(mirror);
                _fallbackDeployCoords["balloon"] = BalloonFallbackL.ConvertAll(mirror);

                _heroCoords = HeroL.ConvertAll(h => new HeroInfo { Name = h.Name, Coord = mirror(h.Coord) });
            }
        }

        /// <summary>
        /// Thực hiện quét và dò tìm vị trí tọa độ của tất cả các thẻ quân đang có trên DeployBar ở đáy màn hình.
        /// Lưu tọa độ vào Dictionary _tabs để bot có thể bấm chọn lính/phép chính xác.
        /// </summary>
        public void UpdateTabs()
        {
            Console.WriteLine("[ATTACK-CS] phase=scan_bar status=start");
            using Mat? screenshot = _adb.TakeScreenshot();
            if (screenshot == null || screenshot.Empty()) return;

            Dictionary<string, Point> previousTabs = new(_tabs, StringComparer.OrdinalIgnoreCase);
            _tabs.Clear();
            var categories = new Dictionary<string, string>
            {
                { "dragon", "troops/dragon" },
                { "e_drag", "troops/E_Drag" },
                { "balloon", "troops/balloon" },
                { "event_goblin", "troops/event_goblin" },
                { "nguoimay", "troops/nguoimay" },
                { "phuthuycuoichoi", "troops/phuthuycuoichoi" },
                { "azure_dragon", "troops/azure_dragon" },
                { "ice_minion", "troops/ice_minion" },
                { "ice_golem", "troops/ice_golem" },
                { "rage", "spells/rage" },
                { "freeze", "spells/freeze" },
                { "queen", "heroes/queen" },
                { "bk", "heroes/bk" },
                { "warden", "heroes/warden" },
                { "prince", "heroes/prince" },
                { "rc", "heroes/rc" }
            };

            foreach (var kvp in categories)
            {
                bool isSpell = kvp.Key == "rage" || kvp.Key == "freeze";
                double threshold = isSpell ? 0.45 : MatchThreshold;
                Point? coord = _vision.FindElement(screenshot, kvp.Value, threshold, DeployBarRoi, out double score);

                if (coord == null)
                {
                    coord = FindDeploymentTabFallback(screenshot, kvp.Key, kvp.Value, threshold, out score);
                }

                if (isSpell && VerboseTemplateLogs)
                {
                    Console.WriteLine($"[ATTACK-CS] phase=scan_bar status=pending action=scan item={kvp.Key} details=\"primary_scan_completed\"");
                }

                // Dò tìm dự phòng cho phép Đóng băng (Freeze) nếu tìm kiếm chính thất bại
                if (coord == null && kvp.Key == "freeze")
                {
                    double fallbackThreshold = 0.40;
                    Rect widerRoi = Rect.FromLTRB(0, 680, screenshot.Width, screenshot.Height);
                    string[] fallbackTemplates =
                    {
                        kvp.Value,
                        "Smart_Auto_train/Spells/freeze",
                        "Smart_Auto_train/to_train/freeze"
                    };

                    foreach (string fallbackTemplate in fallbackTemplates)
                    {
                        coord = _vision.FindElement(screenshot, fallbackTemplate, fallbackThreshold, widerRoi, out double fallbackScore);
                        if (VerboseTemplateLogs)
                        {
                            Console.WriteLine("[ATTACK-CS] phase=scan_bar status=pending action=scan item=freeze details=\"fallback_scan_completed\"");
                        }
                        score = fallbackScore;
                        if (coord != null)
                        {
                            break;
                        }
                    }
                }

                if (coord != null)
                {
                    // Lọc trùng lặp để tránh gán nhầm sang thẻ bên cạnh do khoảng cách quá gần
                    if (IsDuplicateTab(kvp.Key, coord.Value, out string existingName))
                    {
                        Console.WriteLine($"[ATTACK-CS WARNING] phase=scan_bar status=pending action=scan item={kvp.Key} reason=duplicate existing={existingName}");
                        continue;
                    }

                    _tabs[kvp.Key] = coord.Value;
                    Console.WriteLine($"[ATTACK-CS] phase=scan_bar status=pending action=scan item={kvp.Key} verdict=ready");
                }

                if (coord == null && _requiredTabs.Contains(kvp.Key))
                {
                    Console.WriteLine($"[ATTACK-CS WARNING] phase=scan_bar status=missing action=scan item={kvp.Key} reason=required_tab_not_found");
                }
            }

            // Dò tìm xe công thành (Siege Machine) - có 2 trường hợp: Có quân (siege_with_troops) hoặc rỗng (empty_siege)
            Rect widerDeployBarRoi = Rect.FromLTRB(0, 650, screenshot.Width, screenshot.Height);
            Point? swt = _vision.FindElement(screenshot, "troops/siege_with_troops", MatchThreshold, DeployBarRoi, out double swtScore)
                ?? _vision.FindElement(screenshot, "troops/icon_siege", 0.42, widerDeployBarRoi, out swtScore)
                ?? _vision.FindElement(screenshot, "troops/siege_with_troops", 0.42, widerDeployBarRoi, out swtScore);
            Point? es = _vision.FindElement(screenshot, "troops/empty_siege", MatchThreshold, DeployBarRoi, out double esScore)
                ?? _vision.FindElement(screenshot, "troops/empty_siege", 0.42, widerDeployBarRoi, out esScore);
            if (VerboseTemplateLogs)
            {
                Console.WriteLine("[ATTACK-CS] phase=scan_bar status=pending action=scan item=siege details=\"scan_completed\"");
                Console.WriteLine("[ATTACK-CS] phase=scan_bar status=pending action=scan item=siege details=\"empty_scan_completed\"");
            }
            if (swt != null)
            {
                _tabs["siege_machine"] = swt.Value;
                Console.WriteLine("[ATTACK-CS] phase=scan_bar status=pending action=scan item=siege verdict=ready");
            }
            else if (es != null)
            {
                _tabs["siege_machine"] = es.Value;
                Console.WriteLine("[ATTACK-CS] phase=scan_bar status=pending action=scan item=siege verdict=empty");
            }

            foreach (string required in _requiredTabs)
            {
                if (!_tabs.ContainsKey(required))
                {
                    Console.WriteLine($"[ATTACK-CS WARNING] phase=scan_bar status=missing item={required} reason=required_tab_not_found");
                }
            }
        }

        private Point? FindDeploymentTabFallback(Mat screenshot, string key, string primaryTemplate, double primaryThreshold, out double score)
        {
            score = 0;
            Rect widerDeployBarRoi = Rect.FromLTRB(0, 650, screenshot.Width, screenshot.Height);
            double fallbackThreshold = Math.Min(primaryThreshold, 0.42);
            string[] templates = key switch
            {
                "dragon" => new[] { primaryTemplate, "troops/icon_dragon", "troops/dragon", "Smart_Auto_train/Army Troops/dragon", "Smart_Auto_train/to_train/dragon" },
                "e_drag" => new[] { primaryTemplate, "troops/e_drag", "troops/electro_dragon", "troops/E_Drag", "Smart_Auto_train/Army Troops/electro_dragon", "Smart_Auto_train/to_train/electro_dragon" },
                _ => Array.Empty<string>()
            };

            foreach (string template in templates)
            {
                Point? coord = _vision.FindElement(screenshot, template, fallbackThreshold, widerDeployBarRoi, out double fallbackScore);
                score = fallbackScore;
                if (coord != null)
                {
                    return coord;
                }
            }

            return null;
        }

        private bool AreTabsTooClose(Point a, Point b)
        {
            return Math.Abs(a.X - b.X) <= SpellTabMinSeparationPx && Math.Abs(a.Y - b.Y) <= SpellTabMinSeparationPx;
        }

        /// <summary>
        /// Kiểm tra xem tọa độ thẻ quân mới quét được có bị trùng lặp với tọa độ thẻ đã ghi nhận hay không.
        /// </summary>
        private bool IsDuplicateTab(string candidateName, Point candidate, out string existingName)
        {
            foreach (var kvp in _tabs)
            {
                int dx = kvp.Value.X - candidate.X;
                int dy = kvp.Value.Y - candidate.Y;
                if ((dx * dx) + (dy * dy) <= DuplicateTabDistancePx * DuplicateTabDistancePx)
                {
                    existingName = kvp.Key;
                    return !ShouldKeepNearbyTroopTab(candidateName, existingName);
                }
            }

            existingName = "";
            return false;
        }

        private static bool ShouldKeepNearbyTroopTab(string candidateName, string existingName)
        {
            bool candidateMain = candidateName.Equals("dragon", StringComparison.OrdinalIgnoreCase)
                || candidateName.Equals("e_drag", StringComparison.OrdinalIgnoreCase);
            bool existingMain = existingName.Equals("dragon", StringComparison.OrdinalIgnoreCase)
                || existingName.Equals("e_drag", StringComparison.OrdinalIgnoreCase);

            return candidateMain && existingMain && !candidateName.Equals(existingName, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Bấm chọn thẻ và thả nhanh một số lượng quân chỉ định (như goblin sự kiện) theo tọa độ rải rác nhanh.
        /// </summary>
        private void DeployOptionalQuickDrop(string troopKey, int limit = 10, CancellationToken token = default)
        {
            if (IsStopRequested(token)) return;
            if (!_tabs.TryGetValue(troopKey, out Point troopTab))
            {
                return;
            }

            if (!_deployCoords.TryGetValue("dragon", out List<Point>? dragonCoords) || dragonCoords.Count == 0)
            {
                Console.WriteLine($"[ATTACK-CS WARNING] phase=deploy status=skip item={troopKey} reason=quick_drop_unavailable");
                return;
            }

            _adb.Tap(troopTab.X, troopTab.Y);
            if (InterruptibleSleep(TroopTabSelectDelayMs, token)) return;

            int tapLimit = Math.Max(0, limit);
            var quickTaps = new List<Point>(tapLimit);
            for (int i = 0; i < tapLimit; i++)
            {
                quickTaps.Add(JitterCoord(dragonCoords[i % dragonCoords.Count]));
            }

            Console.WriteLine($"[ATTACK-CS] phase=deploy status=start item={troopKey} action=quick_deploy count={tapLimit}");
            _adb.TapSequenceSafeFast(quickTaps, batchSize: 5, batchDelayMs: 60, token);
        }

        private void DeployIfPresent(string troopKey, CancellationToken token = default)
        {
            if (_tabs.ContainsKey(troopKey))
            {
                DeployTroops(troopKey, token);
            }
        }

        private void DeployQuickIfPresent(string troopKey, int limit = 10, CancellationToken token = default)
        {
            if (_tabs.ContainsKey(troopKey))
            {
                DeployOptionalQuickDrop(troopKey, limit, token);
            }
        }

        /// <summary>
        /// Thả một loại quân chỉ định. Bấm chọn thẻ quân và nhấp thả một chuỗi tọa độ (TapSequence).
        /// </summary>
        public void DeployTroops(string troopKey, CancellationToken token = default)
        {
            if (IsStopRequested(token)) return;
            string key = troopKey.ToLower();
            if (!_tabs.TryGetValue(key, out Point tab))
            {
                Console.WriteLine($"[ATTACK-CS WARNING] phase=deploy status=skip item={troopKey} reason=tab_not_found");
                return;
            }

            if (!_deployCoords.TryGetValue(key, out List<Point>? coords) || coords.Count == 0)
            {
                Console.WriteLine($"[ATTACK-CS WARNING] phase=deploy status=skip item={troopKey} reason=pattern_unavailable");
                return;
            }

            Console.WriteLine($"[ATTACK-CS] phase=deploy status=start item={troopKey} tab=({tab.X},{tab.Y}) tap_count={coords.Count}");
            Stopwatch sw = Stopwatch.StartNew();
            _adb.Tap(tab.X, tab.Y);
            if (InterruptibleSleep(TroopTabSelectDelayMs, token)) return;

            var taps = new List<Point>(coords.Count);
            foreach (var pt in coords)
            {
                taps.Add(JitterCoord(pt));
            }

            _adb.TapSequenceSafeFast(taps, batchSize: 5, batchDelayMs: 60, token);

            sw.Stop();
            Console.WriteLine($"[ATTACK-CS] phase=deploy status=success item={troopKey} tab=({tab.X},{tab.Y}) tap_count={taps.Count} duration={sw.ElapsedMilliseconds}ms");
        }

        /// <summary>
        /// Đảm bảo quân lính được thả hoàn toàn (rải bù nhanh nếu đọc thấy số lượng lính còn thừa trên giao diện thẻ).
        /// Chỉ chạy một lượt để ưu tiên tốc độ trong attack flow.
        /// </summary>
        public void EnsureTroopFullyDeployed(string troopKey)
        {
            string key = troopKey.ToLower();
            if (!_tabs.TryGetValue(key, out Point tab))
            {
                Console.WriteLine($"[ATTACK-CS WARNING] phase=validate_remaining status=skip item={troopKey} reason=tab_not_found");
                return;
            }

            if (!_fallbackDeployCoords.TryGetValue(key, out List<Point>? fallbackCoords) || fallbackCoords.Count == 0)
            {
                if (!_deployCoords.TryGetValue(key, out fallbackCoords) || fallbackCoords.Count == 0)
                {
                    Console.WriteLine($"[ATTACK-CS WARNING] phase=validate_remaining status=skip item={troopKey} reason=pattern_unavailable");
                    return;
                }
            }

            for (int pass = 1; pass <= MaxRemainingDeployPasses; pass++)
            {
                Thread.Sleep(RemainingTroopSettleDelayMs);
                int remaining = ReadRemainingTroopCount(key, out double confidence);
                if (remaining < 0)
                {
                    int fallbackTapCount = Math.Min(4, fallbackCoords.Count);
                    Console.WriteLine($"[ATTACK-CS WARNING] phase=validate_remaining status=fallback item={troopKey} reason=count_unavailable fallback_tap_count={fallbackTapCount}");
                    if (fallbackTapCount == 0)
                    {
                        return;
                    }

                    _adb.Tap(tab.X, tab.Y);
                    Thread.Sleep(TroopTabSelectDelayMs);
                    var fallbackTaps = new List<Point>(fallbackTapCount);
                    int fallbackStartOffset = ((pass - 1) * 4) % fallbackCoords.Count;
                    for (int i = 0; i < fallbackTapCount; i++)
                    {
                        fallbackTaps.Add(JitterCoord(fallbackCoords[(fallbackStartOffset + i) % fallbackCoords.Count]));
                    }

                    _adb.TapSequence(fallbackTaps);
                    return;
                }

                if (remaining == 0)
                {
                    Console.WriteLine($"[ATTACK-CS] phase=validate_remaining status=success item={troopKey} details=\"fully_deployed\"");
                    return;
                }

                Console.WriteLine($"[ATTACK-CS WARNING] phase=validate_remaining status=fallback item={troopKey} remaining={remaining} confidence={confidence:F2}");
                _adb.Tap(tab.X, tab.Y);
                Thread.Sleep(TroopTabSelectDelayMs);

                int tapCount = Math.Min(remaining + 2, fallbackCoords.Count);
                var taps = new List<Point>(tapCount);
                int startOffset = ((pass - 1) * 5) % fallbackCoords.Count;
                for (int i = 0; i < tapCount; i++)
                {
                    taps.Add(JitterCoord(fallbackCoords[(startOffset + i) % fallbackCoords.Count]));
                }

                _adb.TapSequence(taps);
            }


        }

        /// <summary>
        /// Thực hiện đọc số lượng lính còn thừa hiển thị ở góc trên cùng bên phải của thẻ quân.
        /// Chạy thử 3 vùng ROI khác nhau để tìm vùng đọc đạt độ tin cậy cao nhất.
        /// </summary>
        private int ReadRemainingTroopCount(string troopKey, out double confidence)
        {
            confidence = 0;
            if (!_tabs.TryGetValue(troopKey, out Point tab))
            {
                return -1;
            }

            using Mat? screenshot = _adb.TakeScreenshot();
            if (screenshot == null || screenshot.Empty())
            {
                return -1;
            }

            // Thử vùng ROI số 1
            Rect countRoi = Rect.FromLTRB(tab.X - 5, tab.Y - 94, tab.X + 72, tab.Y - 42);
            if (TryReadCountFromRoi(screenshot, countRoi, out int value, out confidence))
            {
                return value;
            }

            // Thử vùng ROI số 2 (chỉ lấy phần chữ số)
            Rect digitOnlyRoi = Rect.FromLTRB(tab.X + 22, tab.Y - 96, tab.X + 78, tab.Y - 50);
            if (TryReadCountFromRoi(screenshot, digitOnlyRoi, out value, out confidence))
            {
                return value;
            }

            // Thử vùng ROI số 3 (rộng hơn)
            Rect widerCountRoi = Rect.FromLTRB(tab.X - 20, tab.Y - 98, tab.X + 78, tab.Y - 40);
            if (TryReadCountFromRoi(screenshot, widerCountRoi, out value, out confidence))
            {
                return value;
            }

            return -1;
        }

        /// <summary>
        /// Chạy OCR trích xuất số lượng lính còn thừa trong ROI.
        /// </summary>
        private bool TryReadCountFromRoi(Mat screenshot, Rect roi, out int value, out double confidence)
        {
            // Thử dùng RGB thresholding trước để loại bỏ nền thẻ lính
            if (_vision.TryExtractNumericalMetrics(screenshot, roi, out value, out confidence, useRgbThresh: true) && IsPlausibleTroopCount(value, confidence))
            {
                return true;
            }

            // Thử dùng Threshold xám thông thường
            return _vision.TryExtractNumericalMetrics(screenshot, roi, out value, out confidence) && IsPlausibleTroopCount(value, confidence);
        }

        /// <summary>
        /// Kiểm tra xem giá trị số lính đọc được có khả thi hay không (độ tin cậy > 55% và số nằm trong khoảng 0-99).
        /// </summary>
        private static bool IsPlausibleTroopCount(int value, double confidence)
        {
            return confidence >= 0.55 && value >= 0 && value <= 99;
        }

        /// <summary>
        /// Thả toàn bộ Tướng (Heroes) hiện có trên DeployBar theo các vị trí thả chỉ định.
        /// Bỏ qua xe công thành vì xe được kích hoạt riêng qua DeployTroops("siege_machine").
        /// </summary>
        public void DeployHeroes(CancellationToken token = default)
        {
            if (IsStopRequested(token)) return;
            _heroAbilityTabs.Clear();

            Console.WriteLine("[ATTACK-CS] phase=deploy_heroes status=start");
            Stopwatch sw = Stopwatch.StartNew();
            foreach (var hero in _heroCoords)
            {
                // Bỏ qua xe công thành vì nhấp lại xe đã thả sẽ kích hoạt tự hủy (self-destruct) trong game CoC
                if (hero.Name == "siege_machine") continue;

                if (_tabs.TryGetValue(hero.Name, out Point tab))
                {
                    _heroAbilityTabs[hero.Name] = tab;
                    _adb.Tap(tab.X, tab.Y);
                    if (InterruptibleSleep(72, token)) return;
                    Point jittered = JitterCoord(hero.Coord);
                    _adb.Tap(jittered.X, jittered.Y);
                    if (InterruptibleSleep(72, token)) return;
                }
            }

            sw.Stop();
            if (VerboseTemplateLogs) Console.WriteLine($"[ATTACK-CS] phase=deploy_heroes status=success duration={sw.ElapsedMilliseconds}ms");
        }

        /// <summary>
        /// Bấm chọn thẻ và thả phép (Spell) tại các điểm chỉ định, có độ trễ giữa các lần thả để tránh thả chồng chéo.
        /// </summary>
        public void DeploySpells(string spellKey, CancellationToken token = default)
        {
            if (IsStopRequested(token)) return;
            if (!TryResolveSpellDeployment(spellKey, out Point tab, out List<Point> coords, out int delay))
            {
                return;
            }

            Console.WriteLine($"[ATTACK-CS] phase=deploy_spell status=start item={spellKey} count={coords.Count}");
            Stopwatch sw = Stopwatch.StartNew();
            _adb.Tap(tab.X, tab.Y);

            foreach (var pt in coords)
            {
                if (InterruptibleSleep(delay, token)) return;
                Point jittered = JitterCoord(pt);
                _adb.Tap(jittered.X, jittered.Y);
            }

            sw.Stop();
            if (VerboseTemplateLogs) Console.WriteLine($"[ATTACK-CS] phase=deploy_spell status=success item={spellKey} duration={sw.ElapsedMilliseconds}ms");
        }

        /// <summary>
        /// Phân tích và nạp các thông tin thẻ phép để chuẩn bị thả.
        /// </summary>
        private bool TryResolveSpellDeployment(string spellKey, out Point tab, out List<Point> coords, out int delay)
        {
            string key = spellKey.ToLower();
            tab = default;
            coords = new List<Point>();
            delay = key == "rage" ? SpellCastDelayMs : FreezeCastDelayMs;

            if (!_tabs.TryGetValue(key, out tab))
            {
                Console.WriteLine($"[ATTACK-CS WARNING] phase=deploy_spell status=skip item={spellKey} reason=tab_not_found");
                return false;
            }

            if (!_deployCoords.TryGetValue(key, out List<Point>? resolvedCoords) || resolvedCoords == null || resolvedCoords.Count == 0)
            {
                return false;
            }

            coords = resolvedCoords;
            return true;
        }

        /// <summary>
        /// Nhấp lại liên tục vào các thẻ Tướng để kích hoạt kỹ năng đặc biệt (Special Ability/Iron Fist/Royal Cloak...) của họ.
        /// </summary>
        public void RetapHeroes(CancellationToken token = default)
        {
            if (IsStopRequested(token)) return;
            Console.WriteLine("[ATTACK-CS] phase=activate_abilities status=start");
            string[] tags = { "warden", "queen", "bk", "prince", "rc" };
            foreach (var tag in tags)
            {
                if (_heroAbilityTabs.TryGetValue(tag, out Point tab) || _tabs.TryGetValue(tag, out tab))
                {
                    _adb.Tap(tab.X, tab.Y);
                    if (InterruptibleSleep(108, token)) return;
                }
                else
                {
                    Console.WriteLine($"[ATTACK-CS WARNING] phase=activate_abilities status=skip item={tag} reason=tab_unavailable");
                }
            }
        }

        /// <summary>
        /// Chạy toàn bộ kịch bản tấn công (tấn công bằng Rồng hoặc Rồng điện) theo chiến thuật chỉ định.
        /// Tự động sinh ngẫu nhiên hướng tấn công là cánh Trái hay cánh Phải để đa dạng hóa hành vi.
        /// </summary>
        /// <param name="attackStrategy">Chiến thuật tấn công ("Dragon_Attack" hoặc "ElectroDragon_Attack").</param>
        /// <param name="token">Token dùng để hủy bỏ tiến trình nếu cần dừng bot.</param>
        public void Run(string attackStrategy = "Dragon_Attack", CancellationToken token = default)
        {
            if (IsStopRequested(token)) return;
            // Ngẫu nhiên chọn hướng tấn công trái/phải
            _side = _rand.Next(0, 2) == 0 ? "left" : "right";
            InitializePatterns();

            Console.WriteLine("[ATTACK-CS] phase=run_attack status=start strategy=\"" + attackStrategy + "\" side=\"" + _side.ToUpper() + "\"");

            if (attackStrategy == "Dragon_Attack")
            {
                _requiredTabs.Clear();
                foreach (string key in new[] { "dragon", "balloon", "rage", "freeze" })
                {
                    _requiredTabs.Add(key);
                }

                if (_deployCoords.ContainsKey("siege_machine"))
                {
                    _requiredTabs.Add("siege_machine");
                }

                UpdateTabs();

                // Kịch bản rải Rồng và kiểm tra thả hết lính
                DeployTroops("dragon", token);
                if (IsStopRequested(token)) return;

                // Thả quân hỗ trợ nếu có trên thanh quân.
                DeployIfPresent("ice_minion", token);
                DeployIfPresent("ice_golem", token);
                DeployIfPresent("azure_dragon", token);

                // Thả nhanh quân sự kiện phụ trợ nếu có.
                DeployQuickIfPresent("event_goblin", token: token);
                DeployQuickIfPresent("nguoimay", 50, token);
                DeployQuickIfPresent("phuthuycuoichoi", 50, token);
                if (IsStopRequested(token)) return;

                // Thả Balloon đi kèm để dọn bẫy bay và hút sát thương
                DeployTroops("balloon", token);
                if (IsStopRequested(token)) return;

                // Thả xe công thành và Tướng
                DeployTroops("siege_machine", token);
                DeployHeroes(token);

                // Chờ quân/tướng vào nhịp trước khi thả phép hỗ trợ.
                if (InterruptibleSleep(SpellPhaseDelayMs, token)) return;
                DeploySpells("rage", token);
                DeploySpells("freeze", token);

                EnsureTroopFullyDeployed("dragon");
            }
            else if (attackStrategy == "ElectroDragon_Attack")
            {
                _requiredTabs.Clear();
                foreach (string key in new[] { "e_drag", "balloon", "rage", "freeze" })
                {
                    _requiredTabs.Add(key);
                }

                if (_deployCoords.ContainsKey("siege_machine"))
                {
                    _requiredTabs.Add("siege_machine");
                }

                UpdateTabs();

                // Rải Rồng điện (E-Drag)
                DeployTroops("e_drag", token);
                if (IsStopRequested(token)) return;
                DeployIfPresent("ice_minion", token);
                DeployIfPresent("ice_golem", token);
                DeployIfPresent("azure_dragon", token);

                DeployQuickIfPresent("event_goblin", token: token);
                DeployQuickIfPresent("nguoimay", 50, token);
                DeployQuickIfPresent("phuthuycuoichoi", 50, token);
                if (IsStopRequested(token)) return;

                // Rải Balloon
                DeployTroops("balloon", token);
                if (IsStopRequested(token)) return;

                DeployTroops("siege_machine", token);
                DeployHeroes(token);

                if (InterruptibleSleep(SpellPhaseDelayMs, token)) return;
                DeploySpells("rage", token);
                DeploySpells("freeze", token);

                EnsureTroopFullyDeployed("e_drag");
            }
            else
            {
                _requiredTabs.Clear();
                UpdateTabs();
                Console.WriteLine($"[ATTACK-CS ERROR] phase=run_attack status=fail reason=unknown_strategy strategy=\"{attackStrategy}\"");
            }

            // Cho tướng giao tranh một lúc rồi mới kích hoạt kỹ năng.
            if (InterruptibleSleep(HeroAbilityDelayMs, token)) return;
            RetapHeroes(token);
            Console.WriteLine("[ATTACK-CS] phase=run_attack status=success");
        }
    }
}
