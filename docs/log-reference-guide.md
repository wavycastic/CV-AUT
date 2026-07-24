# Quy ước Logging Hệ thống CV-AUT (Logging Contract & Reference Guide)

Tài liệu này là **Quy ước Logging chính thức (Logging Contract)** cho dự án CV-AUT. Chuẩn hóa phân tầng giữa **Nhật ký kỹ thuật (Dev Log)** và **Nhật ký hoạt động (User Log)**, quy định cấu trúc dữ liệu, mã sự kiện, cấp độ log và cơ chế chống spam log.

---

## 1. Phân tầng Cấp độ Log (Log Severity Levels)

| Level | Ký hiệu | Khi nào sử dụng | Ví dụ |
| --- | --- | --- | --- |
| `Trace` | `🔍` | Dữ liệu cực chi tiết từng lần poll, từng template candidate, score, tọa độ x,y, ROI. | Candidate check fail, match score 0.65. |
| `Debug` | `🐞` | Kết quả trung gian có giá trị điều tra: detector nào được chọn, fallback nào được dùng. | `action=detect_village status=succeeded detector=night_palette`. |
| `Information` | `✓` / `ℹ` / `●` / `■` | Mốc hoạt động bình thường của hệ thống. | `[GAME] action=launch status=succeeded`, bắt đầu chu kỳ. |
| `Warning` | `⚠` / `⏭` | Có bất thường nhưng hệ thống đã thử lại (`retrying`) hoặc bước tùy chọn bị bỏ qua (`skipped`). | Chụp màn hình bị trống thử lại (1/3), bỏ qua Xe tiên dược. |
| `Error` | `✕` | Một thao tác (Operation) thất bại hoàn toàn sau khi đã hết số lần retry/fallback. | Không thể chuyển sang Làng đêm sau 5.5s timeout. |
| `Critical` | `🛑` | Sự cố nghiêm trọng khiến phiên bot không thể tiếp tục chạy. | ADB hỏng hoàn toàn, không thể kết nối tới giả lập. |

---

## 2. Chuẩn hóa Cấu trúc & Trường Dữ liệu Dev Log (Structured Logging)

Mỗi sự kiện Dev Log được phát ra phải tuân theo cấu trúc phẳng Key-Value hoặc JSON chuẩn:

### 2.1. Tập từ vựng Trạng thái chuẩn (`status`)
Không gộp `success=true`, `found`, `_done` hay `fail`. Chỉ sử dụng tập giá trị chuẩn:
- `started`: Bắt đầu tiến trình.
- `pending`: Đang xử lý.
- `retrying`: Thử lại sau sự cố nhẹ.
- `succeeded`: Thực thi thành công hoàn toàn.
- `partially_succeeded`: Thực thi hoàn tất nhưng có bước tùy chọn bị lỗi/bỏ qua.
- `skipped`: Bị bỏ qua do không đủ điều kiện hoặc thiếu ảnh nhận diện.
- `failed`: Thất bại hoàn toàn sau retry.
- `cancelled`: Bị hủy theo yêu cầu người dùng.

### 2.2. Danh mục Component chuẩn (`component`)
Mọi log phải thuộc một trong các component hệ thống:
- `[APP]`: Tiến trình chính ứng dụng, cấu hình, quản lý phiên.
- `[EMULATOR]`: Khởi động/tắt giả lập BlueStacks, đọc cổng.
- `[ADB]`: Kết nối ADB, chụp màn hình, gửi input tap/swipe.
- `[GAME]`: Mở/đóng ứng dụng Clash of Clans.
- `[VISION]`: Khớp mẫu (Match Template), OCR, kiểm tra asset.
- `[BUILDER_BASE]`: Điều hướng, di chuyển, thu thập, nâng cấp Làng đêm.
- `[BATTLE]`: Chuẩn bị quân, tìm trận, thả quân tự động.
- `[ACCOUNT]`: Quản lý và chuyển đổi tài khoản Supercell ID.
- `[CYCLE]`: Quản lý vòng lặp chu kỳ FSM.

### 2.3. Các trường truy vết bắt buộc (Metadata Fields)
```ini
[BUILDER_BASE] event_id=BUILDER_BASE_SWITCH_VERIFY_FAILED session_id=session-20260724-001 cycle_id=cycle-002 operation_id=switch-builder-base-001 component=builder_base phase=navigation action=verify_destination status=failed reason=verification_timeout timeout_ms=5500 attempt=1 device=127.0.0.1:5556
```

---

## 3. Quy tắc Severity & Phản ánh Kết quả Thao tác

1. **Nguyên tắc Retry**:
   - Khi `status=retrying` (đang thử lại lần `1/3`, `2/3`): Mức log ghi `Warning` (chưa phải Error).
   - Khi hết mọi lượt thử mà vẫn thất bại (`status=failed`): Mức log ghi `Error` (`✕`).
   - Nếu `status=failed` khiến toàn bộ bot phải dừng phiên: Ghi log `Critical` (`🛑`) ở cấp phiên chạy.

2. **Chuẩn hóa Kết quả Chu kỳ (`[CYCLE]`)**:
   - `succeeded`: Tất cả bước bắt buộc thành công; không có bước yêu cầu nào thất bại.
   - `partially_succeeded`: Tất cả bước bắt buộc thành công; có bước tùy chọn bị skip/fail.
   - `failed`: Ít nhất một bước bắt buộc thất bại.
   - `cancelled`: Chu kỳ bị dừng theo yêu cầu trước khi hoàn thành.

---

## 4. Nguyên tắc Gom Log & Chống Spam Log (Anti-Spam Rules)

1. **Candidate Failure**: Mọi lỗi thiếu/không khớp từng candidate template cá thể chỉ được ghi ở mức `Trace` hoặc `Debug`.
2. **Aggregated User Log**: User Log chỉ ghi **1 dòng tổng hợp** duy nhất sau khi toàn bộ vòng lặp candidate / retry hoàn tất.
3. **Cơ chế De-duplication**: Cùng bộ `event_id + reason + operation_id` chỉ hiển thị **tối đa 1 dòng** trên User Log trong cùng 1 chu kỳ.

---

## 5. Danh mục Chi tiết các Nhóm Log (Logging Contract Catalog)

### 5.1. Khởi động Giả lập & Kết nối ADB (`[EMULATOR]`, `[ADB]`, `[GAME]`)

| Dev Log (Structured) | User Log (Formatted) | Ý nghĩa & Hướng dẫn xử lý |
| :--- | :--- | :--- |
| `[EMULATOR] phase=startup action=start_instance status=started instance=Pie64` | `● Đang khởi động BlueStacks (Pie64)…` | Đang mở tiến trình giả lập. |
| `[EMULATOR] phase=startup action=start_instance status=succeeded duration_ms=12611` | `✓ BlueStacks đã sẵn sàng` | Giả lập đã khởi động xong. |
| `[ADB] phase=connect action=connect status=retrying reason=connection_refused attempt=1 max_attempts=5` | `ℹ ADB chưa sẵn sàng; bot đang thử lại (1/5)…` | ADB Server chưa phản hồi, bot đang thử lại. |
| `[ADB] phase=connect action=connect status=succeeded device=127.0.0.1:5556` | `✓ Đã kết nối giả lập` | ADB đã kết nối thiết bị. |
| `[GAME] phase=startup action=launch status=succeeded package=com.supercell.clashofclans` | `✓ Clash of Clans đã được mở` | Đã khởi chạy thành công game. |
| `[ADB] phase=device_check action=validate_screen status=failed actual_width=1920 actual_height=1080 actual_dpi=240 expected_width=1600 expected_height=900 expected_dpi=300` | `⚠ Cấu hình màn hình không tương thích: hiện tại 1920×1080/240 DPI, yêu cầu 1600×900/300 DPI` | Độ phân giải bị sai chuẩn. **Khuyến nghị**: Đổi độ phân giải giả lập về 1600x900 / 300 DPI. |

### 5.2. Nhận diện Làng & Điều hướng (`[BUILDER_BASE]`)

| Dev Log (Structured) | User Log (Formatted) | Ý nghĩa & Hướng dẫn xử lý |
| :--- | :--- | :--- |
| `[BUILDER_BASE] phase=navigation action=detect_village status=succeeded village=builder_base detector=night_palette` | `✓ Đã xác định đang ở Làng đêm` | Xác định vị trí Làng đêm thành công. |
| `[BUILDER_BASE] phase=navigation action=detect_village status=retrying reason=no_detector_matched attempt=1` | `⚠ Không xác định được làng hiện tại; đang thử lại` | Chưa tìm thấy dấu hiệu làng, đang thử lại. |
| `[BUILDER_BASE] phase=navigation action=detect_village status=failed reason=no_detector_matched attempts=3` | `✕ Không thể xác định làng hiện tại sau 3 lần thử` | Thất bại nhận diện vị trí làng. |
| `[BUILDER_BASE] phase=navigation action=switch_village status=started target=builder_base` | `● Đang chuyển sang Làng đêm…` | Đang nhấn nút sang Làng đêm. |
| `[BUILDER_BASE] phase=navigation action=verify_destination status=failed reason=verification_timeout timeout_ms=5500` | `✕ Không xác nhận được việc chuyển sang Làng đêm sau 5,5 giây` | Hết thời gian chờ xác minh chuyển làng. **Khuyến nghị**: Kiểm tra trạng thái game sau khi nhấn, ảnh nhận diện Làng đêm, ảnh chụp chẩn đoán và thời gian chờ chuyển làng. |

### 5.3. Thu thập Tài nguyên & Nâng cấp Tường (`[BUILDER_BASE]`)

| Dev Log (Structured) | User Log (Formatted) | Ý nghĩa & Hướng dẫn xử lý |
| :--- | :--- | :--- |
| `[BUILDER_BASE] phase=collect_resources action=detect status=skipped feature=elixir_cart reason=no_candidate_available missing_count=11` | `⏭ Bỏ qua Xe tiên dược vì thiếu bộ ảnh nhận diện` | Không có file nhận diện Xe tiên dược. |
| `[BUILDER_BASE] phase=collect_resources action=tap status=succeeded item=elixir_cart x=421 y=667` | `✓ Đã nhấn thu thập Xe tiên dược` | Đã nhận diện và nhấn thu thập Xe tiên dược. |
| `[BUILDER_BASE] phase=clock_tower action=activate_boost status=succeeded` | `✓ Đã kích hoạt Tăng tốc Tháp đồng hồ` | Tăng tốc Tháp đồng hồ hoàn tất. |
| `[BUILDER_BASE] phase=wall_upgrade action=upgrade status=succeeded` | `✓ Đã nâng cấp 1 viên tường` | Nâng cấp tường thành công. |
| `[BUILDER_BASE] phase=wall_upgrade action=upgrade status=skipped reason=insufficient_resources` | `⏭ Bỏ qua nâng cấp tường vì không đủ tài nguyên` | Vàng/Dầu không đủ ngưỡng nâng tường. |
| `[BUILDER_BASE] phase=wall_upgrade action=upgrade status=skipped reason=no_builder_available` | `⏭ Bỏ qua nâng cấp tường vì không có thợ xây trống` | Thợ xây đang bận. |
| `[BUILDER_BASE] phase=wall_upgrade action=upgrade status=skipped reason=upgrade_target_not_found` | `⚠ Không tìm thấy vị trí tường phù hợp để nâng cấp` | Nhận diện vị trí viên tường bị trượt. |

### 5.4. Tấn công & Quản lý Quân đội (`[BATTLE]`)

| Dev Log (Structured) | User Log (Formatted) | Ý nghĩa & Hướng dẫn xử lý |
| :--- | :--- | :--- |
| `[BATTLE] phase=deployment action=start_strategy status=started strategy=auto` | `● Đang bắt đầu chiến thuật thả quân…` | Bắt đầu quy trình thả lính. |
| `[BATTLE] phase=single_attack action=attack status=succeeded index=1 damage=85 stars=2` | `✓ Trận đánh #1 hoàn tất: 2 sao (85% phá hủy)` | Trận đánh thành công. |
| `[BATTLE] phase=army_check action=verify_ready status=skipped reason=hero_recovering` | `⏭ Bỏ qua tấn công vì quân đội hoặc tướng chưa sẵn sàng` | Lính/Tướng đang chờ hồi phục. |
| `[BATTLE] phase=attack_session action=complete_session status=succeeded completed=3 attempts=3` | `✓ Đã hoàn thành 3/3 lượt tấn công` | Hoàn thành số trận đánh. |

### 5.5. Quản lý Tài khoản & Đóng Ứng dụng (`[ACCOUNT]`, `[APP]`)

| Dev Log (Structured) | User Log (Formatted) | Ý nghĩa & Hướng dẫn xử lý |
| :--- | :--- | :--- |
| `[ACCOUNT] phase=switch_account action=switch status=started current="Acc1" target="Acc2"` | `● Đang chuyển từ tài khoản Acc1 sang Acc2…` | Bắt đầu chuyển tài khoản. |
| `[ACCOUNT] phase=switch_account action=switch status=succeeded current="Acc1" target="Acc2"` | `✓ Đã chuyển sang tài khoản Acc2 thành công` | Chuyển Supercell ID hoàn tất. |
| `[APP] phase=shutdown action=request_stop status=pending reason=user_requested` | `■ Đang dừng bot theo yêu cầu…` | Người dùng nhấn Stop. |
| `[APP] phase=shutdown action=stop status=succeeded reason=user_requested` | `■ Bot đã dừng` | Bot đã dừng an toàn (Information). |
| `[CYCLE] phase=cycle action=run status=cancelled reason=user_requested cycle_id=cycle-002` | `■ Chu kỳ #2 đã dừng theo yêu cầu` | Chu kỳ bị hủy ngang do người dùng dừng. |
