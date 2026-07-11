# Wall Upgrade Plan

## Mục tiêu

- Nâng đúng wall level đã cấu hình, chỉ sau khi về Làng chính.
- Không tiêu quá ngưỡng giữ lại cho Gold hoặc Elixir.
- Không báo số wall đã nâng nếu game chưa xác nhận giao dịch.
- Hoạt động ổn định khi menu Builder đang đóng, mở, đã chọn item, hoặc đang lệch vị trí cuộn.
- Giữ flow C# + OpenCV + ADB hiện tại. Không thêm OCR engine, dependency, cache upgrade list, hoặc native code.

## Hiện trạng cần sửa

- UI lưu `wall_upgrade_threshold` và `wall_reserve_threshold`, nhưng backend đọc `wall_gold_threshold` và `wall_elixir_threshold`. Cấu hình từ UI không điều khiển runtime.
- `HandleHomeResources(...)` tính số lượng theo toàn bộ tài nguyên có thể chi, rồi `UpgradeWallBulk(...)` thực hiện một lần. Số lượng trả về và stats có thể lớn hơn số wall game đã chọn.
- `Add More` bấm theo lượng tài nguyên, không theo số candidate wall thực có. Menu có thể hết wall trước khi thao tác hoàn tất.
- Sau confirm không có screenshot verification. Tap sai tọa độ hoặc dialog chưa mở vẫn có thể log `upgraded`.
- Tất cả ROI, tap point, swipe point là pixel cố định `1600x900`; không có preflight kiểm tra frame/game layout.

## Quy tắc quyết định

- `targetLevel`: level wall người dùng chọn. Chỉ hỗ trợ `8..17` cho đến khi có bảng giá và template được xác minh cho level mới.
- `reserveGold`: Gold luôn giữ lại sau upgrade. Mặc định `100_000`; UI có thể ghi đè.
- `reserveElixir`: Elixir luôn giữ lại sau upgrade. Mặc định `0`; UI có thể ghi đè.
- `startGold` và `startElixir`: ngưỡng tối thiểu để bắt đầu dùng từng resource. Không dùng resource dưới ngưỡng này.
- `affordableGold = max(0, gold - reserveGold) / wallCost` khi `gold >= startGold`.
- `affordableElixir = max(0, elixir - reserveElixir) / wallCost` khi `elixir >= startElixir`.
- `requestedCount = min(affordableCount, wallBatchLimit)`. Template match chỉ tìm row/candidate để chọn wall; không dùng số match để suy ra số wall còn lại.
- Ưu tiên resource có `requestedCount` lớn hơn. Bằng nhau ưu tiên Gold để tránh tồn tài nguyên bị mất khi full storage. Quy tắc này phải nằm trong test.
- Mỗi batch chỉ ghi nhận success sau khi confirm UI được xác minh. Không trừ resource ảo, không update stats trước verify.

## Phase 1: Khóa hợp đồng cấu hình và logic thuần

Mục tiêu: backend nhận đúng ý nghĩa cấu hình từ UI; quyết định resource/count có unit test, không cần ADB.

### Task 1.1: Chuẩn hóa schema wall config

- Chọn schema duy nhất ở root config:
  - `upgrade_wall`: bool
  - `wall_level`: int
  - `wall_gold_threshold`: int
  - `wall_elixir_threshold`: int
  - `wall_gold_reserve`: int
  - `wall_elixir_reserve`: int
- Đổi bindings trong `MainVillageViewModel` và nhãn UI để phản ánh 4 giá trị riêng: start Gold, start Elixir, reserve Gold, reserve Elixir.
- Cập nhật `ConfigStore.EnsureDefaults(...)`, default JSON trong `CVAutomationFramework`, load/save view model, và `GetWallUpgradeConfig(...)` cùng schema.
- Chỉ giữ fallback key cũ nếu config đã phát hành cần migration. Nếu có fallback, log một lần `legacy_config_migrated`; không giữ song song vô hạn.

**Done khi:** Save UI rồi reload app vẫn giữ đúng bốn ngưỡng; backend log đúng các giá trị vừa lưu.

### Task 1.2: Tách quyết định khỏi ADB/UI

- Tạo record nội bộ immutable cho input decision: wall level, cost, Gold, Elixir, start thresholds, reserves, batch limit.
- Tạo record output: resource, requested count, skip reason.
- Chuyển công thức affordability từ `HandleHomeResources(...)` vào hàm pure nhỏ.
- Hàm quyết định không screenshot, không tap, không sleep, không log side effect.
- `HandleHomeResources(...)` chỉ đọc resource, lấy candidate count, gọi decision, rồi điều phối UI.

**Done khi:** cùng input luôn trả cùng output; `requestedCount` không vượt affordability hoặc batch limit.

### Task 1.3: Thêm unit test table-driven

- Thêm backend test project chỉ nếu solution chưa có test project backend dùng được. Không kéo framework mới; dùng dependency test có sẵn hoặc `dotnet test` stack repo đang dùng.
- Cases tối thiểu:
  - level ngoài `8..17` bị skip.
  - thiếu cost bị skip.
  - Gold/Elixir dưới start threshold bị skip dù có đủ cost.
  - reserve không bị chi.
  - batch limit là giới hạn cứng.
  - Gold và Elixir cùng đủ, ưu tiên theo rule.
  - affordability `0` không gọi flow upgrade.
  - count đúng ở biên `cost`, `cost + reserve`, và `2 * cost + reserve - 1`.

**Done khi:** test chạy độc lập, không ADB/device/template asset.

### Checkpoint Phase 1

- `dotnet build src/backend/Simplimixi.Backend.csproj`
- `dotnet test` project test có liên quan.
- Manual: lưu 4 ngưỡng UI, restart app, xác nhận config JSON và backend log khớp.

## Phase 2: Làm transaction UI an toàn và có giới hạn

Mục tiêu: mỗi batch chỉ chọn số wall UI xác minh được, reset UI xác định, xác minh đủ trước/sau confirm.

### Task 2.1: Tạo preflight/reset builder menu

- Trước mỗi batch: zoom-out về state chuẩn, tap safe dismiss point, mở Builder menu, đưa scroll về vị trí gốc, chờ screenshot ổn định.
- Xác minh Builder menu hiện diện bằng visual signal hiện có hoặc template nhẹ. Fail thì return skip reason, không tiếp tục tap.
- Gộp các điểm reset đang phân tán trong `PrepareWallSearch(...)` và `UpgradeWallBulk(...)` thành một sequence có log phase rõ ràng.
- Mỗi tap/sleep dùng `CancellationToken`; cancellation đóng sequence bằng safe dismiss.

**Done khi:** flow có thể bắt đầu từ menu đóng, menu mở, hoặc item cũ đang selected mà không dùng stale selection.

### Task 2.2: Phát hiện candidate và chọn batch hữu hạn

- Giữ OpenCV template scan từ Simplicity/current repo: 4 generic templates, alpha mask, local maxima, dedupe, ordering theo Y, saved offset.
- Sau scan, trả candidates cùng match metadata; dùng chúng để chọn đúng row. Số template match không phải số lượng wall còn lại.
- Candidate selection thử tối đa 3 vị trí riêng. Nếu saved offset fail, reset offset rồi chọn candidates còn lại.
- Candidate hợp lệ cần đồng thời pass: panel mở, OCR cost trong sai số `15%`, và resource upgrade button phù hợp hiện diện/khả dụng.
- Tính `requestedCount` trước Add More bằng `min(affordableCount, wallBatchLimit)`. Hard cap batch ban đầu `10` để giảm thiệt hại khi UI game thay đổi; chỉ tăng sau telemetry ổn định.

**Done khi:** số lần `Add More` bằng `requestedCount - 1`, không phải toàn bộ affordability; count `1` không tap Add More.

### Task 2.3: Xác minh multi-selection trước upgrade

- Sau từng Add More, hoặc tối thiểu trước confirm, screenshot panel và OCR tổng cost hoặc quantity nếu UI hiển thị ổn định. So expected `requestedCount * wallCost`.
- Nếu không đọc được quantity/tổng cost tin cậy: downgrade batch về 1 wall, không confirm multi blind.
- Nếu quantity khác expected: dismiss, reset builder state, retry một lần với batch nhỏ hơn; fail lần hai thì skip batch.
- Log reason machine-readable: `selection_count_unverified`, `selection_count_mismatch`, `retry_single`.

**Done khi:** multi-upgrade không confirm khi selection count chưa chứng minh được; single-wall vẫn là fallback hoạt động.

### Task 2.4: Xác minh confirm và kết quả giao dịch

- Sau tap resource button, xác minh confirm dialog hiện diện trước khi tap confirm coordinate.
- Sau confirm, chờ state settle rồi screenshot lại:
  - dialog đóng hoặc home/builder state hợp lệ;
  - resource tương ứng giảm gần `requestedCount * wallCost`, với tolerance OCR nhỏ;
  - nếu game đã refresh UI, không còn selected panel cũ.
- Chỉ return actual upgraded count sau post-confirm verification.
- Nếu post-confirm không xác minh được: return `0` cho stats, log `outcome_unknown`, dismiss UI, dừng wall action tới cycle sau. Không retry confirm vì có thể double-spend.

**Done khi:** `UpdateWallStats(...)` chỉ nhận verified count; failure/unknown không làm stats tăng.

### Checkpoint Phase 2

- Build backend.
- Device smoke test: từng batch Gold 1 wall, Elixir 1 wall, Gold multi, Elixir multi, không đủ resource, không có wall target, menu đã mở, selection fail.
- Lưu screenshot/log từng test case vào thư mục debug bỏ qua git; review `requested`, `selected`, `confirmed`, `verified` counters.

## Phase 3: Cứng hóa môi trường, telemetry, rollout

Mục tiêu: phát hiện game layout thay đổi, chẩn đoán failure nhanh, rollout không phá farming.

### Task 3.1: Kiểm tra layout trước thao tác

- Lấy screenshot trước wall flow; validate kích thước/scale được hỗ trợ trước khi dùng fixed points.
- Nếu chỉ hỗ trợ `1600x900`, skip rõ `unsupported_screen_layout` thay vì tap sai.
- Nếu device matrix có nhiều resolution cần hỗ trợ, chuyển fixed points và ROI sang normalized coordinates, sau đó calibrate từng ROI bằng screenshot fixture.
- Không chuyển partial sang normalized: toàn bộ point cùng coordinate system trong một phase để tránh lệch tap/ROI.

**Done khi:** resolution không hỗ trợ không thể trigger tap upgrade; log nêu width, height, reason.

### Task 3.2: Telemetry và debug evidence

- Chuẩn hóa log fields xuyên suốt: `cycle`, `resource`, `level`, `cost`, `candidate_match_count`, `affordable_count`, `requested_count`, `verified_count`, `reason`.
- Debug mode mới lưu screenshot theo phase: `preflight`, `candidate_selected`, `selection_verified`, `confirm_open`, `outcome_verified`.
- Không log full resource screenshot hoặc credential/device serial vào public logs.
- Thêm counter session cho `wall_attempted`, `wall_verified`, `wall_skipped`, `wall_unknown`; UI stats chỉ dùng `wall_verified`.

**Done khi:** một failure log đủ trả lời fail ở scan, select, multi-select, confirm, hay post-confirm mà không tái chạy.

### Task 3.3: Soak test và rollout guard

- Chạy test matrix ít nhất 20 cycle mỗi loại Gold/Elixir trên account test, target wall level cố định.
- Đo: verify success rate, false selection rate, unknown outcome rate, average batch size, time/batch.
- Điều kiện bật multi batch mặc định: `outcome_unknown = 0`, false selection = `0`, verified success rate >= `95%` trong soak test.
- Nếu chưa đạt: phát hành single-wall mode mặc định; giữ batch cap `1` bằng config internal. Không disable toàn bộ auto wall khi single vẫn an toàn.
- Thêm kill switch config `wall_batch_limit`; default rollout `1`, tăng `3`, rồi tối đa `10` sau mỗi đợt telemetry sạch.

**Done khi:** có rollback không code change; rollout theo evidence, không theo suy đoán.

### Checkpoint Phase 3

- `dotnet build src/backend/Simplimixi.Backend.csproj`
- `dotnet build src/frontend/Simplimixi.csproj`
- `dotnet test` toàn bộ test liên quan.
- Soak report gồm input config, screenshots debug, counters, pass/fail từng scenario.

## Thứ tự file dự kiến

- `src/backend/Core/WallUpdater.cs`: decision, UI transaction, validation, telemetry.
- `src/backend/Core/CVAutomationFramework.cs`: load config, call contract, stats only after verified result.
- `src/frontend/ViewModels/Settings/MainVillageViewModel.cs`: schema config và bindings.
- `src/frontend/Views/Settings/MainVillageView.axaml`: nhãn/inputs threshold-reserve rõ nghĩa.
- `src/frontend/ConfigStore.cs`: default/migration config.
- `tests/...`: pure decision cases và, nếu abstraction screenshot được tách đủ nhỏ, validation fixtures.

## Không làm trong scope này

- Không thêm Tesseract, RapidOCR, GLM, HTTP OCR, hoặc scan full upgrade cache kiểu NX. Current template scan + numeric OCR đã đủ cho target wall level.
- Không tách native `WallUpgradeDecision`; đây là logic nhỏ, C# test dễ hơn và risk thấp.
- Không tự suy ra wall level từ hình map. User chọn target level, transaction xác minh bằng upgrade cost.
- Không retry post-confirm mù. Giao dịch unknown phải dừng để tránh double upgrade.
