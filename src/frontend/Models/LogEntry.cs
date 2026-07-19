using System;
using System.Collections.Generic;
using System.Text;

namespace CvAut.Models
{
    public enum LogLevel
    {
        Debug,
        Info,
        Warning,
        Error,
    }

    /// <summary>
    /// One structured log line. Carries <see cref="DeviceId"/> so multi-device logs can be
    /// split into per-device buffers (no shared global log stream).
    /// </summary>
    public sealed class LogEntry
    {
        public LogEntry(string message, LogLevel level = LogLevel.Info, string? deviceId = null)
        {
            Timestamp = DateTimeOffset.Now;
            Message = message;
            Level = level;
            DeviceId = deviceId;
            Fields = ParseFields(message);
            Module = ParseModule(message);
            Phase = GetField("phase");
            Status = GetField("status");
            Action = GetField("action");
            Reason = GetField("reason");
            Step = GetField("step");
        }

        public DateTimeOffset Timestamp { get; }

        public string Message { get; }

        public LogLevel Level { get; }

        /// <summary>Owning device id, or null for app-level lines.</summary>
        public string? DeviceId { get; }

        /// <summary>Short local time for console-style rows.</summary>
        public string TimeText => Timestamp.ToLocalTime().ToString("HH:mm:ss");

        public string LevelText => Level switch
        {
            LogLevel.Debug => "DBG",
            LogLevel.Info => "INF",
            LogLevel.Warning => "WRN",
            LogLevel.Error => "ERR",
            _ => Level.ToString().ToUpperInvariant(),
        };

        public string Module { get; }

        public string? Phase { get; }

        public string? Status { get; }

        public string? Action { get; }

        public string? Reason { get; }

        public string? Step { get; }

        public IReadOnlyDictionary<string, string> Fields { get; }

        /// <summary>
        /// Human-focused summary assembled from the structured key=value fields used by the backend.
        /// Keeps the raw message intact while making the UI much easier to scan during debugging.
        /// </summary>
        public string Summary
        {
            get
            {
                if (Fields.Count == 0)
                {
                    return TranslateRawMessage(Message);
                }

                string? phase = TranslateValue("phase", Phase);
                string? step = TranslateValue("step", Step);
                string? status = TranslateValue("status", Status);
                string? action = TranslateValue("action", Action);
                string? reason = TranslateValue("reason", Reason);
                string? details = TranslateValue("details", GetField("details"));

                var sb = new StringBuilder();

                // 1. Build core action + status sentence
                if (!string.IsNullOrWhiteSpace(action))
                {
                    string act = action.ToLowerInvariant();
                    string stat = status?.ToLowerInvariant() ?? "";

                    if (stat == "bắt đầu" || stat == "start")
                    {
                        sb.Append($"Bắt đầu {act}");
                    }
                    else if (stat == "thành công" || stat == "success" || stat == "hoàn tất" || stat == "ok")
                    {
                        sb.Append($"Đã {act} thành công");
                    }
                    else if (stat == "thất bại" || stat == "fail")
                    {
                        sb.Append($"Thất bại khi {act}");
                    }
                    else if (stat == "đang chờ" || stat == "pending")
                    {
                        sb.Append($"Đang {act}");
                    }
                    else if (stat == "thử lại" || stat == "retry")
                    {
                        sb.Append($"Đang thử lại {act}");
                    }
                    else if (stat == "bỏ qua" || stat == "skip")
                    {
                        sb.Append($"Bỏ qua {act}");
                    }
                    else if (stat == "đã dừng" || stat == "stopped")
                    {
                        sb.Append($"Đã dừng {act}");
                    }
                    else
                    {
                        sb.Append($"{Capitalize(action)}");
                        if (!string.IsNullOrWhiteSpace(status))
                        {
                            sb.Append($" {status}");
                        }
                    }
                }
                else if (!string.IsNullOrWhiteSpace(status))
                {
                    string stat = status.ToLowerInvariant();
                    string ph = phase?.ToLowerInvariant() ?? "";

                    if (stat == "bắt đầu" || stat == "start")
                    {
                        sb.Append(string.IsNullOrWhiteSpace(ph) ? "Bắt đầu tiến trình" : $"Bắt đầu {ph}");
                    }
                    else if (stat == "đang chờ" || stat == "pending")
                    {
                        sb.Append(string.IsNullOrWhiteSpace(ph) ? "Đang chờ" : $"Đang chờ {ph}");
                    }
                    else if (stat == "đã dừng" || stat == "stopped")
                    {
                        sb.Append(string.IsNullOrWhiteSpace(ph) ? "Đã dừng tiến trình" : $"{Capitalize(ph)} đã dừng");
                    }
                    else if (stat == "yêu cầu dừng" || stat == "stop_requested")
                    {
                        sb.Append(string.IsNullOrWhiteSpace(ph) ? "Yêu cầu dừng tiến trình" : $"Yêu cầu dừng {ph}");
                    }
                    else
                    {
                        sb.Append(string.IsNullOrWhiteSpace(ph) ? $"Trạng thái: {status}" : $"{Capitalize(ph)}: {status}");
                    }
                }
                else
                {
                    if (!string.IsNullOrWhiteSpace(phase))
                    {
                        sb.Append(Capitalize(phase));
                    }
                }

                // 2. Append phase context if not already covered as core subject
                if (!string.IsNullOrWhiteSpace(phase) && string.IsNullOrWhiteSpace(action))
                {
                    // Already covered as subject
                }
                else if (!string.IsNullOrWhiteSpace(phase) && !string.IsNullOrWhiteSpace(action))
                {
                    sb.Append($" trong giai đoạn {phase.ToLowerInvariant()}");
                }

                // 3. Append step
                if (!string.IsNullOrWhiteSpace(step))
                {
                    sb.Append($" (bước {step})");
                }

                // 4. Append reason do/vì
                if (!string.IsNullOrWhiteSpace(reason))
                {
                    string r = reason.ToLowerInvariant();
                    if (r.Contains("chưa") || r.Contains("không") || r.Contains("lỗi") || r.Contains("hết giờ") || r.Contains("đã"))
                    {
                        sb.Append($" do {reason}");
                    }
                    else
                    {
                        sb.Append($" vì {reason}");
                    }
                }

                // 5. Append details
                if (!string.IsNullOrWhiteSpace(details))
                {
                    sb.Append($" \u2014 {details}");
                }

                if (sb.Length == 0)
                {
                    return TranslateRawMessage(Message);
                }

                return sb.ToString();
            }
        }

        private static string Capitalize(string s)
        {
            if (string.IsNullOrEmpty(s)) return s;
            return char.ToUpperInvariant(s[0]) + s.Substring(1);
        }

        private static string TranslateKey(string key)
        {
            return key switch
            {
                "phase" => "giai_đoạn",
                "step" => "bước",
                "status" => "trạng_thái",
                "action" => "hành_động",
                "reason" => "lý_do",
                "details" => "chi_tiết",
                _ => key
            };
        }

        private static string? TranslateValue(string key, string? value)
        {
            if (string.IsNullOrWhiteSpace(value)) return value;

            string lower = value.ToLowerInvariant().Trim();
            
            if (key == "status")
            {
                return lower switch
                {
                    "start" => "bắt đầu",
                    "success" => "thành công",
                    "fail" => "thất bại",
                    "pending" => "đang chờ",
                    "retry" => "thử lại",
                    "skip" => "bỏ qua",
                    "ready" => "sẵn sàng",
                    "empty" => "trống",
                    "missing" => "thiếu",
                    "ok" => "hoàn tất",
                    "selected" => "đã chọn",
                    "upgraded" => "đã nâng cấp",
                    "confirmed" => "đã xác nhận",
                    "already_set" => "đã thiết lập",
                    "stopped" => "đã dừng",
                    "stop_requested" => "yêu cầu dừng",
                    _ => value
                };
            }

            if (key == "action")
            {
                return lower switch
                {
                    "scan" => "quét",
                    "quick_deploy" => "thả quân nhanh",
                    "hero_ability" => "chiêu tướng",
                    "deploy_siege" => "thả xe công thành",
                    "start_server" => "khởi động adb",
                    "kill_process" => "đóng tiến trình",
                    "list_devices" => "danh sách thiết bị",
                    "scan_downloads" => "quét thư mục tải",
                    "locate_player" => "tìm giả lập",
                    "locate_conf" => "tìm cấu hình",
                    "set_instance" => "thiết lập instance",
                    "kill_adb" => "tắt adb",
                    "ensure_online" => "kiểm tra kết nối",
                    "wait_online" => "chờ trực tuyến",
                    "fallback_tap" => "nhấn dự phòng",
                    "zoom_out" => "thu nhỏ bản đồ",
                    "find_boat" => "tìm thuyền",
                    "tap_boat" => "nhấn thuyền",
                    "verify_switch" => "xác nhận chuyển làng",
                    _ => value
                };
            }

            if (key == "phase")
            {
                return lower switch
                {
                    "input" => "nhập liệu",
                    "init" => "khởi tạo",
                    "connect" => "kết nối",
                    "uia2" => "UIAutomator2",
                    "screenshot" => "chụp màn hình",
                    "command" => "lệnh adb",
                    "scan_bar" => "quét thanh lính",
                    "deploy" => "thả quân",
                    "attack" => "tấn công",
                    "one_cycle" => "chu kỳ bot",
                    "read_resources" => "đọc tài nguyên",
                    "attempt_upgrade" => "thử nâng cấp",
                    "select_candidate" => "chọn vị trí tường",
                    "add_more" => "thêm tường",
                    "confirm_open" => "mở bảng xác nhận",
                    "ocr_cost" => "đọc giá ocr",
                    "decide" => "quyết định",
                    "validate_tap" => "xác thực nhấn",
                    "train" => "huấn luyện",
                    "smart_train" => "huấn luyện thông minh",
                    "quick_train" => "huấn luyện nhanh",
                    "worker" => "tiến trình chính",
                    "boot" => "khởi động",
                    "configure" => "cấu hình",
                    "switch" => "chuyển làng",
                    "detect" => "nhận diện làng",
                    "entry" => "vào làng đêm",
                    "switch_stage" => "chuyển khu vực làng đêm",
                    "cycle" => "chu kỳ",
                    "find_template" => "tìm mẫu",
                    "template_match" => "khớp mẫu",
                    "pinch" => "thu phóng",
                    _ => value
                };
            }

            if (key == "reason")
            {
                return lower switch
                {
                    "strategy_not_selected" => "chưa chọn chiến thuật",
                    "duplicate" => "trùng lặp",
                    "required_tab_not_found" => "không tìm thấy tab yêu cầu",
                    "quick_drop_unavailable" => "không thể thả quân nhanh",
                    "tab_not_found" => "không tìm thấy tab",
                    "pattern_unavailable" => "không có mẫu nhận diện",
                    "simplicity_gold_then_elixir" => "vàng trước dầu sau",
                    "unsupported_wall_level" => "cấp tường không hỗ trợ",
                    "below_start_threshold" => "dưới ngưỡng bắt đầu",
                    "no_candidates" => "không tìm thấy tường",
                    "unvalidated" => "chưa được xác thực",
                    "confirm_dialog_not_open" => "bảng xác nhận chưa mở",
                    "timeout" => "hết giờ",
                    "decode_fail" => "lỗi giải mã ảnh",
                    "blank" => "màn hình trống",
                    "not_found" => "không tìm thấy",
                    "no_device_detected" => "không phát hiện thiết bị",
                    "bluestacks is not running" => "BlueStacks chưa khởi chạy",
                    "timeout_retrying" => "đang thử lại",
                    "already_there" => "đã ở đúng làng",
                    "main_village_detected" => "phát hiện đang ở làng chính",
                    "switch_to_builder_base_failed" => "chuyển sang làng đêm thất bại",
                    "switch_to_main_village_failed" => "chuyển về làng chính thất bại",
                    "not_detected_after_attempts" => "không nhận diện được sau nhiều lần thử",
                    "template_not_found" => "không tìm thấy mẫu thuyền",
                    "not_on_builder_base" => "không ở làng đêm",
                    "main_village_not_confirmed" => "không xác nhận được làng chính",
                    "builder_base_not_confirmed_after_switch" => "không xác nhận được làng đêm sau khi chuyển",
                    "confirmed_after_switch" => "đã xác nhận làng đêm sau khi chuyển",
                    "tunnel_template_not_found" => "không tìm thấy mẫu đường hầm",
                    "missing_file" => "thiếu tệp mẫu",
                    "pinch_unsupported" => "cử chỉ thu phóng không hỗ trợ",
                    _ => value
                };
            }

            if (key == "details")
            {
                return lower switch
                {
                    "preferred_device_selected" => "đã chọn thiết bị ưu tiên",
                    "device_connected" => "đã kết nối thiết bị",
                    "device_connected_fallback" => "đã kết nối thiết bị dự phòng",
                    "active_device_detected" => "phát hiện thiết bị đang hoạt động",
                    "automation_started" => "bắt đầu tự động hóa",
                    _ => value
                };
            }

            return value;
        }

        private static string TranslateRawMessage(string message)
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

        public string FormattedLevel => $"[{LevelText}]";
        public string FormattedModule => $"[{Module}]";

        public string CleanMessage
        {
            get
            {
                if (Message.StartsWith("[" + Module + "]", StringComparison.Ordinal))
                {
                    return Message.Substring(Module.Length + 2).Trim();
                }
                if (Message.StartsWith("[", StringComparison.Ordinal))
                {
                    int end = Message.IndexOf(']');
                    if (end > 1)
                    {
                        return Message.Substring(end + 1).Trim();
                    }
                }
                return Message;
            }
        }

        public string SearchText => Message + " " + Summary + " " + Module + " " + DeviceId;

        private string? GetField(string key)
            => Fields.TryGetValue(key, out string? value) ? value : null;

        private static string ParseModule(string message)
        {
            if (message.StartsWith("[", StringComparison.Ordinal))
            {
                int end = message.IndexOf(']');
                if (end > 1)
                {
                    return message.Substring(1, end - 1);
                }
            }

            return "APP";
        }

        private static IReadOnlyDictionary<string, string> ParseFields(string message)
        {
            var fields = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            int i = 0;
            while (i < message.Length)
            {
                while (i < message.Length && char.IsWhiteSpace(message[i]))
                {
                    i++;
                }

                int keyStart = i;
                while (i < message.Length && (char.IsLetterOrDigit(message[i]) || message[i] == '_' || message[i] == '-'))
                {
                    i++;
                }

                if (i <= keyStart || i >= message.Length || message[i] != '=')
                {
                    i++;
                    continue;
                }

                string key = message.Substring(keyStart, i - keyStart);
                i++; // '='
                string value;
                if (i < message.Length && message[i] == '"')
                {
                    i++;
                    var sb = new StringBuilder();
                    while (i < message.Length)
                    {
                        char c = message[i++];
                        if (c == '"')
                        {
                            break;
                        }

                        sb.Append(c);
                    }

                    value = sb.ToString();
                }
                else
                {
                    int valueStart = i;
                    while (i < message.Length && !char.IsWhiteSpace(message[i]))
                    {
                        i++;
                    }

                    value = message.Substring(valueStart, i - valueStart);
                }

                if (!string.IsNullOrWhiteSpace(key))
                {
                    fields[key] = value;
                }
            }

            return fields;
        }
    }
}
