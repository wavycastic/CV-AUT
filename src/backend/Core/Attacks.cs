using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
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

    internal sealed class AttackDelayConfig
    {
        public int TroopDeployDelayMs { get; init; } = 60;
        public int RageSpellDelayMs { get; init; } = 650;
        public int FreezeSpellDelayMs { get; init; } = 850;
        public int GrandWardenAbilityDelayMs { get; init; } = 2500;
    }

    internal sealed class SpellDeploymentGroups
    {
        public List<Point> RageInitial { get; init; } = new();
        public List<Point> Freeze { get; init; } = new();
        public List<Point> RageRemaining { get; init; } = new();
    }

    internal sealed class AttackCoordinateConfig
    {
        public Dictionary<string, SpellDeploymentGroups> SpellCoordinates { get; init; } = new(StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Phân hệ Tấn công (Attacks):
    /// - Quản lý tọa độ rải quân mặc định theo cánh trái/phải đối xứng.
    /// - Dò tìm các thẻ quân, phép, tướng hiện có trên giao diện chiến trận dưới đáy màn hình.
    /// - Thực hiện kịch bản rải quân (tạp biến ngẫu nhiên chống chống-bot), rải phép đóng băng/cuồng nộ.
    /// - Quét số lượng lính còn dư để tiến hành rải bù.
    /// </summary>
    internal partial class Attacks
    {
        private readonly ADBHelper _adb;
        private readonly VisionEngine _vision;
        private readonly string _templatesPath;
        private readonly AttackDelayConfig _delays;
        private readonly AttackCoordinateConfig _coordinates;
        private readonly Random _rand = new();

        private const int ScreenWidth = 1600;
        private const double MatchThreshold = 0.52;
        private const int TroopTabSelectDelayMs = 160;
        private const int DefaultTroopDeployDelayMs = 60;
        private const int DefaultRageSpellDelayMs = 650;
        private const int DefaultFreezeSpellDelayMs = 850;
        private const int SpellPhaseDelayMs = 1200;
        private const int DefaultHeroAbilityDelayMs = 2500;
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

        // Tọa độ rải quân từ góc DƯỚI-TRÁI. Không mirror theo Y vì bản đồ isometric và thanh UI che cạnh dưới.
        private static readonly List<Point> DragonBottomL = new()
        {
            new(176, 500), new(205, 520), new(242, 548), new(281, 576), new(326, 607), new(372, 637),
            new(418, 670), new(460, 650), new(505, 620), new(550, 590), new(595, 560), new(640, 530),
            new(685, 500), new(730, 470)
        };

        private static readonly List<Point> BalloonBottomL = new()
        {
            new(176, 500), new(205, 520), new(242, 548), new(281, 576), new(326, 607), new(372, 637),
            new(418, 670), new(460, 650), new(505, 620), new(550, 590), new(595, 560), new(640, 530),
            new(685, 500), new(730, 470), new(326, 607), new(418, 670), new(505, 620)
        };

        private static readonly List<Point> DragonFallbackBottomL = new()
        {
            new(176, 500), new(205, 520), new(242, 548), new(281, 576), new(326, 607), new(372, 637),
            new(418, 670), new(460, 650), new(505, 620), new(550, 590), new(595, 560), new(640, 530),
            new(685, 500), new(730, 470), new(300, 690), new(380, 675)
        };

        private static readonly List<Point> BalloonFallbackBottomL = new()
        {
            new(176, 500), new(205, 520), new(242, 548), new(281, 576), new(326, 607), new(372, 637),
            new(418, 670), new(460, 650), new(505, 620), new(550, 590), new(595, 560), new(640, 530),
            new(685, 500), new(326, 607), new(418, 670), new(505, 620), new(380, 675)
        };

        private static readonly List<Point> RageBottomL = new()
        {
            new(568, 548), new(688, 645), new(812, 584), new(716, 480), new(800, 510)
        };

        private static readonly List<Point> FreezeBottomL = new()
        {
            new(620, 538), new(768, 624), new(774, 540), new(706, 430), new(800, 506), new(874, 506)
        };

        private static readonly List<HeroInfo> HeroBottomL = new()
        {
            new() { Name = "siege_machine", Coord = new Point(418, 670) },
            new() { Name = "queen",         Coord = new Point(418, 670) },
            new() { Name = "bk",            Coord = new Point(300, 690) },
            new() { Name = "warden",        Coord = new Point(372, 637) },
            new() { Name = "prince",        Coord = new Point(372, 637) },
            new() { Name = "rc",            Coord = new Point(460, 650) }
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
        private string _attackDirection = "top_left";
        private readonly HashSet<string> _requiredTabs = new(StringComparer.OrdinalIgnoreCase);
        private bool _scanElectroDragonTab = true;

        private static bool IsStopRequested(CancellationToken token) => token.IsCancellationRequested;

        private static bool InterruptibleSleep(int milliseconds, CancellationToken token)
        {
            return token.WaitHandle.WaitOne(milliseconds);
        }

        /// <summary>
        /// Khởi tạo đối tượng Attacks điều khiển trận đánh.
        /// </summary>
        public Attacks(ADBHelper adb, VisionEngine vision, string? templatesPath = null, AttackDelayConfig? delays = null, AttackCoordinateConfig? coordinates = null)
        {
            _adb = adb;
            _vision = vision;
            _templatesPath = string.IsNullOrWhiteSpace(templatesPath) ? vision.TemplatesDirectory : templatesPath;
            _delays = delays ?? new AttackDelayConfig();
            _coordinates = coordinates ?? new AttackCoordinateConfig();
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

            Converter<Point, Point> mirror = pt => new Point(ScreenWidth - 1 - pt.X, pt.Y);
            bool bottom = _attackDirection.StartsWith("bottom", StringComparison.OrdinalIgnoreCase);
            bool right = _attackDirection.EndsWith("right", StringComparison.OrdinalIgnoreCase);
            Converter<Point, Point> transform = right ? mirror : pt => pt;

            List<Point> dragon = bottom ? DragonBottomL.ConvertAll(transform) : right ? DragonR : DragonL;
            List<Point> balloon = bottom ? BalloonBottomL.ConvertAll(transform) : right ? BalloonR : BalloonL;
            List<Point> rage = (bottom ? RageBottomL : RageL).ConvertAll(transform);
            List<Point> freeze = (bottom ? FreezeBottomL : FreezeL).ConvertAll(transform);
            List<Point> dragonFallback = (bottom ? DragonFallbackBottomL : DragonFallbackL).ConvertAll(transform);
            List<Point> balloonFallback = (bottom ? BalloonFallbackBottomL : BalloonFallbackL).ConvertAll(transform);
            List<HeroInfo> heroes = (bottom ? HeroBottomL : HeroL).ConvertAll(h => new HeroInfo { Name = h.Name, Coord = transform(h.Coord) });

            _deployCoords["dragon"] = dragon;
            _deployCoords["e_drag"] = dragon.GetRange(2, Math.Min(10, dragon.Count - 2));
            _deployCoords["balloon"] = balloon;
            _deployCoords["rage"] = rage;
            _deployCoords["rage_initial"] = rage.Take(2).ToList();
            _deployCoords["freeze"] = freeze;
            _deployCoords["rage_remaining"] = rage.Skip(2).ToList();
            _deployCoords["siege_machine"] = new List<Point> { heroes[0].Coord };
            _deployCoords["azure_dragon"] = new List<Point> { heroes[2].Coord };
            _deployCoords["ice_minion"] = dragon.GetRange(2, Math.Min(10, dragon.Count - 2));
            _deployCoords["ice_golem"] = dragon.GetRange(4, Math.Min(5, dragon.Count - 4));
            _fallbackDeployCoords["dragon"] = dragonFallback;
            _fallbackDeployCoords["e_drag"] = dragonFallback;
            _fallbackDeployCoords["balloon"] = balloonFallback;

            _heroCoords = heroes;

            ApplyCustomSpellCoordinates();
        }

        private void ApplyCustomSpellCoordinates()
        {
            if (!_coordinates.SpellCoordinates.TryGetValue(_attackDirection, out SpellDeploymentGroups? groups))
            {
                return;
            }

            if (groups.RageInitial.Count > 0)
            {
                _deployCoords["rage_initial"] = groups.RageInitial;
            }

            if (groups.Freeze.Count > 0)
            {
                _deployCoords["freeze"] = groups.Freeze;
            }

            if (groups.RageRemaining.Count > 0)
            {
                _deployCoords["rage_remaining"] = groups.RageRemaining;
            }

            List<Point> customRage = _deployCoords.GetValueOrDefault("rage_initial", new List<Point>())
                .Concat(_deployCoords.GetValueOrDefault("rage_remaining", new List<Point>()))
                .ToList();
            if (customRage.Count > 0)
            {
                _deployCoords["rage"] = customRage;
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
                { "balloon", "troops/balloon" },
                { "event_goblin", "troops/event_goblin" },
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

            if (_scanElectroDragonTab)
            {
                categories["e_drag"] = "troops/E_Drag";
            }
            else
            {
                Console.WriteLine("[ATTACK-CS] phase=scan_bar status=skip action=scan item=e_drag reason=strategy_not_selected");
            }

            foreach (var eventTroop in LoadEventTroopTemplates())
            {
                categories[eventTroop.Key] = eventTroop.Template;
            }

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

        private IEnumerable<(string Key, string Template, int DropCount)> LoadEventTroopTemplates()
        {
            foreach (string name in TemplateAssetLoader.EnumerateNames(_templatesPath, "event"))
            {
                string key = "event_" + name.ToLowerInvariant().Replace(' ', '_').Replace('-', '_');
                int dropCount = 10;
                int underscore = name.LastIndexOf('_');
                if (underscore > 0 && underscore < name.Length - 1 && int.TryParse(name[(underscore + 1)..], out int parsedCount))
                {
                    dropCount = Math.Clamp(parsedCount, 1, 200);
                    key = "event_" + name[..underscore].ToLowerInvariant().Replace(' ', '_').Replace('-', '_');
                }

                yield return (key, "event/" + name, dropCount);
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
            _adb.TapSequenceSafeFast(quickTaps, batchSize: 5, batchDelayMs: _delays.TroopDeployDelayMs, token);
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

            _adb.TapSequenceSafeFast(taps, batchSize: 5, batchDelayMs: _delays.TroopDeployDelayMs, token);

            sw.Stop();
            Console.WriteLine($"[ATTACK-CS] phase=deploy status=success item={troopKey} tab=({tab.X},{tab.Y}) tap_count={taps.Count} duration={sw.ElapsedMilliseconds}ms");
        }

        /// <summary>
        /// Đảm bảo quân lính được thả hoàn toàn (rải bù nhanh nếu đọc thấy số lượng lính còn thừa trên giao diện thẻ).
        /// Chỉ chạy một lượt để ưu tiên tốc độ trong attack flow.
        /// </summary>
        public void EnsureTroopFullyDeployed(string troopKey, CancellationToken token = default)
        {
            if (IsStopRequested(token)) return;
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
                if (InterruptibleSleep(RemainingTroopSettleDelayMs, token)) return;
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
                    if (InterruptibleSleep(TroopTabSelectDelayMs, token)) return;
                    var fallbackTaps = new List<Point>(fallbackTapCount);
                    int fallbackStartOffset = ((pass - 1) * 4) % fallbackCoords.Count;
                    for (int i = 0; i < fallbackTapCount; i++)
                    {
                        fallbackTaps.Add(JitterCoord(fallbackCoords[(fallbackStartOffset + i) % fallbackCoords.Count]));
                    }

                    _adb.TapSequenceSafeFast(fallbackTaps, batchSize: 5, batchDelayMs: _delays.TroopDeployDelayMs, token);
                    return;
                }

                if (remaining == 0)
                {
                    Console.WriteLine($"[ATTACK-CS] phase=validate_remaining status=success item={troopKey} details=\"fully_deployed\"");
                    return;
                }

                Console.WriteLine($"[ATTACK-CS WARNING] phase=validate_remaining status=fallback item={troopKey} remaining={remaining} confidence={confidence:F2}");
                _adb.Tap(tab.X, tab.Y);
                if (InterruptibleSleep(TroopTabSelectDelayMs, token)) return;

                int tapCount = Math.Min(remaining + 2, fallbackCoords.Count);
                var taps = new List<Point>(tapCount);
                int startOffset = ((pass - 1) * 5) % fallbackCoords.Count;
                for (int i = 0; i < tapCount; i++)
                {
                    taps.Add(JitterCoord(fallbackCoords[(startOffset + i) % fallbackCoords.Count]));
                }

                _adb.TapSequenceSafeFast(taps, batchSize: 5, batchDelayMs: _delays.TroopDeployDelayMs, token);
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
            Rect countRoi = ImageUtils.ClampRect(Rect.FromLTRB(tab.X - 5, tab.Y - 94, tab.X + 72, tab.Y - 42), screenshot.Width, screenshot.Height);
            if (TryReadCountFromRoi(screenshot, countRoi, out int value, out confidence))
            {
                return value;
            }

            // Thử vùng ROI số 2 (chỉ lấy phần chữ số)
            Rect digitOnlyRoi = ImageUtils.ClampRect(Rect.FromLTRB(tab.X + 22, tab.Y - 96, tab.X + 78, tab.Y - 50), screenshot.Width, screenshot.Height);
            if (TryReadCountFromRoi(screenshot, digitOnlyRoi, out value, out confidence))
            {
                return value;
            }

            // Thử vùng ROI số 3 (rộng hơn)
            Rect widerCountRoi = ImageUtils.ClampRect(Rect.FromLTRB(tab.X - 20, tab.Y - 98, tab.X + 78, tab.Y - 40), screenshot.Width, screenshot.Height);
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
            if (roi.Width <= 0 || roi.Height <= 0)
            {
                value = 0;
                confidence = 0;
                return false;
            }

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
            string tabKey = key.StartsWith("rage", StringComparison.OrdinalIgnoreCase) ? "rage" : key;
            tab = default;
            coords = new List<Point>();
            delay = tabKey == "rage" ? _delays.RageSpellDelayMs : _delays.FreezeSpellDelayMs;

            if (!_tabs.TryGetValue(tabKey, out tab))
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
                    Console.WriteLine($"[ATTACK-CS] phase=activate_abilities status=skip item={tag} reason=optional_unavailable");
                }
            }
        }

        private void DeployConfiguredEventTroops(bool enabled, CancellationToken token)
        {
            if (!enabled)
            {
                Console.WriteLine("[ATTACK-CS] phase=deploy status=skip item=event_troops reason=disabled");
                return;
            }

            var eventTroops = LoadEventTroopTemplates().ToList();
            if (eventTroops.Count == 0)
            {
                Console.WriteLine("[ATTACK-CS] phase=deploy status=skip item=event_troops reason=no_event_templates directory=event");
                return;
            }

            foreach (var troop in eventTroops)
            {
                if (IsStopRequested(token)) return;
                DeployQuickIfPresent(troop.Key, troop.DropCount, token);
            }
        }

        /// <summary>
        /// Chạy toàn bộ kịch bản tấn công (tấn công bằng Rồng hoặc Rồng điện) theo chiến thuật chỉ định.
        /// Tự động sinh ngẫu nhiên hướng tấn công là cánh Trái hay cánh Phải để đa dạng hóa hành vi.
        /// </summary>
        /// <param name="attackStrategy">Chiến thuật tấn công ("Dragon_Attack" hoặc "ElectroDragon_Attack").</param>
        /// <param name="token">Token dùng để hủy bỏ tiến trình nếu cần dừng bot.</param>
        public void Run(string attackStrategy = "Dragon_Attack", CancellationToken token = default, bool useEventTroops = false)
        {
            if (IsStopRequested(token)) return;
            string normalizedStrategy = NormalizeAttackStrategy(attackStrategy);
            // Tạm thời tắt hướng bottom_left/bottom_right khi thả lính.
            string[] directions = { "top_left", "top_right" };
            _attackDirection = directions[_rand.Next(directions.Length)];
            _side = _attackDirection.EndsWith("left", StringComparison.OrdinalIgnoreCase) ? "left" : "right";
            InitializePatterns();

            Console.WriteLine("[ATTACK-CS] phase=run_attack status=start strategy=\"" + attackStrategy + "\" normalized_strategy=\"" + normalizedStrategy + "\" side=\"" + _side.ToUpper() + "\" direction=\"" + _attackDirection + "\"");

            if (normalizedStrategy == "Dragon_Attack")
            {
                _scanElectroDragonTab = false;
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

                DeployConfiguredEventTroops(useEventTroops, token);
                if (IsStopRequested(token)) return;

                // Thả Balloon đi kèm để dọn bẫy bay và hút sát thương
                DeployTroops("balloon", token);
                if (IsStopRequested(token)) return;

                // Thả xe công thành và Tướng
                DeployTroops("siege_machine", token);
                DeployHeroes(token);

                // Chờ quân/tướng vào nhịp trước khi thả phép hỗ trợ.
                if (InterruptibleSleep(SpellPhaseDelayMs, token)) return;
                DeploySpells("rage_initial", token);
                DeploySpells("freeze", token);
                DeploySpells("rage_remaining", token);

                EnsureTroopFullyDeployed("dragon", token);
            }
            else if (normalizedStrategy == "ElectroDragon_Attack")
            {
                _scanElectroDragonTab = true;
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

                DeployConfiguredEventTroops(useEventTroops, token);
                if (IsStopRequested(token)) return;

                // Rải Balloon
                DeployTroops("balloon", token);
                if (IsStopRequested(token)) return;

                DeployTroops("siege_machine", token);
                DeployHeroes(token);

                if (InterruptibleSleep(SpellPhaseDelayMs, token)) return;
                DeploySpells("rage_initial", token);
                DeploySpells("freeze", token);
                DeploySpells("rage_remaining", token);

                EnsureTroopFullyDeployed("e_drag", token);
            }
            else
            {
                _scanElectroDragonTab = true;
                _requiredTabs.Clear();
                UpdateTabs();
                Console.WriteLine($"[ATTACK-CS ERROR] phase=run_attack status=fail reason=unknown_strategy strategy=\"{attackStrategy}\"");
            }

            // Cho tướng giao tranh một lúc rồi mới kích hoạt kỹ năng.
            if (InterruptibleSleep(_delays.GrandWardenAbilityDelayMs, token)) return;
            RetapHeroes(token);
            Console.WriteLine("[ATTACK-CS] phase=run_attack status=success");
        }

        private static string NormalizeAttackStrategy(string? attackStrategy)
        {
            string strategy = string.IsNullOrWhiteSpace(attackStrategy) ? "Dragon_Attack" : attackStrategy.Trim();
            return strategy switch
            {
                "Dragon_Attack" or "Dragon attack" => "Dragon_Attack",
                "ElectroDragon_Attack" or "Electro Dragon attack" => "ElectroDragon_Attack",
                _ => strategy
            };
        }
    }
}
