using System.Collections.Generic;

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
    /// </remarks>
    internal static class LogVocabulary
    {
        private static readonly Dictionary<string, string> s_status = new()
        {
            ["start"] = "bắt đầu",
            ["success"] = "thành công",
            ["fail"] = "thất bại",
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
        };

        private static readonly Dictionary<string, string> s_details = new()
        {
            ["preferred_device_selected"] = "đã chọn thiết bị ưu tiên",
            ["device_connected"] = "đã kết nối thiết bị",
            ["device_connected_fallback"] = "đã kết nối thiết bị dự phòng",
            ["active_device_detected"] = "phát hiện thiết bị đang hoạt động",
            ["automation_started"] = "bắt đầu tự động hóa",
        };

        /// <summary>
        /// Translates one field value. <paramref name="key"/> selects the table; unknown keys
        /// and unmapped values are returned untouched.
        /// </summary>
        public static string? Translate(string key, string? value)
        {
            if (string.IsNullOrWhiteSpace(value)) return value;

            var table = key switch
            {
                "status" => s_status,
                "action" => s_action,
                "phase" => s_phase,
                "reason" => s_reason,
                "details" => s_details,
                _ => null
            };

            if (table is null) return value;

            string lower = value.ToLowerInvariant().Trim();
            return table.TryGetValue(lower, out string? translated) ? translated : value;
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
