using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using OpenCvSharp;
using Point = OpenCvSharp.Point;

namespace CvAut
{
    public class HeroInfo
    {
        public string Name { get; set; } = "";
        public Point Coord { get; set; }
    }

    public class Attacks
    {
        private readonly ADBHelper _adb;
        private readonly VisionEngine _vision;
        private readonly Random _rand = new();

        private const int ScreenWidth = 1600;
        private const double MatchThreshold = 0.52;
        private const int TroopTabSelectDelayMs = 500;
        private const int DuplicateTabDistancePx = 45;
        private const int RemainingTroopSettleDelayMs = 900;
        private const int MaxRemainingDeployPasses = 3;
        private const int SpellTabMinSeparationPx = 45;
        private const double SpellTabAmbiguityScoreDelta = 0.06;
        private static readonly Rect DeployBarRoi = Rect.FromLTRB(70, 720, 1180, 890);

        // Coordinate presets for troop/spell deployment (Left-side)
        private static readonly List<Point> DragonL = new()
        {
            new(170, 384), new(214, 348), new(246, 327), new(270, 306), new(305, 285), new(345, 255),
            new(368, 238), new(396, 216), new(417, 201), new(442, 182), new(487, 152), new(535, 121),
            new(640, 35),  new(442, 182)
        };

        private static readonly List<Point> BalloonL = new()
        {
            new(170, 384), new(214, 348), new(246, 327), new(270, 306), new(305, 285), new(345, 255),
            new(368, 238), new(396, 216), new(417, 201), new(444, 183), new(486, 154), new(534, 122),
            new(345, 255), new(444, 183), new(368, 238), new(246, 327), new(417, 201)
        };

        private static readonly List<Point> DragonR = new()
        {
            new(1344, 346), new(1272, 295), new(1234, 261), new(1191, 229), new(1150, 200), new(1116, 173),
            new(1074, 138), new(1042, 114), new(1000, 91), new(946, 47), new(904, 18), new(1033, 108),
            new(1091, 152), new(1109, 172)
        };

        private static readonly List<Point> DragonFallbackL = new()
        {
            new(145, 420), new(171, 384), new(214, 348), new(246, 327), new(270, 306), new(305, 285),
            new(345, 255), new(396, 216), new(442, 182), new(487, 152), new(535, 121), new(610, 66),
            new(185, 500), new(238, 562), new(304, 616), new(374, 670)
        };

        private static readonly List<Point> BalloonFallbackL = new()
        {
            new(145, 420), new(170, 384), new(214, 348), new(246, 327), new(270, 306), new(305, 285),
            new(345, 255), new(368, 238), new(396, 216), new(417, 201), new(444, 183), new(486, 154),
            new(534, 122), new(185, 500), new(238, 562), new(304, 616), new(374, 670)
        };

        private static readonly List<Point> BalloonR = new()
        {
            new(1344, 346), new(1272, 295), new(1234, 261), new(1191, 229), new(1150, 200), new(1116, 173),
            new(1074, 138), new(1042, 114), new(1000, 91), new(946, 47), new(904, 18), new(1033, 108),
            new(1091, 152), new(1109, 172), new(1207, 209), new(1296, 273), new(1311, 256)
        };

        private static readonly List<Point> RageL = new()
        {
            new(549, 353), new(674, 247), new(797, 317), new(690, 439), new(777, 403)
        };

        private static readonly List<Point> FreezeL = new()
        {
            new(614, 371), new(769, 276), new(770, 363), new(704, 494), new(798, 405), new(874, 405)
        };

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
        private string _side = "left";
        private readonly HashSet<string> _requiredTabs = new(StringComparer.OrdinalIgnoreCase);

        public Attacks(ADBHelper adb, VisionEngine vision)
        {
            _adb = adb;
            _vision = vision;
        }

        private Point JitterCoord(Point pt)
        {
            int dx, dy;
            if (_side == "left")
            {
                dx = _rand.Next(0, 40);       // [0, 39]
                dy = _rand.Next(-27, 1);      // [-27, 0]
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
                _fallbackDeployCoords["balloon"] = BalloonFallbackL;

                _heroCoords = HeroL.ConvertAll(h => new HeroInfo { Name = h.Name, Coord = h.Coord });
            }
            else
            {
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
                _fallbackDeployCoords["balloon"] = BalloonFallbackL.ConvertAll(mirror);

                _heroCoords = HeroL.ConvertAll(h => new HeroInfo { Name = h.Name, Coord = mirror(h.Coord) });
            }
        }

        public void UpdateTabs()
        {
            Console.WriteLine("[ATTACK-CS] Đang dò tìm các thẻ lính/phép ở dưới màn hình...");
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

                if (isSpell)
                {
                    Console.WriteLine($"[TPL] {kvp.Key} primary scan: template='{kvp.Value}', threshold={threshold:F2}, score={score:F2}, found={(coord != null ? coord.Value.ToString() : "null")}");
                }

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
                        Console.WriteLine($"[TPL] freeze fallback scan: template='{fallbackTemplate}', roi={widerRoi}, threshold={fallbackThreshold:F2}, score={fallbackScore:F2}, found={(coord != null ? coord.Value.ToString() : "null")}");
                        score = fallbackScore;
                        if (coord != null)
                        {
                            break;
                        }
                    }
                }

                if (coord != null)
                {
                    if (IsDuplicateTab(coord.Value, out string existingName))
                    {
                        Console.WriteLine($"[TPL] {kvp.Key} duplicate of {existingName} at {coord.Value} (score={score:F2}) -> skipped");
                        continue;
                    }

                    _tabs[kvp.Key] = coord.Value;
                    Console.WriteLine($"[ATTACK-CS] Đã phát hiện thẻ '{kvp.Key}' tại: {coord.Value} score={score:F2}");
                }

                if (coord == null && _requiredTabs.Contains(kvp.Key))
                {
                    Console.WriteLine($"[TPL] {kvp.Key} not found (score={score:F2}, threshold={threshold:F2})");

                }
            }

            // Dò xe công thành
            Point? swt = _vision.FindElement(screenshot, "troops/siege_with_troops", MatchThreshold, DeployBarRoi, out double swtScore);
            Point? es = _vision.FindElement(screenshot, "troops/empty_siege", MatchThreshold, DeployBarRoi, out double esScore);
            Console.WriteLine($"[DEBUG] siege_with_troops: max match = {swtScore:F3}");
            Console.WriteLine($"[DEBUG] empty_siege: max match = {esScore:F3}");
            if (swt != null)
            {
                _tabs["siege_machine"] = swt.Value;
                Console.WriteLine($"[ATTACK-CS] Đã phát hiện xe công thành có quân tại: {swt.Value} score={swtScore:F2}");
            }
            else if (es != null)
            {
                _tabs["siege_machine"] = es.Value;
                Console.WriteLine($"[ATTACK-CS] Đã phát hiện xe công thành rỗng tại: {es.Value} score={esScore:F2}");
            }
        }

        private bool AreTabsTooClose(Point a, Point b)
        {
            return Math.Abs(a.X - b.X) <= SpellTabMinSeparationPx && Math.Abs(a.Y - b.Y) <= SpellTabMinSeparationPx;
        }

        private bool IsDuplicateTab(Point candidate, out string existingName)
        {
            foreach (var kvp in _tabs)
            {
                int dx = kvp.Value.X - candidate.X;
                int dy = kvp.Value.Y - candidate.Y;
                if ((dx * dx) + (dy * dy) <= DuplicateTabDistancePx * DuplicateTabDistancePx)
                {
                    existingName = kvp.Key;
                    return true;
                }
            }

            existingName = "";
            return false;
        }

        private void DeployOptionalQuickDrop(string troopKey, int limit = 10)
        {
            Console.WriteLine($"[ATTACK-CS] Làm mới vị trí thẻ '{troopKey}' trước khi thả nhanh...");
            UpdateTabs();

            if (!_tabs.TryGetValue(troopKey, out Point troopTab))
            {
                return;
            }

            if (!_deployCoords.TryGetValue("dragon", out List<Point>? dragonCoords) || dragonCoords.Count == 0)
            {
                Console.WriteLine($"[ATTACK-CS] Không có tọa độ rải nhanh cho '{troopKey}'.");
                return;
            }

            _adb.Tap(troopTab.X, troopTab.Y);
            Thread.Sleep(TroopTabSelectDelayMs);

            int tapLimit = Math.Max(0, limit);
            var quickTaps = new List<Point>(tapLimit);
            for (int i = 0; i < tapLimit; i++)
            {
                quickTaps.Add(JitterCoord(dragonCoords[i % dragonCoords.Count]));
            }

            Console.WriteLine($"[ATTACK-CS] Thả nhanh '{troopKey}' ({tapLimit} taps)...");
            _adb.TapSequence(quickTaps);
        }

        public void DeployTroops(string troopKey)
        {
            string key = troopKey.ToLower();
            Console.WriteLine($"[ATTACK-CS] Làm mới vị trí thẻ '{troopKey}' trước khi thả quân...");
            UpdateTabs();

            if (!_tabs.TryGetValue(key, out Point tab))
            {
                Console.WriteLine($"[ATTACK-CS] Bỏ qua: Thẻ '{troopKey}' không tìm thấy.");
                return;
            }

            if (!_deployCoords.TryGetValue(key, out List<Point>? coords) || coords.Count == 0)
            {
                Console.WriteLine($"[ATTACK-CS] Bỏ qua: Không có tọa độ thả lính cho '{troopKey}'.");
                return;
            }

            Console.WriteLine($"[ATTACK-CS] Chọn thẻ '{troopKey}' -> Đang thả quân ({coords.Count} taps)...");
            Stopwatch sw = Stopwatch.StartNew();
            _adb.Tap(tab.X, tab.Y);
            Thread.Sleep(TroopTabSelectDelayMs);

            var taps = new List<Point>(coords.Count);
            foreach (var pt in coords)
            {
                taps.Add(JitterCoord(pt));
            }

            _adb.TapSequence(taps);

            sw.Stop();
            Console.WriteLine($"[ATTACK-CS DEBUG] '{troopKey}' deploy elapsed={sw.ElapsedMilliseconds}ms.");
        }

        public void EnsureTroopFullyDeployed(string troopKey)
        {
            string key = troopKey.ToLower();
            if (!_tabs.TryGetValue(key, out Point tab))
            {
                Console.WriteLine($"[ATTACK-CS] Không kiểm tra quân còn lại: Thẻ '{troopKey}' không tìm thấy.");
                return;
            }

            if (!_fallbackDeployCoords.TryGetValue(key, out List<Point>? fallbackCoords) || fallbackCoords.Count == 0)
            {
                if (!_deployCoords.TryGetValue(key, out fallbackCoords) || fallbackCoords.Count == 0)
                {
                    Console.WriteLine($"[ATTACK-CS] Không có điểm rải bổ sung cho '{troopKey}'.");
                    return;
                }
            }

            for (int pass = 1; pass <= MaxRemainingDeployPasses; pass++)
            {
                Thread.Sleep(RemainingTroopSettleDelayMs);
                int remaining = ReadRemainingTroopCount(key, out double confidence);
                if (remaining < 0)
                {
                    Console.WriteLine($"[ATTACK-CS] Không đọc được số quân còn lại của '{troopKey}', bỏ qua rải bù.");
                    return;
                }

                if (remaining == 0)
                {
                    Console.WriteLine($"[ATTACK-CS] '{troopKey}' đã rải hết.");
                    return;
                }

                Console.WriteLine($"[ATTACK-CS WARNING] '{troopKey}' còn x{remaining} trên thanh quân (conf={confidence:F2}). Rải bù pass {pass}/{MaxRemainingDeployPasses}...");
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

            Thread.Sleep(RemainingTroopSettleDelayMs);
            int finalRemaining = ReadRemainingTroopCount(key, out double finalConfidence);
            if (finalRemaining > 0)
            {
                Console.WriteLine($"[ATTACK-CS WARNING] Sau rải bù, '{troopKey}' vẫn còn x{finalRemaining} (conf={finalConfidence:F2}).");
            }
        }

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

            Rect countRoi = Rect.FromLTRB(tab.X - 5, tab.Y - 94, tab.X + 72, tab.Y - 42);
            if (TryReadCountFromRoi(screenshot, countRoi, out int value, out confidence))
            {
                return value;
            }

            Rect digitOnlyRoi = Rect.FromLTRB(tab.X + 22, tab.Y - 96, tab.X + 78, tab.Y - 50);
            if (TryReadCountFromRoi(screenshot, digitOnlyRoi, out value, out confidence))
            {
                return value;
            }

            Rect widerCountRoi = Rect.FromLTRB(tab.X - 20, tab.Y - 98, tab.X + 78, tab.Y - 40);
            if (TryReadCountFromRoi(screenshot, widerCountRoi, out value, out confidence))
            {
                return value;
            }

            return -1;
        }

        private bool TryReadCountFromRoi(Mat screenshot, Rect roi, out int value, out double confidence)
        {
            if (_vision.TryExtractNumericalMetrics(screenshot, roi, out value, out confidence, useRgbThresh: true) && IsPlausibleTroopCount(value, confidence))
            {
                return true;
            }

            return _vision.TryExtractNumericalMetrics(screenshot, roi, out value, out confidence) && IsPlausibleTroopCount(value, confidence);
        }

        private static bool IsPlausibleTroopCount(int value, double confidence)
        {
            return confidence >= 0.55 && value >= 0 && value <= 99;
        }

        public void DeployHeroes()
        {
            Console.WriteLine("[ATTACK-CS] Làm mới vị trí thẻ tướng trước khi triển khai...");
            UpdateTabs();

            Console.WriteLine("[ATTACK-CS] Đang triển khai quân tướng...");
            Stopwatch sw = Stopwatch.StartNew();
            foreach (var hero in _heroCoords)
            {
                // Skip siege_machine — already deployed via DeployTroops("siege_machine").
                // Re-tapping a deployed siege in CoC triggers self-destruct.
                if (hero.Name == "siege_machine") continue;

                if (_tabs.TryGetValue(hero.Name, out Point tab))
                {
                    _adb.Tap(tab.X, tab.Y);
                    Point jittered = JitterCoord(hero.Coord);
                    _adb.Tap(jittered.X, jittered.Y);
                }
            }

            sw.Stop();
            Console.WriteLine($"[ATTACK-CS DEBUG] Heroes deploy elapsed={sw.ElapsedMilliseconds}ms.");
        }

        public void DeploySpells(string spellKey)
        {
            if (!TryResolveSpellDeployment(spellKey, out Point tab, out List<Point> coords, out int delay))
            {
                return;
            }

            Console.WriteLine($"[ATTACK-CS] Chọn thẻ phép '{spellKey}' -> Đang thả phép ({coords.Count} spells)...");
            Stopwatch sw = Stopwatch.StartNew();
            _adb.Tap(tab.X, tab.Y);

            foreach (var pt in coords)
            {
                Thread.Sleep(delay);
                Point jittered = JitterCoord(pt);
                _adb.Tap(jittered.X, jittered.Y);
            }

            sw.Stop();
            Console.WriteLine($"[ATTACK-CS DEBUG] '{spellKey}' spell elapsed={sw.ElapsedMilliseconds}ms.");
        }

        private bool TryResolveSpellDeployment(string spellKey, out Point tab, out List<Point> coords, out int delay)
        {
            string key = spellKey.ToLower();
            tab = default;
            coords = new List<Point>();
            delay = key == "rage" ? 1000 : 2000;

            Console.WriteLine($"[ATTACK-CS] Làm mới vị trí thẻ phép '{spellKey}' trước khi thả...");
            UpdateTabs();

            if (!_tabs.TryGetValue(key, out tab))
            {
                Console.WriteLine($"[ATTACK-CS] Bỏ qua: Thẻ phép '{spellKey}' không tìm thấy.");
                return false;
            }

            if (!_deployCoords.TryGetValue(key, out List<Point>? resolvedCoords) || resolvedCoords == null || resolvedCoords.Count == 0)
            {
                return false;
            }

            coords = resolvedCoords;

            return true;
        }

        public void RetapHeroes()
        {
            Console.WriteLine("[ATTACK-CS] Làm mới vị trí thẻ tướng trước khi kích hoạt kỹ năng...");
            UpdateTabs();

            Console.WriteLine("[ATTACK-CS] Đang kích hoạt kỹ năng đặc biệt của Tướng...");
            string[] tags = { "warden", "queen", "bk", "prince", "rc" };
            foreach (var tag in tags)
            {
                if (_tabs.TryGetValue(tag, out Point tab))
                {
                    _adb.Tap(tab.X, tab.Y);
                }
            }
        }

        public void Run(string attackStrategy = "Dragon_Attack")
        {
            _side = _rand.Next(0, 2) == 0 ? "left" : "right";
            InitializePatterns();

            Console.WriteLine($"\n==============================================");
            Console.WriteLine($"[ATTACK-CS] Thực thi: {attackStrategy} | Tấn công cánh: {_side.ToUpper()}");
            Console.WriteLine($"==============================================");

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
                DeployTroops("dragon");
                EnsureTroopFullyDeployed("dragon");
                DeployTroops("ice_minion");
                DeployTroops("ice_golem");
                DeployTroops("azure_dragon");

                DeployOptionalQuickDrop("event_goblin");
                DeployOptionalQuickDrop("nguoimay", 50);
                DeployOptionalQuickDrop("phuthuycuoichoi", 50);

                DeployTroops("balloon");
                EnsureTroopFullyDeployed("balloon");
                DeployTroops("siege_machine");
                DeployHeroes();
                DeploySpells("rage");
                DeploySpells("freeze");
                EnsureTroopFullyDeployed("dragon");
                EnsureTroopFullyDeployed("balloon");
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
                DeployTroops("e_drag");
                DeployTroops("ice_minion");
                DeployTroops("ice_golem");
                DeployTroops("azure_dragon");

                DeployOptionalQuickDrop("event_goblin");
                DeployOptionalQuickDrop("nguoimay", 50);
                DeployOptionalQuickDrop("phuthuycuoichoi", 50);

                DeployTroops("balloon");
                EnsureTroopFullyDeployed("balloon");
                DeployTroops("siege_machine");
                DeployHeroes();
                DeploySpells("rage");
                DeploySpells("freeze");
            }
            else
            {
                _requiredTabs.Clear();
                UpdateTabs();
                Console.WriteLine($"[ATTACK-CS ERROR] Chiến thuật không xác định: {attackStrategy}");
            }

            RetapHeroes();
            Console.WriteLine("[ATTACK-CS] Kịch bản cướp trận hoàn tất.");
        }
    }
}
