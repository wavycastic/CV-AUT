using System;
using System.Collections.Generic;
using System.Globalization;

namespace CvAut.Models
{
    /// <summary>
    /// Vietnamese phrase book for the log viewer. Pure lookup data: it maps the backend's
    /// English field values onto the wording shown in the UI, and rewrites a handful of
    /// unstructured messages that never got key=value fields.
    /// </summary>
    /// <remarks>
    /// Lookups are keyed on the trimmed lowercase value. Anything not listed here falls
    /// through unchanged, so an unmapped value shows up verbatim rather than disappearing.
    /// <para>
    /// Three value shapes cannot be covered by a flat table and get their own handling:
    /// reasons that embed a unit name (<c>troop_missing_dragon</c>) are matched by prefix,
    /// <c>detail</c> values that carry measurements (<c>ocr_low_confidence
    /// confidence=0.00 digits=5</c>) keep everything after the leading token verbatim, and
    /// arbitrary measurement fields are named through <see cref="TranslateFieldLabel"/>
    /// rather than through a per-line sentence.
    /// </para>
    /// </remarks>
    internal static class LogVocabulary
    {
        private static readonly Dictionary<string, string> s_status = new()
        {
            ["start"] = "bắt đầu",
            ["success"] = "thành công",
            ["succeeded"] = "hoàn tất",
            ["complete"] = "hoàn tất",
            ["fail"] = "thất bại",
            ["failed"] = "thất bại",
            ["pending"] = "đang chờ",
            ["retry"] = "thử lại",
            ["skip"] = "bỏ qua",
            ["ready"] = "sẵn sàng",
            ["empty"] = "trống",
            ["missing"] = "thiếu",
            ["ok"] = "hoàn tất",
            ["selected"] = "đã chọn",
            ["upgraded"] = "đã nâng cấp",
            ["confirmed"] = "đã xác nhận",
            ["already_set"] = "đã thiết lập",
            ["stopped"] = "đã dừng",
            ["stop_requested"] = "yêu cầu dừng",
            ["send"] = "gửi",
            ["check"] = "đang kiểm tra",
            ["fallback"] = "dùng phương án dự phòng",
            ["not_found"] = "không tìm thấy",
        };

        private static readonly Dictionary<string, string> s_action = new()
        {
            ["scan"] = "quét",
            ["quick_deploy"] = "thả quân nhanh",
            ["hero_ability"] = "chiêu tướng",
            ["deploy_siege"] = "thả xe công thành",
            ["start_server"] = "khởi động adb",
            ["kill_process"] = "đóng tiến trình",
            ["list_devices"] = "danh sách thiết bị",
            ["scan_downloads"] = "quét thư mục tải",
            ["locate_player"] = "tìm giả lập",
            ["locate_conf"] = "tìm cấu hình",
            ["set_instance"] = "thiết lập instance",
            ["kill_adb"] = "tắt adb",
            ["ensure_online"] = "kiểm tra kết nối",
            ["wait_online"] = "chờ trực tuyến",
            ["fallback_tap"] = "nhấn dự phòng",
            ["zoom_out"] = "thu nhỏ bản đồ",
            ["find_boat"] = "tìm thuyền",
            ["tap_boat"] = "nhấn thuyền",
            ["verify_switch"] = "xác nhận chuyển làng",
            ["check_emulator"] = "kiểm tra giả lập",
            ["prepare_attack"] = "chuẩn bị tấn công",
            ["ensure_home"] = "bảo đảm đã về làng",
            ["continue"] = "nhấn tiếp tục",
        };

        private static readonly Dictionary<string, string> s_phase = new()
        {
            ["input"] = "nhập liệu",
            ["init"] = "khởi tạo",
            ["connect"] = "kết nối",
            ["uia2"] = "UIAutomator2",
            ["screenshot"] = "chụp màn hình",
            ["command"] = "lệnh adb",
            ["scan_bar"] = "quét thanh lính",
            ["deploy"] = "thả quân",
            ["attack"] = "tấn công",
            ["one_cycle"] = "chu kỳ bot",
            ["read_resources"] = "đọc tài nguyên",
            ["attempt_upgrade"] = "thử nâng cấp",
            ["select_candidate"] = "chọn vị trí tường",
            ["add_more"] = "thêm tường",
            ["confirm_open"] = "mở bảng xác nhận",
            ["ocr_cost"] = "đọc giá ocr",
            ["decide"] = "quyết định",
            ["validate_tap"] = "xác thực nhấn",
            ["train"] = "huấn luyện",
            ["smart_train"] = "huấn luyện thông minh",
            ["quick_train"] = "huấn luyện nhanh",
            ["worker"] = "tiến trình chính",
            ["boot"] = "khởi động",
            ["configure"] = "cấu hình",
            ["switch"] = "chuyển làng",
            ["detect"] = "nhận diện làng",
            ["entry"] = "vào làng đêm",
            ["switch_stage"] = "chuyển khu vực làng đêm",
            ["cycle"] = "chu kỳ",
            ["find_template"] = "tìm mẫu",
            ["template_match"] = "khớp mẫu",
            ["pinch"] = "thu phóng",
            ["startup"] = "khởi động ứng dụng",
            ["worker_loop"] = "vòng lặp chính",
            ["home_check"] = "kiểm tra đang ở làng",
            ["camera_zoom"] = "thu phóng màn hình",
            ["calibration"] = "hiệu chuẩn nhịp khung",
            ["check_app"] = "kiểm tra ứng dụng",
            ["launch_app"] = "mở ứng dụng",
            ["collect_resources"] = "thu tài nguyên",
            ["after_collect"] = "sau khi thu tài nguyên",
            ["check"] = "kiểm tra",
            ["scout"] = "tìm làng đối thủ",
            ["scout_wait"] = "chờ tải làng đối thủ",
            ["extract"] = "đọc tài nguyên làng đối thủ",
            ["select_strategy"] = "chọn chiến thuật",
            ["prepare"] = "chuẩn bị tấn công",
            ["pipeline"] = "chuỗi tấn công",
            ["battle_wait"] = "chờ trận đấu",
            ["battle_stats"] = "thống kê trận đấu",
            ["return_home"] = "về làng",
            ["validate"] = "xác thực đội hình",
            ["validate_troops"] = "xác thực lính",
            ["validate_spells"] = "xác thực phép",
            ["validate_siege"] = "xác thực xe công thành",
            ["validate_remaining"] = "đếm quân còn lại",
        };

        /// <summary>Attack pipeline stages, reported in the <c>stage</c> field.</summary>
        private static readonly Dictionary<string, string> s_stage = new()
        {
            ["preparation"] = "chuẩn bị",
            ["troop_deployment"] = "thả lính",
            ["hero_ability"] = "chiêu tướng",
            ["spell_deployment"] = "thả phép",
            ["battle_completion"] = "kết thúc trận",
        };

        /// <summary>
        /// Unit names. Used both for the <c>item</c> field and for the unit embedded in
        /// missing-unit reasons. Values that are not unit names (a template path, for
        /// example) fall through unchanged.
        /// </summary>
        private static readonly Dictionary<string, string> s_unit = new()
        {
            ["dragon"] = "rồng",
            ["electro_dragon"] = "rồng điện",
            ["balloon"] = "bóng bay",
            ["rage"] = "phép cuồng nộ",
            ["freeze"] = "phép đóng băng",
            ["slammer"] = "búa công thành",
            ["siege_machine"] = "xe công thành",
        };

        /// <summary>
        /// Module tags, as they appear between the leading brackets of a log line. Without
        /// this the user cannot tell which subsystem produced a row.
        /// </summary>
        private static readonly Dictionary<string, string> s_module = new(StringComparer.OrdinalIgnoreCase)
        {
            ["APP"] = "Ứng dụng",
            ["APP_LOG"] = "Ứng dụng",
            ["FSM-CS"] = "Bot",
            ["DEBUG"] = "Gỡ lỗi",
            ["ADB"] = "Giả lập",
            ["TRAIN"] = "Huấn luyện",
            ["ATTACK-CS"] = "Tấn công",
            ["SCOUT-CS"] = "Tìm làng",
            ["BB-CS"] = "Làng đêm",
            ["VISION"] = "Thị giác",
            ["WALL"] = "Tường",
            ["WALL DECISION"] = "Tường (quyết định)",
            ["WALL RESULT"] = "Tường (kết quả)",
            ["WALL-MV"] = "Tường Làng Chính",
            ["TREASURE HUNT"] = "Kho báu",
            ["FRAME-PACER"] = "Nhịp khung",
            ["CONFIG-CS"] = "Cấu hình",
            ["LICENSE"] = "Bản quyền",
        };

        /// <summary>
        /// Names for every measurement field the backend logs. These used to be dropped
        /// entirely, which hid exactly the numbers a user watches during a run.
        /// </summary>
        private static readonly Dictionary<string, string> s_fieldLabels = new(StringComparer.OrdinalIgnoreCase)
        {
            ["stars"] = "số sao",
            ["gold"] = "vàng",
            ["elixir"] = "dầu",
            ["dark_elixir"] = "dầu đen",
            ["total"] = "tổng tài nguyên",
            ["total_ok"] = "đủ dữ liệu",
            ["trophies"] = "cúp",
            ["score"] = "điểm khớp",
            ["threshold"] = "ngưỡng",
            ["best_scale"] = "tỉ lệ tốt nhất",
            ["best_scale_score"] = "điểm ở tỉ lệ tốt nhất",
            ["confidence"] = "độ tin cậy",
            ["remaining"] = "còn lại",
            ["tap_count"] = "số lần nhấn",
            ["taps"] = "số lần nhấn",
            ["total_taps"] = "tổng số lần nhấn",
            ["attempt"] = "lần thử",
            ["attempts"] = "số lần thử",
            ["index"] = "vị trí",
            ["slot"] = "ô lính",
            ["count"] = "số lượng",
            ["current"] = "hiện tại",
            ["capacity"] = "sức chứa",
            ["level"] = "cấp",
            ["village"] = "làng",
            ["mode"] = "chế độ",
            ["strategy"] = "chiến thuật",
            ["troop"] = "lính",
            ["spell"] = "phép",
            ["unit"] = "quân",
            ["name"] = "tên",
            ["template"] = "ảnh mẫu",
            ["duration"] = "thời lượng",
            ["duration_ms"] = "thời lượng (ms)",
            ["elapsed_ms"] = "đã trôi qua (ms)",
            ["baseline_ms"] = "nhịp nền (ms)",
            ["wait_ms"] = "chờ (ms)",
            ["timeout_ms"] = "giới hạn chờ (ms)",
            ["attacks"] = "số trận",
            ["candidates"] = "số vị trí ứng viên",
            ["cost"] = "chi phí",
            ["x"] = "x",
            ["y"] = "y",
            ["x1"] = "từ x",
            ["y1"] = "từ y",
            ["x2"] = "đến x",
            ["y2"] = "đến y",
            ["device"] = "thiết bị",
            ["deviceid"] = "thiết bị",
            ["port"] = "cổng",
            ["package"] = "ứng dụng",
            ["file"] = "tệp",
            ["path"] = "đường dẫn",
            ["run_id"] = "mã lượt chạy",
            ["trigger"] = "điều kiện kích hoạt",
            ["batch_budget"] = "ngân sách mẻ",
            ["batch_limit"] = "giới hạn mẻ",
            ["gold_threshold"] = "ngưỡng vàng",
            ["elixir_threshold"] = "ngưỡng dầu",
            ["gold_reserve"] = "dự trữ vàng",
            ["elixir_reserve"] = "dự trữ dầu",
            ["dark_ratio"] = "tỉ lệ vùng tối",
            ["raw_count"] = "số lượng ban đầu",
            ["dedupe_count"] = "số lượng sau lọc",
            ["panel_open"] = "bảng nâng tường mở",
            ["white_panel"] = "vùng viền trắng",
            ["gold_avail"] = "nút vàng khả dụng",
            ["elixir_avail"] = "nút dầu khả dụng",
            ["raw_gold_cost"] = "giá vàng ocr",
            ["raw_elixir_cost"] = "giá dầu ocr",
            ["affordable_gold"] = "số tường mua được bằng vàng",
            ["affordable_elixir"] = "số tường mua được bằng dầu",
            ["requested_count"] = "số lượng yêu cầu",
            ["selected_count"] = "số lượng đã chọn",
            ["roi_diff"] = "độ lệch roi",
            ["red_pixels"] = "điểm ảnh đỏ",
            ["red_ratio"] = "tỉ lệ điểm ảnh đỏ",
            ["dialog_brightness"] = "độ sáng bảng xác nhận",
            ["dialog_open"] = "bảng xác nhận mở",
            ["dialog_closed"] = "bảng xác nhận đã đóng",
            ["expected_spend"] = "chi phí dự kiến",
            ["actual_spend"] = "chi phí thực tế",
            ["attempted_unverified"] = "đã thử nhưng chưa xác thực",
        };

        /// <summary>
        /// Fields whose values are large amounts and are unreadable without grouping.
        /// </summary>
        private static readonly HashSet<string> s_groupedNumberFields = new(StringComparer.OrdinalIgnoreCase)
        {
            "gold", "elixir", "dark_elixir", "total", "cost", "trophies",
        };

        /// <summary>Fields whose value names a troop, spell or siege machine.</summary>
        private static readonly HashSet<string> s_unitValuedFields = new(StringComparer.OrdinalIgnoreCase)
        {
            "troop", "spell", "unit", "siege", "item",
        };

        /// <summary>
        /// Vietnamese grouping for amounts: <c>368207</c> reads as <c>368.207</c>. Built by
        /// hand rather than taken from the current culture, so the log looks the same on
        /// every machine and under invariant globalization.
        /// </summary>
        private static readonly NumberFormatInfo s_amountFormat = new()
        {
            NumberGroupSeparator = ".",
            NumberDecimalSeparator = ",",
            NumberGroupSizes = new[] { 3 },
        };

        private static readonly Dictionary<string, string> s_reason = new()
        {
            ["strategy_not_selected"] = "chưa chọn chiến thuật",
            ["duplicate"] = "trùng lặp",
            ["required_tab_not_found"] = "không tìm thấy tab yêu cầu",
            ["quick_drop_unavailable"] = "không thể thả quân nhanh",
            ["tab_not_found"] = "không tìm thấy tab",
            ["pattern_unavailable"] = "không có mẫu nhận diện",
            ["simplicity_gold_then_elixir"] = "vàng trước dầu sau",
            ["unsupported_wall_level"] = "cấp tường không hỗ trợ",
            ["below_start_threshold"] = "dưới ngưỡng bắt đầu",
            ["no_candidates"] = "không tìm thấy tường",
            ["unvalidated"] = "chưa được xác thực",
            ["confirm_dialog_not_open"] = "bảng xác nhận chưa mở",
            ["timeout"] = "hết giờ",
            ["decode_fail"] = "lỗi giải mã ảnh",
            ["blank"] = "màn hình trống",
            ["not_found"] = "không tìm thấy",
            ["no_device_detected"] = "không phát hiện thiết bị",
            ["bluestacks is not running"] = "BlueStacks chưa khởi chạy",
            ["timeout_retrying"] = "đang thử lại",
            ["already_there"] = "đã ở đúng làng",
            ["main_village_detected"] = "phát hiện đang ở làng chính",
            ["switch_to_builder_base_failed"] = "chuyển sang làng đêm thất bại",
            ["switch_to_main_village_failed"] = "chuyển về làng chính thất bại",
            ["not_detected_after_attempts"] = "không nhận diện được sau nhiều lần thử",
            ["template_not_found"] = "không tìm thấy mẫu thuyền",
            ["not_on_builder_base"] = "không ở làng đêm",
            ["main_village_not_confirmed"] = "không xác nhận được làng chính",
            ["builder_base_not_confirmed_after_switch"] = "không xác nhận được làng đêm sau khi chuyển",
            ["confirmed_after_switch"] = "đã xác nhận làng đêm sau khi chuyển",
            ["tunnel_template_not_found"] = "không tìm thấy mẫu đường hầm",
            ["missing_file"] = "thiếu tệp mẫu",
            ["pinch_unsupported"] = "cử chỉ thu phóng không hỗ trợ",
            ["below_threshold"] = "điểm nhận diện dưới ngưỡng",
            ["score_below_threshold"] = "điểm khớp dưới ngưỡng",
            ["disabled"] = "đã tắt trong cấu hình",
            ["already_foreground"] = "ứng dụng đang mở sẵn",
            ["home_detected"] = "đã nhận diện được làng",
            ["total"] = "xét theo tổng tài nguyên",
            ["screenshot_empty"] = "không chụp được màn hình",
            ["army_window_not_detected"] = "không nhận diện được cửa sổ quân",
            ["army_space_unreadable"] = "không đọc được ô sức chứa quân",
            ["army_space_not_full"] = "doanh trại chưa đầy",
            ["spell_space_unreadable"] = "không đọc được ô sức chứa phép",
            ["spell_space_not_full"] = "nhà phép chưa đầy",
            ["troop_missing"] = "thiếu lính",
            ["screenshot_failed"] = "chụp màn hình thất bại",
            ["template_missing"] = "thiếu ảnh mẫu",
        };

        private static readonly Dictionary<string, string> s_details = new()
        {
            ["preferred_device_selected"] = "đã chọn thiết bị ưu tiên",
            ["device_connected"] = "đã kết nối thiết bị",
            ["device_connected_fallback"] = "đã kết nối thiết bị dự phòng",
            ["active_device_detected"] = "phát hiện thiết bị đang hoạt động",
            ["automation_started"] = "bắt đầu tự động hóa",
            ["automation_core_initialized"] = "đã khởi tạo lõi tự động hóa",
            ["single_account"] = "chế độ một tài khoản",
            ["initial_zoomout"] = "thu nhỏ bản đồ lần đầu",
            ["bluestacks_detected"] = "đã phát hiện BlueStacks",
            ["bluestacks_adb_pinch"] = "thu phóng bằng adb",
            ["already_foreground"] = "ứng dụng đang mở sẵn",
            ["running"] = "đang chạy",
            ["connected"] = "đã kết nối",
            ["loading"] = "đang tải",
            ["ready"] = "đã sẵn sàng",
            ["waiting"] = "đang chờ",
            ["collecting_resources"] = "đang thu tài nguyên",
            ["result_screen_detected"] = "đã thấy bảng kết quả",
            ["target"] = "làng mục tiêu",
            ["target_accepted"] = "đã chấp nhận làng mục tiêu",
        };

        /// <summary>
        /// Leading tokens of the <c>detail</c> field. These come from the template matcher
        /// and the fraction reader, and each one names a distinct failure cause.
        /// </summary>
        private static readonly Dictionary<string, string> s_detail = new()
        {
            ["matched"] = "đã khớp",
            ["image_empty"] = "ảnh chụp rỗng",
            ["template_file_missing"] = "thiếu tệp ảnh mẫu",
            ["template_unreadable"] = "không đọc được tệp ảnh mẫu",
            ["roi_smaller_than_template"] = "vùng quét nhỏ hơn ảnh mẫu",
            ["score_below_threshold"] = "điểm khớp dưới ngưỡng",
            ["read"] = "đã đọc được",
            ["ocr_no_result"] = "OCR không đọc ra số nào",
            ["ocr_low_confidence"] = "OCR đọc được nhưng không đủ tin cậy",
            ["ocr_too_few_digits"] = "OCR ra quá ít chữ số",
            ["ocr_odd_digit_count"] = "OCR ra số chữ số lẻ nên không tách được hai số",
            ["split_failed"] = "không tách được hai số",
        };

        /// <summary>
        /// Translates one field value. <paramref name="key"/> selects the table; unknown keys
        /// and unmapped values are returned untouched.
        /// </summary>
        public static string? Translate(string key, string? value)
        {
            if (string.IsNullOrWhiteSpace(value)) return value;

            string lower = value.ToLowerInvariant().Trim();

            if (key == "reason")
            {
                string? missingUnit = TranslateMissingUnitReason(lower);
                if (missingUnit is not null) return missingUnit;
            }

            var table = key switch
            {
                "status" => s_status,
                "action" => s_action,
                "phase" => s_phase,
                "stage" => s_stage,
                "item" => s_unit,
                "reason" => s_reason,
                "details" => s_details,
                _ => null
            };

            if (table is null) return value;

            return table.TryGetValue(lower, out string? translated) ? translated : value;
        }

        /// <summary>
        /// Names the subsystem a log line came from. Tags such as <c>FSM-CS WARNING</c> are
        /// resolved by falling back to the leading word, so a severity suffix does not lose
        /// the module name.
        /// </summary>
        public static string TranslateModule(string? module)
        {
            if (string.IsNullOrWhiteSpace(module)) return "Ứng dụng";

            string trimmed = module.Trim();
            if (s_module.TryGetValue(trimmed, out string? direct)) return direct;

            int space = trimmed.LastIndexOf(' ');
            if (space > 0)
            {
                string head = trimmed.Substring(0, space);
                string tail = trimmed.Substring(space + 1).ToLowerInvariant();
                string headLabel = s_module.TryGetValue(head, out string? mapped) ? mapped : head;

                string? tailLabel = tail switch
                {
                    "warning" => "cảnh báo",
                    "error" => "lỗi",
                    "info" => null,
                    _ => null
                };

                if (tailLabel is not null) return headLabel + " (" + tailLabel + ")";
                if (s_module.ContainsKey(head)) return headLabel;
            }

            return trimmed;
        }

        /// <summary>
        /// Names a measurement field. Returns null for unknown keys so the caller can show
        /// the raw key instead of hiding the value.
        /// </summary>
        public static string? TranslateFieldLabel(string key)
            => s_fieldLabels.TryGetValue(key, out string? label) ? label : null;

        /// <summary>
        /// Formats a field value for reading: resource amounts get thousands separators,
        /// booleans become có/không, and unit-valued fields get their Vietnamese name.
        /// Anything else is returned untouched, because in a diagnostic line the raw value
        /// is the evidence.
        /// </summary>
        public static string FormatFieldValue(string key, string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return value;

            string trimmed = value.Trim();

            if (s_groupedNumberFields.Contains(key) &&
                long.TryParse(trimmed, NumberStyles.Integer, CultureInfo.InvariantCulture, out long amount) &&
                Math.Abs(amount) >= 1000)
            {
                return amount.ToString("#,##0", s_amountFormat);
            }

            if (s_unitValuedFields.Contains(key) &&
                s_unit.TryGetValue(trimmed.ToLowerInvariant(), out string? unit))
            {
                return unit;
            }

            if (string.Equals(trimmed, "true", StringComparison.OrdinalIgnoreCase)) return "có";
            if (string.Equals(trimmed, "false", StringComparison.OrdinalIgnoreCase)) return "không";

            return trimmed;
        }

        /// <summary>
        /// Expands reasons such as <c>troop_missing_dragon</c>, where the unit name is part
        /// of the token. Returns null when the reason is not of that shape.
        /// </summary>
        private static string? TranslateMissingUnitReason(string reason)
        {
            foreach ((string prefix, string label) in s_missingUnitReasons)
            {
                if (reason.Length > prefix.Length && reason.StartsWith(prefix, StringComparison.Ordinal))
                {
                    string unit = reason.Substring(prefix.Length);
                    string unitText = s_unit.TryGetValue(unit, out string? mapped) ? mapped : unit;
                    return label + " " + unitText;
                }
            }

            return null;
        }

        /// <summary>
        /// Reason prefixes that carry a unit name after the underscore. A flat table can
        /// never match these, because the tail is data rather than vocabulary.
        /// </summary>
        private static readonly (string Prefix, string Label)[] s_missingUnitReasons =
        {
            ("troop_missing_", "thiếu lính"),
            ("spell_missing_", "thiếu"),
            ("siege_missing_", "thiếu"),
        };

        /// <summary>
        /// Translates the <c>detail</c> field, which is a diagnostic token optionally followed
        /// by its own measurements (<c>ocr_low_confidence confidence=0.00 digits=5</c>). Only
        /// the leading token is translated: the numbers are the evidence and must survive
        /// untouched. An unknown token leaves the whole value alone.
        /// </summary>
        public static string? TranslateDetail(string? value)
        {
            if (string.IsNullOrWhiteSpace(value)) return value;

            string trimmed = value.Trim();
            int split = trimmed.IndexOf(' ');
            string head = split < 0 ? trimmed : trimmed.Substring(0, split);

            if (!s_detail.TryGetValue(head.ToLowerInvariant(), out string? translated))
            {
                return value;
            }

            return split < 0 ? translated : translated + " " + trimmed.Substring(split + 1);
        }

        /// <summary>
        /// Rewrites the few backend messages that carry no key=value fields at all.
        /// </summary>
        public static string TranslateRawMessage(string message)
        {
            if (string.IsNullOrWhiteSpace(message)) return message;

            string lower = message.ToLowerInvariant();
            if (lower.Contains("failed to read bluestacks ports"))
                return "[CẢNH BÁO ADB] Lỗi đọc cổng BlueStacks từ tệp cấu hình.";
            if (lower.Contains("screenshot failed"))
                return "[LỖI] Chụp màn hình giả lập thất bại.";
            if (lower.Contains("emulator host not set"))
                return "[LỖI] Chưa thiết lập host của giả lập để ẩn cửa sổ.";
            if (lower.Contains("no generic wall templates found"))
                return "[CẢNH BÁO] Không tìm thấy mẫu ảnh tường trong thư mục Templates.";
            if (lower.Contains("validation failed"))
                return "[CẢNH BÁO] Xác thực thất bại -> đang nhấn vùng an toàn và thử lại.";
            if (lower.Contains("legacy_config_migrated"))
                return "[CẤU HÌNH] Đã di trú cấu hình cũ của tường thành công.";

            return message;
        }
    }
}
