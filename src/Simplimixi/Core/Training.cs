using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using OpenCvSharp;
using Point = OpenCvSharp.Point;

namespace CvAut
{
    /// <summary>
    /// Phân hệ Huấn luyện lính (Training):
    /// - Quản lý quy trình huấn luyện lính nhanh (Quick Train) thông qua Đội hình mẫu mẫu lưu sẵn.
    /// - Huấn luyện lính thông minh (Smart Train): Quét nhận diện số lượng lính/phép/xe hiện tại trên giao diện quân đội,
    ///   so sánh với mẫu yêu cầu, tự động xóa hàng chờ cũ (Trash queue) và bấm thêm số lượng lính/phép còn thiếu.
    /// - Trích xuất kiểm tra dung lượng sức chứa tối đa của doanh trại.
    /// </summary>
    public class Training
    {
        private readonly ADBHelper _adb;
        private readonly VisionEngine _vision;
        private readonly string _templateRoot;

        // Các điểm chạm điều khiển trên giao diện thông tin Quân đội (Army Window)
        private static readonly Point OpenArmyWindow = new(62, 658);
        private static readonly Point CloseArmyWindow = new(1545, 81);
        private static readonly Point ArmyRecipePane = new(777, 90);
        private static readonly Point ConfirmRecipeUse = new(972, 584);

        // Vùng ROI dùng để xác thực cửa sổ Quân đội đang mở thành công
        private static readonly Rect ArmyWindowRoi = new(76, 57, 489, 99);
        
        // Vùng ROI của nút Sử dụng Đội hình mẫu 1 và 2 (Quick Slot 1 & 2)
        private static readonly Rect QuickSlot1Roi = Rect.FromLTRB(1364, 189, 1574, 425);
        private static readonly Rect QuickSlot2Roi = Rect.FromLTRB(1368, 486, 1572, 735);

        // Vùng ROI hiển thị lính, phép, xe hiện có trong quân đội
        private static readonly Rect ArmyRoi = Rect.FromLTRB(682, 228, 1573, 383);
        private static readonly Rect SpellRoi = Rect.FromLTRB(689, 461, 1250, 600);
        private static readonly Rect SiegeRoi = Rect.FromLTRB(1256, 457, 1554, 608);
        
        // Vùng ROI hiển thị chỉ số sức chứa lính hiện tại (ví dụ: "240/240")
        private static readonly Rect SpaceRoi = Rect.FromLTRB(750, 195, 826, 225);
        private static readonly Rect ArmySpaceSecondaryRoi = Rect.FromLTRB(751, 183, 858, 230);
        
        // Vùng ROI hiển thị sức chứa phép hiện tại
        private static readonly Rect SpellSpaceRoi = Rect.FromLTRB(731, 398, 810, 464);

        // Vùng ROI dò tìm nút Thùng rác (Xóa toàn bộ hàng chờ huấn luyện)
        private static readonly Rect TrashArmyRoi = Rect.FromLTRB(1519, 184, 1570, 231);
        private static readonly Rect TrashSpellRoi = Rect.FromLTRB(1197, 408, 1250, 455);
        private static readonly Rect TrashSiegeRoi = Rect.FromLTRB(1511, 406, 1577, 458);

        private const double ValidationIconThreshold = 0.70;

        // Tọa độ chạm nút xóa hàng chờ
        private static readonly Point TapClearArmy = new(1546, 209);
        private static readonly Point TapClearSpell = new(1225, 429);
        private static readonly Point TapClearSiege = new(1545, 427);
        private static readonly Point ConfirmTapArmy = new(969, 579);
        private static readonly Point ConfirmTapSpell = new(978, 583);
        private static readonly Point ConfirmTapSiege = new(966, 581);

        // Các nút chuyển tab huấn luyện lính, phép, xe trong giao diện nhà lính
        private static readonly Point OpenArmyTab = new(1063, 305);
        private static readonly Point CloseArmyTab = new(47, 85);
        private static readonly Point OpenSpellTab = new(1008, 531);
        private static readonly Point CloseSpellTab = new(59, 52);
        private static readonly Point OpenSiegeTab = new(1398, 533);
        private static readonly Point CloseSiegeTab = new(27, 85);

        // Trọng số sức chứa (Housing Space) của từng loại lính cụ thể để tính toán số lượng huấn luyện
        private static readonly Dictionary<string, int> SpaceCost = new(StringComparer.OrdinalIgnoreCase)
        {
            ["dragon"] = 20,            // Rồng thường chiếm 20 chỗ
            ["electro_dragon"] = 30,    // Rồng điện chiếm 30 chỗ
            ["balloon"] = 5             // Balloon chiếm 5 chỗ
        };

        // Danh sách định nghĩa đội hình tiêu chuẩn cho từng kịch bản chiến thuật
        private static readonly Dictionary<string, ArmySpec> ArmySets = new(StringComparer.OrdinalIgnoreCase)
        {
            ["Dragon_Attack"] = new("dragon", new[] { "dragon", "balloon" }, new[] { "rage", "freeze" }, "slammer"),
            ["Dragon attack"] = new("dragon", new[] { "dragon", "balloon" }, new[] { "rage", "freeze" }, "slammer"),
            ["ElectroDragon_Attack"] = new("electro_dragon", new[] { "electro_dragon", "balloon" }, new[] { "rage", "freeze" }, "slammer"),
            ["Electro Dragon attack"] = new("electro_dragon", new[] { "electro_dragon", "balloon" }, new[] { "rage", "freeze" }, "slammer")
        };

        /// <summary>
        /// Khởi tạo đối tượng Training quản lý nhà lính.
        /// </summary>
        /// <param name="adb">Đối tượng ADBHelper.</param>
        /// <param name="templatesPath">Đường dẫn chứa thư mục các template.</param>
        /// <param name="vision">Đối tượng VisionEngine xử lý ảnh.</param>
        public Training(ADBHelper adb, string templatesPath, VisionEngine vision)
        {
            _adb = adb;
            _vision = vision;
            _templateRoot = Path.Combine(templatesPath, "Smart_Auto_train");
        }

        /// <summary>
        /// Thực hiện luyện quân nhanh (Quick Train) thông qua giao diện Đội hình mẫu đã lưu sẵn của game.
        /// </summary>
        /// <param name="quickSlot">Số thứ tự slot đội hình mẫu cần luyện (1 hoặc 2).</param>
        /// <returns>True nếu thực hiện thành công, ngược lại False.</returns>
        public bool QuickTrain(int quickSlot = 1)
        {
            Console.WriteLine($"[quick_train] Bắt đầu dùng Army Recipe slot {quickSlot}...");

            if (!ValidateArmyWindow())
            {
                Console.WriteLine("[quick_train] Army window not detected - aborting");
                return false;
            }

            _adb.Tap(ArmyRecipePane.X, ArmyRecipePane.Y);
            Thread.Sleep(500);

            using Mat? shot = _adb.TakeScreenshot();
            if (shot == null || shot.Empty())
            {
                Console.WriteLine("[quick_train] failed to capture screenshot after opening recipes");
                CloseArmyWindowIfPossible();
                return false;
            }

            Rect slotRoi = quickSlot == 1 ? QuickSlot1Roi : QuickSlot2Roi;
            // Tìm nút 'Train' (use_button) trong slot chỉ định
            if (!TryFindTemplate(shot, "use_button.png", slotRoi, out Point useButton, out double useScore))
            {
                Console.WriteLine("[quick_train] use_button template missing or invalid");
                CloseArmyWindowIfPossible();
                return false;
            }

            Console.WriteLine($"[quick_train] use_button match score = {useScore:F3}");
            if (useScore >= 0.90)
            {
                _adb.Tap(useButton.X, useButton.Y);
                Thread.Sleep(600);

                // Xác nhận lại việc ghi đè/luyện quân nếu xuất hiện popup yêu cầu
                using Mat? confirmShot = _adb.TakeScreenshot();
                if (confirmShot != null && !confirmShot.Empty()
                    && TryFindTemplate(confirmShot, "use_army_recipe_window.png", null, out _, out double confirmScore))
                {
                    Console.WriteLine($"[quick_train] recipe window match score = {confirmScore:F3}");
                    if (confirmScore >= 0.90)
                    {
                        _adb.Tap(ConfirmRecipeUse.X, ConfirmRecipeUse.Y);
                    }
                }
            }

            Thread.Sleep(500);
            CloseArmyWindowIfPossible();
            Thread.Sleep(500);
            return true;
        }

        /// <summary>
        /// Thực hiện quy trình luyện quân thông minh (Smart Train):
        /// 1. Mở giao diện thông tin Quân đội.
        /// 2. So khớp các icon lính, phép, xe xem đã đủ đội hình chiến đấu chưa.
        /// 3. Nếu còn thiếu, tự động mở tab tương ứng để xóa hàng chờ cũ và bấm thêm lính/phép/xe thiếu.
        /// 4. Đóng giao diện thông tin Quân đội.
        /// </summary>
        /// <param name="cfg">Tài liệu JSON chứa cấu hình chiến thuật đang chạy.</param>
        public void SmartTrain(JsonElement cfg)
        {
            Console.WriteLine("\n--- [SMART] Starting Smart Train Sequence ---");

            if (!ValidateArmyWindow())
            {
                Console.WriteLine("[SMART] Army window not detected - skipping Army training");
                return;
            }

            Console.WriteLine("Army window detected");

            // Xác thực tính sẵn sàng của đội hình
            bool armyOk = ValidateTroops(cfg);
            bool spellOk = ValidateSpells();
            bool siegeOk = ValidateSiege();

            if (armyOk && spellOk && siegeOk)
            {
                Console.WriteLine("[SMART] All valid - no training needed");
                CloseArmyWindowIfPossible();
                Thread.Sleep(1000);
                return;
            }

            // Nếu lính chưa đủ, thực hiện luyện lính
            if (!armyOk)
            {
                TrainTroops(cfg);
            }

            // Nếu phép chưa đủ, thực hiện chế tạo phép
            if (!spellOk)
            {
                TrainSpells();
            }

            // Nếu thiếu xe công thành, thực hiện chế tạo xe
            if (!siegeOk)
            {
                TrainSlammer();
            }

            Console.WriteLine("[SMART] Training complete - closing Army tab");
            CloseArmyWindowIfPossible();
            Thread.Sleep(1000);
        }

        /// <summary>
        /// Mở và xác thực xem cửa sổ Quân đội có hiển thị thành công hay không bằng MatchTemplate.
        /// </summary>
        private bool ValidateArmyWindow()
        {
            _adb.Tap(OpenArmyWindow.X, OpenArmyWindow.Y);
            Thread.Sleep(1000);

            using Mat? shot = _adb.TakeScreenshot();
            if (shot == null || shot.Empty())
            {
                Console.WriteLine("[WINDOW CHECK] failed to capture screenshot");
                return false;
            }

            if (!TryFindTemplate(shot, "army_window.png", ArmyWindowRoi, out _, out double score))
            {
                Console.WriteLine("[WINDOW CHECK] missing template or invalid army ROI");
                return false;
            }

            Console.WriteLine($"[WINDOW CHECK] army window match score = {score:F3}");
            return score >= 0.60;
        }

        /// <summary>
        /// Tắt cửa sổ thông tin Quân đội.
        /// </summary>
        private void CloseArmyWindowIfPossible()
        {
            _adb.Tap(CloseArmyWindow.X, CloseArmyWindow.Y);
        }

        /// <summary>
        /// Kiểm tra xem số lượng và chủng loại lính trong doanh trại hiện tại có khớp với chiến thuật cấu hình hay không.
        /// </summary>
        private bool ValidateTroops(JsonElement cfg)
        {
            Console.WriteLine("[SMART] Validating current troops...");

            using Mat? shot = _adb.TakeScreenshot();
            if (shot == null || shot.Empty())
            {
                return false;
            }

            ArmySpec spec = GetArmySpec(cfg);
            using Mat army = Crop(shot, ArmyRoi);
            foreach (string troop in spec.Troops)
            {
                // Thư mục chứa template lính (Rồng điện nằm ở s_troops, Rồng thường/Balloon ở Army Troops)
                string subdir = troop.Equals("electro_dragon", StringComparison.OrdinalIgnoreCase) ? "s_troops" : "Army Troops";
                if (!TryMatch(subdir, troop, army, 0.86, out Point center, out double score))
                {
                    Console.WriteLine($"[VALIDATION] {subdir}/{troop}: score={score:F3}, center=({center.X},{center.Y}) => missing; retraining.");
                    return false;
                }

                Console.WriteLine($"[VALIDATION] {subdir}/{troop}: score={score:F3}, center=({center.X},{center.Y}) => ok");
            }

            Console.WriteLine("[VALIDATION] composition ok");
            return true;
        }

        /// <summary>
        /// Kiểm tra xem lượng phép Cuồng nộ và Đóng băng trong nhà phép có đủ để đánh trận không.
        /// </summary>
        private bool ValidateSpells()
        {
            Console.WriteLine("[SMART] Validating spells...");

            using Mat? shot = _adb.TakeScreenshot();
            if (shot == null || shot.Empty())
            {
                return false;
            }

            using Mat spells = Crop(shot, SpellRoi);
            foreach (string spell in new[] { "rage", "freeze" })
            {
                if (!TryMatch("Spells", spell, spells, 0.86, out Point center, out double score))
                {
                    Console.WriteLine($"[SPELL VALIDATION] Spells/{spell}: score={score:F3}, center=({center.X},{center.Y}) => missing; retraining.");
                    return false;
                }

                Console.WriteLine($"[SPELL VALIDATION] Spells/{spell}: score={score:F3}, center=({center.X},{center.Y}) => ok");
            }

            Console.WriteLine("[SPELL VALIDATION] composition ok");
            return true;
        }

        /// <summary>
        /// Kiểm tra xem xe công thành Stone Slammer có sẵn sàng hay không.
        /// </summary>
        private bool ValidateSiege()
        {
            Console.WriteLine("[SMART] Validating siege machines...");

            using Mat? shot = _adb.TakeScreenshot();
            if (shot == null || shot.Empty())
            {
                return false;
            }

            using Mat siege = Crop(shot, SiegeRoi);
            if (TryMatch("Siege Machines", "slammer", siege, 0.86, out Point center, out double score))
            {
                Console.WriteLine($"[SIEGE] Siege Machines/slammer: score={score:F3}, center=({center.X},{center.Y}) => ok");
                Console.WriteLine("[SIEGE] composition ok");
                return true;
            }

            Console.WriteLine($"[SIEGE] Siege Machines/slammer: score={score:F3}, center=({center.X},{center.Y}) => missing; will rebuild");
            return false;
        }

        /// <summary>
        /// Chẩn đoán hiển thị ảnh lưu sẵn của Quân đội để kiểm tra tính chính xác của các mẫu template khớp ảnh.
        /// </summary>
        public static void DiagnoseSavedArmyWindow(string imagePath, string templatesPath)
        {
            string templateRoot = Path.Combine(templatesPath, "Smart_Auto_train");
            if (!File.Exists(imagePath))
            {
                Console.WriteLine($"[DIAG] saved Army Window image not found: {imagePath}");
                return;
            }

            using Mat shot = Cv2.ImRead(imagePath, ImreadModes.Color);
            if (shot.Empty())
            {
                Console.WriteLine($"[DIAG] saved Army Window image is empty/unreadable: {imagePath}");
                return;
            }

            Console.WriteLine($"[DIAG] Analyzing saved Army Window image: {imagePath}");
            Console.WriteLine($"[DIAG] Image size: {shot.Width}x{shot.Height}");

            VisionEngine vision = new(templatesPath);
            DiagnoseTemplate(shot, templateRoot, vision, ArmyRoi, "Army Troops", "dragon", ValidationIconThreshold);
            DiagnoseTemplate(shot, templateRoot, vision, ArmyRoi, "s_troops", "s_dragon", ValidationIconThreshold);
            DiagnoseTemplate(shot, templateRoot, vision, ArmyRoi, "Army Troops", "balloon", ValidationIconThreshold);
            DiagnoseTemplate(shot, templateRoot, vision, SpellRoi, "Spells", "rage", ValidationIconThreshold);
            DiagnoseTemplate(shot, templateRoot, vision, SpellRoi, "Spells", "freeze", ValidationIconThreshold);
            DiagnoseTemplate(shot, templateRoot, vision, SiegeRoi, "Siege Machines", "slammer", ValidationIconThreshold);
        }

        /// <summary>
        /// Thực hiện quy trình xếp hàng huấn luyện lính mới:
        /// 1. Dọn dẹp hàng chờ cũ (Clear queue) để tránh lính bị kẹt hàng chờ sai cấu hình.
        /// 2. Quét đọc dung lượng sức chứa tối đa của doanh trại.
        /// 3. Tính toán số lượng Rồng/Rồng điện và Balloon tối ưu nhất cho dung lượng đó (theo tỉ lệ 80% lính chính).
        /// 4. Click liên tục các icon lính tương ứng để xếp hàng huấn luyện.
        /// </summary>
        private void TrainTroops(JsonElement cfg)
        {
            ArmySpec spec = GetArmySpec(cfg);
            Console.WriteLine("[TRAIN] Initiating fresh troop training load...");

            ClearQueue(TrashArmyRoi, TapClearArmy, ConfirmTapArmy);

            _adb.Tap(OpenArmyTab.X, OpenArmyTab.Y);
            Thread.Sleep(1000);

            using Mat? shot = _adb.TakeScreenshot();
            // Đọc sức chứa tối đa doanh trại, mặc định 260 nếu không quét được
            int limit = shot == null || shot.Empty() ? 260 : MeasureArmySpace(shot) ?? 260;
            (int mainCount, int balloonCount) = GetExpectedTroopCounts(spec, limit);

            Console.WriteLine($"[TRAIN] {mainCount}x{spec.Main}, {balloonCount}xballoon (limit={limit})");
            TapIconInTab(spec.Main, mainCount);
            TapIconInTab("balloon", balloonCount);

            _adb.Tap(CloseArmyTab.X, CloseArmyTab.Y);
            Thread.Sleep(1000);
            Console.WriteLine("[TRAIN] Troops queued successfully.");
        }

        /// <summary>
        /// Thực hiện quy trình chế tạo phép mới:
        /// 1. Dọn dẹp hàng chờ phép cũ.
        /// 2. Đo sức chứa nhà phép.
        /// 3. Tính toán số lượng phép Cuồng nộ và Đóng băng.
        /// 4. Click chế tạo phép tương ứng.
        /// </summary>
        private void TrainSpells()
        {
            Console.WriteLine("[TRAIN] Initiating spell production load...");

            ClearQueue(TrashSpellRoi, TapClearSpell, ConfirmTapSpell);

            _adb.Tap(OpenSpellTab.X, OpenSpellTab.Y);
            Thread.Sleep(1000);

            int limit = MeasureSpellSpace() ?? 11;
            (int rageCount, int freezeCount) = GetExpectedSpellCounts(limit);

            Console.WriteLine($"[TRAIN] {rageCount}xrage, {freezeCount}xfreeze (limit={limit})");
            TapIconInTab("rage", rageCount);
            TapIconInTab("freeze", freezeCount);

            _adb.Tap(CloseSpellTab.X, CloseSpellTab.Y);
            Thread.Sleep(1000);
            Console.WriteLine("[TRAIN] Spells queued successfully.");
        }

        /// <summary>
        /// Thực hiện chế tạo xe công thành Stone Slammer.
        /// </summary>
        private void TrainSlammer()
        {
            Console.WriteLine("[TRAIN] Queuing Stone Slammer production...");

            ClearQueue(TrashSiegeRoi, TapClearSiege, ConfirmTapSiege);

            _adb.Tap(OpenSiegeTab.X, OpenSiegeTab.Y);
            Thread.Sleep(1000);

            Console.WriteLine("[TRAIN] 3xslammer");
            // Xếp hàng chế tạo tối đa 3 xe
            TapIconInTab("slammer", 3);

            _adb.Tap(CloseSiegeTab.X, CloseSiegeTab.Y);
            Thread.Sleep(1000);
            Console.WriteLine("[TRAIN] Stone Slammer queued.");
        }

        private void ClearQueue(Rect roi, Point tapCoord, Point confirmCoord)
        {
            ClearIfTrash(roi, tapCoord, confirmCoord);
        }

        /// <summary>
        /// Phát hiện xem nút dọn hàng chờ (Trash Icon) có tồn tại hay không. Nếu có, thực hiện nhấp xóa và xác nhận.
        /// </summary>
        private void ClearIfTrash(Rect roi, Point tapCoord, Point confirmCoord)
        {
            using Mat? shot = _adb.TakeScreenshot();
            if (shot == null || shot.Empty())
            {
                return;
            }

            using Mat crop = Crop(shot, roi);
            // Tìm kiếm nút sọt rác màu đỏ
            if (!TryMatch("to_train", "trash_icon", crop, 0.80, out _, out _))
            {
                return;
            }

            Console.WriteLine("[TRASH] cleaning troops...");
            _adb.Tap(tapCoord.X, tapCoord.Y);
            Thread.Sleep(1000);
            _adb.Tap(confirmCoord.X, confirmCoord.Y);
            Thread.Sleep(1000);
        }

        /// <summary>
        /// Thực hiện nhấp liên tục vào icon lính/phép trong tab huấn luyện.
        /// </summary>
        private void TapIconInTab(string name, int count)
        {
            if (count <= 0)
            {
                return;
            }

            using Mat? shot = _adb.TakeScreenshot();
            if (shot == null || shot.Empty())
            {
                return;
            }

            // Dò tìm vị trí của icon lính/phép để chạm chính xác
            if (!TryMatch("to_train", name, shot, 0.70, out Point center, out double score))
            {
                Console.WriteLine($"[TRAIN] {name}.png not found in tab (score={score:F3})");
                return;
            }

            Console.WriteLine($"[TRAIN] {name}: score={score:F3} center=({center.X},{center.Y}) count={count}");
            for (int i = 0; i < count; i++)
            {
                Console.WriteLine($"[TRAIN] tap {i + 1}/{count} -> ({center.X},{center.Y}) score={score:F3}");
                _adb.Tap(center.X, center.Y);
                Thread.Sleep(100); // Tránh chạm quá nhanh khiến hệ thống game phản hồi không kịp
            }
        }

        /// <summary>
        /// Đo đạc dung lượng tối đa của doanh trại.
        /// Thử chạy OCR phân tích văn bản ở vùng hiển thị sức chứa lính.
        /// Nếu thất bại, chuyển sang cơ chế so khớp mẫu dự phòng (MeasureArmySpaceSecondary).
        /// </summary>
        private int? MeasureArmySpace(Mat shot)
        {
            // Trích xuất số lượng bằng bộ nhị phân OCR
            if (_vision.TryExtractNumericalMetrics(shot, SpaceRoi, out int limit, out double confidence, useRgbThresh: true)
                && limit >= 120)
            {
                Console.WriteLine($"[SPACE CHECK] Available space = {limit}, confidence={confidence:F2}");
                return limit;
            }

            Console.WriteLine("Failed to get the correct army space. ");
            return MeasureArmySpaceSecondary(shot);
        }

        /// <summary>
        /// Cơ chế dự phòng đo sức chứa doanh trại bằng cách so khớp mẫu ảnh cứng (e.g. 220, 240, 260, 280, 310, 320, 300, 340).
        /// Dành cho trường hợp OCR đọc số lượng bị mờ/lỗi.
        /// </summary>
        private int? MeasureArmySpaceSecondary(Mat shot)
        {
            using Mat region = Crop(shot, ArmySpaceSecondaryRoi);
            using Mat regionGray = new();
            Cv2.CvtColor(region, regionGray, ColorConversionCodes.BGR2GRAY);

            int[] spaceMap = { 220, 240, 260, 280, 310, 320, 300, 340 };
            int bestIndex = -1;
            double bestScore = -1.0;

            for (int i = 0; i < spaceMap.Length; i++)
            {
                string templatePath = Path.Combine(_templateRoot, $"army_space_{i}.png");
                if (!File.Exists(templatePath))
                {
                    continue;
                }

                using Mat template = Cv2.ImRead(templatePath, ImreadModes.Grayscale);
                if (template.Empty() || template.Width > regionGray.Width || template.Height > regionGray.Height)
                {
                    continue;
                }

                using Mat result = new();
                Cv2.MatchTemplate(regionGray, template, result, TemplateMatchModes.CCoeffNormed);
                Cv2.MinMaxLoc(result, out _, out double maxVal, out _, out _);

                if (maxVal > bestScore)
                {
                    bestScore = maxVal;
                    bestIndex = i;
                }
            }

            if (bestIndex >= 0 && bestScore >= 0.90)
            {
                int space = spaceMap[bestIndex];
                Console.WriteLine($"[SPACE secondary] match=army_space_{bestIndex}  score={bestScore:F3}  => space={space}");
                return space;
            }

            Console.WriteLine($"[SPACE secondary] no confident match (best={bestScore:F3}), skipping.");
            return null;
        }

        private int? MeasureSpellSpace()
        {
            using Mat? shot = _adb.TakeScreenshot();
            if (shot == null || shot.Empty())
            {
                return null;
            }

            return MeasureSpellSpaceFromShot(shot);
        }

        /// <summary>
        /// Đo sức chứa tối đa của nhà phép bằng cách so khớp các template ảnh tương ứng với các sức chứa chuẩn (6, 9, 11).
        /// </summary>
        private int? MeasureSpellSpaceFromShot(Mat shot)
        {
            using Mat spaceImage = Crop(shot, SpellSpaceRoi);
            double bestScore = -1.0;
            int? bestLimit = null;

            foreach (int value in new[] { 6, 9, 11 })
            {
                string templatePath = Path.Combine(_templateRoot, $"Spell_space_{value}.png");
                if (!File.Exists(templatePath))
                {
                    continue;
                }

                using Mat template = Cv2.ImRead(templatePath, ImreadModes.Color);
                if (template.Empty() || template.Width > spaceImage.Width || template.Height > spaceImage.Height)
                {
                    continue;
                }

                using Mat result = new();
                Cv2.MatchTemplate(spaceImage, template, result, TemplateMatchModes.CCoeffNormed);
                Cv2.MinMaxLoc(result, out _, out double maxVal, out _, out _);

                if (maxVal > bestScore)
                {
                    bestScore = maxVal;
                    bestLimit = value;
                }
            }

            if (bestLimit == null || bestScore < 0.85)
            {
                Console.WriteLine($"[SPELL SPACE CHECK] no good match (best={bestScore:F3}), defaulting to 11");
                return 11;
            }

            return bestLimit.Value;
        }

        /// <summary>
        /// Xác thực số lượng lính cụ thể trên ô lính của giao diện thông tin Quân đội.
        /// Chạy OCR đọc nhãn số lượng nằm ở góc của thẻ, tự động chuẩn hóa số đọc được chống nhận diện sai.
        /// </summary>
        private bool ValidateIconCount(Mat shot, Rect sectionRoi, string label, Point centerInSection, int expected)
        {
            if (expected <= 0)
            {
                return true;
            }

            Rect countRoi = CountRoiForIcon(shot, sectionRoi, centerInSection);
            if (!_vision.TryExtractNumericalMetrics(shot, countRoi, out int actual, out double confidence, useRgbThresh: true))
            {
                Console.WriteLine($"[COUNT OCR] {label}: unknown, keeping template fallback");
                return true;
            }

            // Chuẩn hóa chống OCR đọc nhầm ghép số (ví dụ đọc 20 thành 200 hoặc ngược lại)
            int normalized = NormalizeBadgeCount(actual, expected);
            if (normalized != actual)
            {
                Console.WriteLine($"[COUNT OCR] {label}: read={actual}, normalized={normalized}, expected>={expected}, confidence={confidence:F2}");
            }
            else
            {
                Console.WriteLine($"[COUNT OCR] {label}: read={actual}, expected>={expected}, confidence={confidence:F2}");
            }

            if (confidence < 0.58)
            {
                return true;
            }

            if (normalized < expected)
            {
                Console.WriteLine($"[COUNT OCR] {label} missing count - will train fresh load");
                return false;
            }

            return true;
        }

        /// <summary>
        /// Tính toán tọa độ vùng hiển thị số đếm (Badge) của một icon cụ thể dựa trên tọa độ tâm của icon đó.
        /// </summary>
        private static Rect CountRoiForIcon(Mat shot, Rect sectionRoi, Point centerInSection)
        {
            int iconLeft = sectionRoi.X + centerInSection.X - 62;
            int iconTop = sectionRoi.Y + centerInSection.Y - 62;
            Rect rough = new(iconLeft + 24, iconTop + 8, 44, 26);
            return ImageUtils.ClampRect(rough, shot.Width, shot.Height);
        }

        /// <summary>
        /// Sửa lỗi đọc số OCR nhầm của chữ số ở góc thẻ lính (Ví dụ: Số lính thực tế 24 nhưng đọc nhầm sang 240).
        /// Thuật toán kiểm tra giới hạn nghi ngờ và cắt bỏ các chữ số dư thừa nếu cần.
        /// </summary>
        private static int NormalizeBadgeCount(int actual, int expected)
        {
            int normalized = actual;
            int suspiciousLimit = Math.Max(expected + 3, expected * 2);
            while (normalized >= 10 && normalized > suspiciousLimit)
            {
                int digits = normalized.ToString().Length;
                int divisor = (int)Math.Pow(10, digits - 1);
                normalized %= divisor;
            }

            return normalized == 0 ? actual : normalized;
        }

        /// <summary>
        /// Tính toán phân chia số lượng lính chính (Rồng/Rồng điện) và lính phụ Balloon dựa trên tổng sức chứa tối đa.
        /// Công thức: Lính chính chiếm khoảng 80% sức chứa tối đa, phần dư còn lại được lấp đầy bằng Balloon.
        /// </summary>
        private static (int MainCount, int BalloonCount) GetExpectedTroopCounts(ArmySpec spec, int limit)
        {
            int mainCost = SpaceCost[spec.Main];
            int mainSpace = ((limit * 80 / 100) / mainCost) * mainCost;
            int mainCount = mainSpace / mainCost;
            int balloonCount = Math.Max(0, (limit - mainSpace) / SpaceCost["balloon"]);
            return (mainCount, balloonCount);
        }

        /// <summary>
        /// Tính toán phân chia số lượng phép Cuồng nộ và Đóng băng dựa trên sức chứa tối đa của nhà phép.
        /// </summary>
        private static (int RageCount, int FreezeCount) GetExpectedSpellCounts(int limit)
        {
            int primarySpace = ((limit * 80 / 100) / 2) * 2;
            int rageCount = primarySpace / 2;
            int freezeCount = Math.Max(0, limit - primarySpace);

            if (freezeCount > 9)
            {
                freezeCount %= 10;
            }

            return (rageCount, freezeCount);
        }

        private bool TryFindTemplate(Mat screenshot, string templateName, Rect? roi, out Point center, out double score)
        {
            center = default;
            score = 0;

            string templatePath = Path.Combine(_templateRoot, templateName);
            if (!File.Exists(templatePath))
            {
                Console.WriteLine($"[quick_train WARNING] Missing template: {templatePath}");
                return false;
            }

            using Mat template = Cv2.ImRead(templatePath, ImreadModes.Color);
            if (template.Empty())
            {
                return false;
            }

            Rect searchRect = roi ?? new Rect(0, 0, screenshot.Width, screenshot.Height);
            searchRect = ImageUtils.ClampRect(searchRect, screenshot.Width, screenshot.Height);
            if (searchRect.Width < template.Width || searchRect.Height < template.Height)
            {
                return false;
            }

            using Mat searchArea = new(screenshot, searchRect);
            using Mat result = new();
            Cv2.MatchTemplate(searchArea, template, result, TemplateMatchModes.CCoeffNormed);
            Cv2.MinMaxLoc(result, out _, out score, out _, out Point maxLoc);

            center = new Point(
                searchRect.X + maxLoc.X + template.Width / 2,
                searchRect.Y + maxLoc.Y + template.Height / 2
            );
            return true;
        }

        private bool TryMatch(string subdir, string name, Mat haystack, double threshold, out Point center, out double score)
        {
            center = default;
            score = 0;

            string? templatePath = FindTemplatePath(name, subdir);
            if (templatePath == null || haystack.Empty())
            {
                return false;
            }

            using Mat template = Cv2.ImRead(templatePath, ImreadModes.Color);
            if (template.Empty() || template.Width > haystack.Width || template.Height > haystack.Height)
            {
                return false;
            }

            using Mat result = new();
            Cv2.MatchTemplate(haystack, template, result, TemplateMatchModes.CCoeffNormed);
            Cv2.MinMaxLoc(result, out _, out score, out _, out Point maxLoc);

            center = new Point(maxLoc.X + template.Width / 2, maxLoc.Y + template.Height / 2);
            bool matched = score >= threshold;
            string verdict = matched ? "ok" : "low";
            Console.WriteLine($"[TEMPLATE] {subdir}/{name}: score={score:F3}, threshold={threshold:F2}, center=({center.X},{center.Y}) => {verdict}");
            return matched;
        }

        private static void DiagnoseTemplate(Mat shot, string templateRoot, VisionEngine vision, Rect roi, string subdir, string name, double threshold)
        {
            using Mat haystack = Crop(shot, roi);
            string? templatePath = FindTemplatePath(templateRoot, name, subdir);
            if (templatePath == null)
            {
                Console.WriteLine($"[DIAG] {subdir}/{name}: template missing");
                return;
            }

            using Mat template = Cv2.ImRead(templatePath, ImreadModes.Color);
            if (template.Empty() || template.Width > haystack.Width || template.Height > haystack.Height)
            {
                Console.WriteLine($"[DIAG] {subdir}/{name}: template invalid or larger than ROI");
                return;
            }

            using Mat result = new();
            Cv2.MatchTemplate(haystack, template, result, TemplateMatchModes.CCoeffNormed);
            Cv2.MinMaxLoc(result, out _, out double score, out _, out Point maxLoc);

            Point centerInSection = new(maxLoc.X + template.Width / 2, maxLoc.Y + template.Height / 2);
            Point centerAbsolute = new(roi.X + centerInSection.X, roi.Y + centerInSection.Y);
            string verdict = score >= threshold ? "ok" : "low";
            Console.WriteLine($"[DIAG] {subdir}/{name}: score={score:F3}, threshold={threshold:F2}, centerAbs=({centerAbsolute.X},{centerAbsolute.Y}) => {verdict}");

            if (score < 0.60)
            {
                return;
            }

            Rect countRoi = CountRoiForIcon(shot, roi, centerInSection);
            if (vision.TryExtractNumericalMetrics(shot, countRoi, out int actual, out double confidence, useRgbThresh: true))
            {
                Console.WriteLine($"[DIAG COUNT OCR] {name}: read={actual}, confidence={confidence:F2}, roi=({countRoi.X},{countRoi.Y},{countRoi.Width},{countRoi.Height})");
            }
            else
            {
                Console.WriteLine($"[DIAG COUNT OCR] {name}: unknown, roi=({countRoi.X},{countRoi.Y},{countRoi.Width},{countRoi.Height})");
            }

            ScanCountCandidates(shot, roi, centerInSection, vision, name);
        }

        private static void ScanCountCandidates(Mat shot, Rect sectionRoi, Point centerInSection, VisionEngine vision, string name)
        {
            int iconLeft = sectionRoi.X + centerInSection.X - 62;
            int iconTop = sectionRoi.Y + centerInSection.Y - 62;
            var candidates = new List<(int Value, double Confidence, Rect Roi)>();

            for (int y = 0; y <= 100; y += 8)
            {
                for (int x = 0; x <= 86; x += 8)
                {
                    Rect roi = ImageUtils.ClampRect(new Rect(iconLeft + x, iconTop + y, 44, 26), shot.Width, shot.Height);
                    if (roi.Width < 30 || roi.Height < 18)
                    {
                        continue;
                    }

                    if (!vision.TryExtractNumericalMetrics(shot, roi, out int value, out double confidence, useRgbThresh: true))
                    {
                        continue;
                    }

                    if (value > 0 && value <= 80 && confidence >= 0.68)
                    {
                        candidates.Add((value, confidence, roi));
                    }
                }
            }

            var best = candidates
                .OrderByDescending(c => c.Confidence)
                .ThenBy(c => c.Value >= 10 ? 0 : 1)
                .Take(8)
                .ToList();

            if (best.Count == 0)
            {
                Console.WriteLine($"[DIAG COUNT SCAN] {name}: no plausible candidates");
                return;
            }

            string summary = string.Join("; ", best.Select(c => $"{c.Value}@{c.Confidence:F2}/({c.Roi.X},{c.Roi.Y},{c.Roi.Width},{c.Roi.Height})"));
            Console.WriteLine($"[DIAG COUNT SCAN] {name}: {summary}");
        }

        private string? FindTemplatePath(string name, string? subdir = null)
        {
            string fileName = name.EndsWith(".png", StringComparison.OrdinalIgnoreCase) ? name : $"{name}.png";
            string root = subdir == null ? _templateRoot : Path.Combine(_templateRoot, subdir);
            if (!Directory.Exists(root))
            {
                return null;
            }

            return Directory.EnumerateFiles(root, fileName, SearchOption.AllDirectories).FirstOrDefault();
        }

        private static string? FindTemplatePath(string templateRoot, string name, string? subdir = null)
        {
            string fileName = name.EndsWith(".png", StringComparison.OrdinalIgnoreCase) ? name : $"{name}.png";
            string root = subdir == null ? templateRoot : Path.Combine(templateRoot, subdir);
            if (!Directory.Exists(root))
            {
                return null;
            }

            return Directory.EnumerateFiles(root, fileName, SearchOption.AllDirectories).FirstOrDefault();
        }

        private static Mat Crop(Mat image, Rect rect)
        {
            Rect clamped = ImageUtils.ClampRect(rect, image.Width, image.Height);
            return new Mat(image, clamped);
        }

        private static ArmySpec GetArmySpec(JsonElement cfg)
        {
            string attack = "Dragon_Attack";
            if (cfg.ValueKind == JsonValueKind.Object
                && cfg.TryGetProperty("attack", out JsonElement attackElement)
                && attackElement.ValueKind == JsonValueKind.String)
            {
                attack = attackElement.GetString() ?? attack;
            }

            return ArmySets.TryGetValue(attack, out ArmySpec? spec)
                ? spec
                : ArmySets["Dragon_Attack"];
        }

        private sealed record ArmySpec(string Main, string[] Troops, string[] Spells, string Siege);
    }
}
