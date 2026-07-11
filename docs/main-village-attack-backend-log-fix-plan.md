# Plan sửa log và backend lỗi không đánh được Làng chính

## Mục tiêu

- User chọn `run_session.play_mode = "main_village"` và `attack_mode = "attack"` thì backend phải vào được màn hình scout và chạy `_attacks.Run()` khi target đạt điều kiện.
- Nếu không vào được scout, log phải chỉ rõ fail ở bước nào: config gate, home detect, open attack menu, find match, confirm cost, scout wait, next button, hay recover.
- Không sửa UI trong vòng này nếu frontend vẫn ghi đúng config Làng chính.
- Không sửa chiến thuật rải quân trong `Attacks.Run()` trừ khi log mới chứng minh lỗi nằm ở deploy troop.

## Hiện trạng đã rà lại

- `src/backend/Core/CVAutomationFramework.cs:393` gọi `GetMainVillageConfig(cfg, _currentVillageIdx)`.
- `src/backend/Core/CVAutomationFramework.cs:400` gọi `EnsureHomeBase(...)`; fail thì skip cycle bằng `reason=home_not_detected`.
- `src/backend/Core/CVAutomationFramework.cs:407` nếu `mainConfig.AttackMode == DonateOnly` thì chạy donate-only và return, không vào scout.
- `src/backend/Core/CVAutomationFramework.cs:482` log threshold hiện thiếu `attack_mode`, `attack`, `train_mode`, `profile_path`, `target_village`, nguồn config.
- `src/backend/Core/CVAutomationFramework.cs:486` gọi `SearchAttack()` nhưng method trả `void`, nên `OneCycle()` không biết mở attack menu/find match/confirm cost thành công hay chưa.
- `src/backend/Core/CVAutomationFramework.cs:505` nếu `WaitForScoutScreen()` fail thì backend `BootRecovery()` ngay, không retry `SearchAttack()` từ home.
- `src/backend/Core/CVAutomationFramework.cs:829-839` `SearchAttack()` đang tap mù 3 tọa độ: `113,797`, `272,659`, `1445,804`; chỉ sleep 700ms, không xác nhận screen sau mỗi tap.
- `src/backend/Core/CVAutomationFramework.cs:1016-1050` `WaitForScoutScreen()` chỉ trả `bool`, fail timeout không trả reason chi tiết ngoài `timeout`.
- `src/backend/Core/CVAutomationFramework.cs:1459-1497` `GetMainVillageConfig()` cho profile `Village_N.json` ghi đè root config, nên `attack_mode = donate_only` trong profile có thể làm user tưởng đang chọn attack nhưng backend skip sang donate-only.
- `src/backend/Core/CVAutomationFramework.cs:1666-1677` parse `AccountConfig.TargetVillage`, nhưng `BotLoop()` không dùng `targetVillage` để quyết định nhánh `OneCycle()`.

## Root cause khả dĩ

1. `SearchAttack()` thao tác quá mù: tap sai màn hình hoặc quá sớm, nhưng không có log xác nhận attack menu/find match/fee/scout.
2. `SearchAttack()` không có return status, nên `OneCycle()` vẫn đi vào scout loop dù thực tế chưa vào scout.
3. Fail scout sau `SearchAttack()` bị reboot ngay, làm mất thông tin lỗi và giảm khả năng tự hồi phục.
4. Config merge ưu tiên profile có thể chuyển `attack_mode` thành `donate_only` ngoài ý muốn.
5. Multi-account có `targetVillage`, nhưng backend chưa log/gate rõ `main_village`; khó xác nhận user chọn Làng chính nhưng worker đang chạy đúng village/account.
6. `EnsureHomeBase()` log thành công chưa ghi template/score/ROI; khi fail home detect, không biết icon nào thiếu.

## Nguyên tắc sửa

- Sửa nhỏ nhất trong `CVAutomationFramework.cs`; không thêm abstraction rộng.
- Ưu tiên dùng template đã có trong `assets/Templates/ui`: `attack_button.png`, `find_match.png`, `find_match_n.png`, `okay_battle_rank.png`, `end_battle.png`, `next_button.png`.
- Giữ tọa độ fallback hiện tại khi template chưa match, nhưng phải log rõ fallback.
- Không tự đổi config user; chỉ log warning nếu profile/root conflict.
- Không reboot ngay khi chỉ fail search/scout lần đầu; retry một lần có kiểm soát.
- Log có cấu trúc key-value, ít nhưng đủ mốc; không spam mỗi 500ms.

## Impact analysis bắt buộc trước khi sửa code thật

Theo `AGENTS.md`, trước khi sửa symbol phải chạy GitNexus impact và báo blast radius:

```text
gitnexus_impact({ target: "CVAutomationFramework.OneCycle", direction: "upstream" })
gitnexus_impact({ target: "CVAutomationFramework.SearchAttack", direction: "upstream" })
gitnexus_impact({ target: "CVAutomationFramework.EnsureHomeBase", direction: "upstream" })
gitnexus_impact({ target: "CVAutomationFramework.WaitForScoutScreen", direction: "upstream" })
gitnexus_impact({ target: "CVAutomationFramework.GetMainVillageConfig", direction: "upstream" })
gitnexus_impact({ target: "CVAutomationFramework.GetConfiguredAccounts", direction: "upstream" })
```

- Nếu `HIGH` hoặc `CRITICAL`: báo user trước khi sửa.
- Dự kiến rủi ro cao nhất: `OneCycle()` và `EnsureHomeBase()` vì ảnh hưởng worker loop, donate-only, multi-account, wall update sau battle.
- Dự kiến rủi ro trung bình: `SearchAttack()`, `WaitForScoutScreen()`, `GetMainVillageConfig()` vì ảnh hưởng farming Làng chính và config profile.

## Plan sửa log

### 1. Log quyết định config trước khi làm gì

Thêm log ngay sau `GetMainVillageConfig()` và `GetTrainingConfig()`:

```text
[CONFIG-CS] phase=main_village_config status=loaded active_village=1 target_village=main_village attack_mode=attack attack=Dragon_Attack train_mode=smart quick_slot=1 profile_path="..." attack_mode_source=profile|root|fallback attack_source=profile|root|fallback train_mode_source=profile|root|fallback
```

Nếu profile ghi đè root `attack_mode` thành donate-only:

```text
[CONFIG-CS WARNING] phase=main_village_config status=skip reason=profile_attack_mode_donate_only root_attack_mode=attack profile_attack_mode=donate_only action=donate_only_cycle
```

Nếu `targetVillage` khác Làng chính:

```text
[CONFIG-CS WARNING] phase=main_village_config status=skip reason=unsupported_target_village target_village=night_village action=skip_cycle
```

### 2. Log home detect có bằng chứng

Sửa `EnsureHomeBase()`/`DetectHomeBase()` log các mốc:

```text
[FSM-CS] phase=home_check status=start timeout=50 allow_boot_recovery=true
[FSM-CS] phase=home_check status=success detector=game_setting score=0.812 roi="..." elapsed_ms=1234
[FSM-CS] phase=home_check status=success detector=shop score=0.774 center=(1440,770) elapsed_ms=1234
[FSM-CS WARNING] phase=home_check status=retry attempt=1 reason=not_detected action=wait
[FSM-CS ERROR] phase=home_check status=fail reason=detection_failed action=boot_recovery
```

Không log mỗi vòng nếu không cần; log attempt đầu, retry theo nhịp lớn, fail cuối.

### 3. Log `SearchAttack()` theo từng step

Chuẩn hóa chuỗi log:

```text
[SEARCH-CS] phase=search_attack status=start village=1 target_village=main_village attempt=1
[SEARCH-CS] phase=search_attack step=open_attack_menu status=pending action=tap x=113 y=797 method=coordinate
[SEARCH-CS] phase=search_attack step=open_attack_menu status=success detector=find_match template="ui/find_match.png" score=0.82 elapsed_ms=900
[SEARCH-CS WARNING] phase=search_attack step=open_attack_menu status=retry reason=find_match_not_visible action=tap_attack_again attempt=2
[SEARCH-CS] phase=search_attack step=find_match status=pending action=tap template="ui/find_match.png" center=(272,659)
[SEARCH-CS] phase=search_attack step=find_match status=success detector=confirm_cost template="ui/okay_battle_rank.png" score=0.80
[SEARCH-CS] phase=search_attack step=confirm_cost status=success action=tap x=1445 y=804 method=coordinate
[SEARCH-CS] phase=search_attack step=scout_screen status=success detector=end_battle elapsed_ms=4200
[SEARCH-CS ERROR] phase=search_attack step=scout_screen status=fail reason=timeout action=retry_search_attack
```

### 4. Log scout loop phân biệt lỗi

Khi `WaitForScoutScreen()` fail trong scout loop, log reason khác nhau:

```text
[SCOUT-CS WARNING] phase=scout_wait status=fail reason=timeout last_detector=end_battle action=retry_search_attack
[SCOUT-CS WARNING] phase=scout status=fail reason=next_button_unavailable action=return_home
[SCOUT-CS ERROR] phase=scout status=fail reason=search_attack_failed action=boot_recovery
```

### 5. Log action tiếp theo cho mọi fail

Mọi fail phải có `action=`:

- `action=retry_search_attack`
- `action=return_home`
- `action=boot_recovery`
- `action=skip_cycle`
- `action=donate_only_cycle`

## Plan sửa backend

### 1. Tách trạng thái `SearchAttack()`

Đổi `SearchAttack()` từ `void` sang kết quả rõ:

```text
SearchAttackResult SearchAttack(CancellationToken token, int attempt, string targetVillage)
```

Kết quả tối thiểu:

- `Success`
- `Cancelled`
- `OpenAttackMenuFailed`
- `FindMatchFailed`
- `ConfirmCostFailed`
- `ScoutScreenTimeout`
- `ConnectionRecovered`

Nếu muốn diff ngắn hơn: dùng `bool SearchAttack(..., out string reason)`. Enum tốt hơn cho log/test nhưng vẫn giữ nội bộ file.

### 2. Xác nhận sau tap Attack

Sau tap `113,797`, không tap Find Match ngay. Chờ tối đa 2-3 giây để detect một trong các template:

- `ui/find_match.png`
- `ui/find_match_n.png`
- `ui/find_match_rank.png`

Nếu thấy template, tap center template. Nếu không thấy, tap lại Attack tối đa 1 lần. Nếu vẫn fail, return `OpenAttackMenuFailed`.

### 3. Xác nhận sau tap Find Match

Sau tap Find Match, chờ một trong ba trạng thái:

- Thấy confirm cost/rank bằng `ui/okay_battle_rank.png`: tap confirm.
- Thấy scout screen bằng `ui/end_battle.png`: bỏ qua confirm vì đã vào scout.
- Thấy connection popup: recovery và return `ConnectionRecovered`.

Nếu không thấy gì, retry tap Find Match tối đa 1 lần, rồi return `FindMatchFailed`.

### 4. Xác nhận sau confirm cost

Sau tap confirm cost, `SearchAttack()` tự gọi scout wait ngắn để chốt kết quả.

- Success khi detect `end_battle.png` trong `ScoutUiRoi`.
- Fail `ScoutScreenTimeout` khi timeout.
- Không để `OneCycle()` đoán lỗi từ scout loop nữa.

### 5. Đổi `OneCycle()` chỉ vào scout loop khi search thành công

Luồng mới:

1. Load config và log config.
2. `EnsureHomeBase()` success.
3. Nếu `AttackMode == DonateOnly`: log reason và chạy donate-only.
4. Gọi `SearchAttack()` attempt 1.
5. Nếu fail loại recoverable: `ReturnHome()` hoặc `EnsureHomeBase(..., allowBootRecovery: false)`, rồi retry `SearchAttack()` attempt 2.
6. Nếu attempt 2 fail: log final reason, rồi `BootRecovery()` hoặc skip cycle tùy lỗi.
7. Chỉ khi `SearchAttack()` success mới bắt đầu scout loop `searchCount = 1`.

### 6. Giảm reboot không cần thiết

Không `BootRecovery()` ngay tại `WaitForScoutScreen()` fail lần đầu sau search.

- Fail `ScoutScreenTimeout`: retry search 1 lần.
- Fail `OpenAttackMenuFailed`: quay home/clear popup trước, rồi retry.
- Fail `FindMatchFailed`: quay home hoặc back về attack menu, rồi retry.
- Fail `ConnectionRecovered`: return cycle vì recovery đã restart app.
- Fail `home_not_detected`: mới boot recovery như hiện tại.

### 7. Gắn `targetVillage` vào runtime

Hiện `AccountConfig.TargetVillage` chỉ log trong `SwitchToAccount()`, chưa được `OneCycle()` dùng.

Sửa tối thiểu:

- Thêm field runtime `_currentTargetVillage`, default `"main_village"`.
- Single-account set `_currentTargetVillage = "main_village"`.
- Multi-account set `_currentTargetVillage = account.TargetVillage` trước `OneCycle()`.
- `OneCycle()` nếu `_currentTargetVillage != "main_village"` thì log skip rõ và không chạy farming Làng chính.

Không thêm hỗ trợ Night Village/Clan Capital trong plan này.

### 8. Log nguồn config profile/root

`GetMainVillageConfig()` hiện trả config nhưng không trả source. Sửa tối thiểu bằng helper nội bộ hoặc record nhỏ chứa source:

- `attack_mode_source`
- `target_source`
- `request_troops_source`
- `use_event_troops_source`
- `smart_surrender_source`

Nếu muốn diff nhỏ: chỉ log source cho `attack_mode`, `attack`, `train_mode`, vì đây là nhóm trực tiếp làm user không đánh được.

### 9. Không chạm deploy strategy vòng đầu

Không sửa `src/backend/Core/Attacks.cs` trong vòng này.

Điều kiện mới chạm `Attacks.Run()`:

- Log có `[ATTACK-CS] phase=run_attack status=start`.
- Bot đã vào scout và chọn target.
- `_attacks.Run()` chạy nhưng không thả quân hoặc trận không bắt đầu.

## Thứ tự triển khai khi được phép code

1. Chạy GitNexus impact cho các symbol trong mục impact.
2. Thêm log config và `targetVillage` runtime trước, build nhanh.
3. Đổi `SearchAttack()` có return status và log từng step.
4. Sửa `OneCycle()` dùng kết quả `SearchAttack()` để retry/skip/recovery đúng.
5. Nâng `WaitForScoutScreen()` trả reason hoặc nhận `out string reason`.
6. Bổ sung log home detect score/template nếu đụng `EnsureHomeBase()`.
7. Build và test theo checklist.
8. Trước commit: chạy `gitnexus_detect_changes()`.

## Test/check sau khi sửa code thật

### Build

```powershell
dotnet build E:\Projects\CV-AUT\src\backend\Simplimixi.Backend.csproj
dotnet build E:\Projects\CV-AUT\src\frontend\Simplimixi.csproj
dotnet test E:\Projects\CV-AUT\tests\Simplimixi.Frontend.Tests\Simplimixi.Frontend.Tests.csproj
```

### Manual smoke test thành công

Config:

```json
{
  "run_session": { "play_mode": "main_village" },
  "attack_mode": "attack",
  "attack": "Dragon_Attack",
  "train_mode": "smart"
}
```

Log phải có chuỗi:

```text
[CONFIG-CS] phase=main_village_config status=loaded ... target_village=main_village attack_mode=attack ...
[FSM-CS] phase=home_check status=success ...
[SEARCH-CS] phase=search_attack status=start ...
[SEARCH-CS] phase=search_attack step=open_attack_menu status=success ...
[SEARCH-CS] phase=search_attack step=find_match status=success ...
[SEARCH-CS] phase=search_attack step=scout_screen status=success ...
[SCOUT-CS] phase=scout status=pending index=1 ...
[ATTACK-CS] phase=select_strategy status=success ...
```

### Manual fail test

- Che popup/event trước khi start: log phải ghi fail step và `action=retry_search_attack` hoặc `action=boot_recovery`.
- Đặt profile `Village_1.json` có `attack_mode=donate_only`: log phải warning `reason=profile_attack_mode_donate_only`.
- Đặt account `targetVillage=night_village`: log phải skip `reason=unsupported_target_village`.
- Tắt mạng/emulator popup connection: log phải `phase=connection_check` và không đánh dấu nhầm là search fail.

## Tiêu chí xong

- User chọn Làng chính + attack mode thì backend không bị kẹt trước scout vì tap mù không kiểm chứng.
- `SearchAttack()` trả success/fail có reason; `OneCycle()` không còn tiếp tục scout loop khi chưa vào scout.
- Fail mở attack/find match/scout được retry 1 lần trước khi reboot.
- Log đủ thông tin để biết lỗi do config, home detect, popup, tap, template, confirm cost, scout timeout, hay next button.
- Không ảnh hưởng donate-only, multi-account switch, wall update sau battle, và `Attacks.Run()`.
