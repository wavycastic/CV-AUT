# Plan sửa lỗi không đánh được Làng chính

## Phạm vi

- Chỉ sửa backend và log cho luồng Làng chính.
- Không đổi UI nếu frontend vẫn gửi đúng `run_session.play_mode = "main_village"`, `attack_mode = "attack"`, `attack`, `train_mode`.
- Không đổi chiến thuật rải quân trong `Attacks.Run()` trừ khi log xác nhận lỗi nằm ở deploy.

## Bối cảnh hiện tại

- Luồng đánh Làng chính chạy trong `CVAutomationFramework.OneCycle()`.
- `OneCycle()` lấy `MainVillageConfig` theo `_currentVillageIdx`, xác nhận home bằng `EnsureHomeBase()`, train, rồi gọi `SearchAttack()`.
- `SearchAttack()` đang tap tọa độ cố định:
  - `113,797`: nút Attack ngoài home.
  - `272,659`: Find Match.
  - `1445,804`: chấp nhận phí tìm trận.
- Sau đó loop dùng `WaitForScoutScreen()`, `IsNextButtonPresent()`, `IsTarget.ExtractResources()`, `ShouldAcceptTarget()`, rồi `_attacks.Run()`.
- Log hiện thiếu dấu mốc xác nhận từng bước trong `SearchAttack()`, nên khó biết fail ở home, attack menu, find match, phí tìm trận, hay scout screen.

## Giả thuyết lỗi

1. `SearchAttack()` tap sai hoặc quá sớm ở một trong ba bước, nhưng không có log xác nhận màn hình sau tap.
2. Backend không phân biệt rõ `targetVillage = "main_village"` và profile village khi chạy multi-account, nên user tưởng đã chọn Làng chính nhưng worker vẫn gate theo village/profile khác.
3. `EnsureHomeBase()` nhận diện home quá hẹp: chỉ `game_setting.png` và `shop.png`; nếu theme/layout/event che icon, cycle skip `home_not_detected`.
4. `WaitForScoutScreen()` hoặc `IsNextButtonPresent()` fail sau khi đã bấm tìm trận, backend reboot thay vì retry quay lại đúng bước.
5. `AttackMode` từ profile `Village_N.json` có thể ghi đè root config thành `donate_only`, khiến Làng chính không vào scout/attack.

## Impact analysis cần chạy trước khi sửa thật

Theo `AGENTS.md`, trước khi sửa symbol phải chạy GitNexus impact:

- `gitnexus_impact({ target: "CVAutomationFramework.OneCycle", direction: "upstream" })`
- `gitnexus_impact({ target: "CVAutomationFramework.SearchAttack", direction: "upstream" })`
- `gitnexus_impact({ target: "CVAutomationFramework.EnsureHomeBase", direction: "upstream" })`
- `gitnexus_impact({ target: "CVAutomationFramework.WaitForScoutScreen", direction: "upstream" })`
- Nếu sửa config load: `gitnexus_impact({ target: "CVAutomationFramework.GetMainVillageConfig", direction: "upstream" })`

Blast radius dự kiến:

- `SearchAttack()`: direct caller `OneCycle()`, risk MEDIUM, ảnh hưởng toàn bộ farming Làng chính.
- `EnsureHomeBase()`: nhiều caller gồm startup, account switch, post-battle wall update, risk HIGH nếu đổi detection/recovery.
- `OneCycle()`: worker loop gọi trực tiếp, risk HIGH vì điều phối train/scout/attack/stats.
- `GetMainVillageConfig()`: ảnh hưởng profile config, donate-only, surrender, request troops, risk MEDIUM.

Nếu GitNexus trả HIGH/CRITICAL: báo user trước khi sửa.

## Plan sửa log

1. Chuẩn hóa log `SearchAttack()` thành từng step có tọa độ, timeout, kết quả detect:
   - `phase=search_attack status=start village=... target_village=main_village`
   - `step=open_attack_menu action=tap x=113 y=797`
   - `step=open_attack_menu status=success|retry|fail reason=...`
   - `step=find_match action=tap x=272 y=659`
   - `step=confirm_cost action=tap x=1445 y=804`
   - `step=scout_screen status=success|fail reason=...`

2. Log config quyết định đánh trước khi vào scout:
   - `active_village`
   - `target_village`
   - `attack_mode`
   - `attack_strategy`
   - `train_mode`
   - `profile_path`
   - `config_source=root|profile|fallback`

3. Log `EnsureHomeBase()` chi tiết hơn:
   - template nào match (`game_setting`, `shop`)
   - score
   - ROI
   - số lần retry
   - boot recovery có chạy không

4. Log fail phải có action kế tiếp:
   - `action=retry_search_attack`
   - `action=return_home`
   - `action=boot_recovery`
   - `action=skip_cycle`

5. Không log quá dày trong loop mỗi giây; chỉ log lần đầu, retry, và fail cuối.

## Plan sửa backend

1. Tách `SearchAttack()` từ `void` sang kết quả rõ ràng khi sửa thật:
   - `bool SearchAttack(CancellationToken token, out string reason)` hoặc enum nhỏ nội bộ.
   - `OneCycle()` chỉ vào scout loop nếu `SearchAttack()` success.
   - Fail thì retry 1 lần từ home, sau đó recovery.

2. Sau tap `Attack`, xác nhận đã vào menu Attack bằng template/nút hiện có trước khi tap `Find Match`.
   - Ưu tiên dùng helper hiện có `TryMatchTemplate()`/`TapFirstVisibleTemplate()` nếu đủ.
   - Nếu chưa có template phù hợp, giữ tọa độ nhưng thêm delay/retry và screenshot debug khi fail.

3. Sau tap `Find Match`, xác nhận màn hình phí tìm trận hoặc scout screen.
   - Nếu phí hiện: tap confirm.
   - Nếu scout đã vào thẳng: bỏ tap confirm.
   - Nếu chưa thấy: retry tap Find Match tối đa 1 lần.

4. Sau tap confirm cost, gọi `WaitForScoutScreen()` ngay trong `SearchAttack()` để chốt trạng thái.
   - `OneCycle()` không phải đoán lỗi do SearchAttack hay scout loop.
   - Nếu fail, lưu debug screenshot qua helper hiện có nếu có.

5. Bảo vệ Làng chính theo `targetVillage`:
   - Khi `targetVillage == "main_village"`, cho phép chạy `OneCycle()` farming bình thường.
   - Nếu sau này có `night_village`/`clan_capital`, branch riêng, không để rơi vào Làng chính bằng fallback im lặng.
   - Log skip rõ: `reason=unsupported_target_village target_village=...`.

6. Chặn `donate_only` ghi đè ngoài ý muốn:
   - Log `attack_mode` cuối cùng sau khi merge root + profile.
   - Nếu user chọn Làng chính đánh nhưng profile đang `donate_only`, log warning rõ `reason=profile_attack_mode_donate_only`.
   - Không tự đổi config trong code; chỉ fail/skip có thông tin.

7. Giảm reboot không cần thiết:
   - Với fail `scouting_ui_not_detected` ngay sau `SearchAttack()`, retry SearchAttack trước.
   - Chỉ `BootRecovery()` sau khi home không detect hoặc retry vẫn fail.

## File cần chạm khi sửa thật

- `src/backend/Core/CVAutomationFramework.cs`
  - `OneCycle()`
  - `SearchAttack()`
  - `EnsureHomeBase()` / `DetectHomeBase()` nếu cần log score
  - `WaitForScoutScreen()` nếu cần expose reason
  - `GetMainVillageConfig()` nếu cần log source config

Không cần sửa `src/backend/Core/Attacks.cs` trong vòng đầu, vì lỗi hiện mô tả là không vào được đánh Làng chính, chưa phải lỗi rải quân.

## Test/check sau khi sửa thật

1. Build backend/frontend:
   - `dotnet build E:\Projects\CV-AUT\src\backend\Simplimixi.Backend.csproj`
   - `dotnet build E:\Projects\CV-AUT\src\frontend\Simplimixi.csproj`

2. Chạy unit test hiện có:
   - `dotnet test E:\Projects\CV-AUT\tests\Simplimixi.Frontend.Tests\Simplimixi.Frontend.Tests.csproj`

3. Manual smoke test với emulator:
   - Config `run_session.play_mode = "main_village"`.
   - Config `attack_mode = "attack"`.
   - Start bot từ home screen.
   - Xác nhận log có đủ chuỗi:
     - `phase=home_check status=success`
     - `phase=search_attack status=start`
     - `step=open_attack_menu status=success`
     - `step=find_match status=success`
     - `step=scout_screen status=success`
     - `phase=scout status=pending index=1`
     - `phase=select_strategy status=success`
     - `phase=run_attack status=start`

4. Manual fail test:
   - Mở popup/event che màn hình rồi start.
   - Xác nhận log nói rõ fail ở step nào và action retry/recovery.

5. Trước commit:
   - `gitnexus_detect_changes()` để xác nhận chỉ ảnh hưởng luồng Làng chính/search attack/log.

## Thứ tự triển khai đề xuất

1. Chỉ thêm log và return status cho `SearchAttack()`.
2. Build và smoke test, xem lỗi thật nằm ở step nào.
3. Nếu fail do tap sai, thay tap mù bằng template detect/tap.
4. Nếu fail do config/profile, sửa merge/log config.
5. Nếu fail do home detect, mở rộng `DetectHomeBase()` bằng template/ROI có sẵn.

## Tiêu chí xong

- User chọn Làng chính + attack mode thì backend vào được scout screen.
- Nếu không vào được scout screen, log chỉ rõ fail tại step nào, tọa độ nào, detect nào thiếu.
- Không reboot ngay khi chỉ fail một tap tìm trận.
- Không ảnh hưởng donate-only, multi-account switch, wall update sau battle.
