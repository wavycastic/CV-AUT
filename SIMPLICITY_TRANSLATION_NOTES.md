# Simplicity Translation Notes

## Context

Goal hiện tại là dịch ngược các phần cần thiết từ `Simplicity.exe` trong:

`E:\Download\Simplicity v3.50.2-February FIX\Simplicity v3.50.1-February FIX\Simplicity v3.50.1-February FIX`

sang code C# của repo `CV-AUT`, ưu tiên các flow đang dùng trong bot: zoom out làng chính, chụp màn hình, thu hoạch collector, train quân, search/attack, recovery.

## Những Gì Đã Dịch Và Port

### Zoom Out

Simplicity có 2 cơ chế zoom:

- MEmu: dùng `AutoHotkey\v2\zoom_out.ahk`, tìm cửa sổ `MEmu.exe`, attach input thread, post phím `F3` 4 lần, mỗi lần cách nhau 1 giây.
- BlueStacks: dùng module Python `zoom_out.py` gọi `uiautomator2`, connect tới `main.host`, lấy root object rồi gọi `pinch_in(percent=100, steps=20)` nhiều lần.

Đã port vào:

- `CVAutomationFramework.cs`
  - `ZoomOut()` phát hiện MEmu/BlueStacks.
  - MEmu gửi `F3` bằng Win32 `PostMessage` giống AHK.
  - BlueStacks gọi `_adb.PinchInZoomOut(count: 5, durationMs: 450, intervalMs: 350)`.

- `ADBHelper.cs`
  - Thêm chọn đúng ADB device theo host/port thay vì lấy device đầu tiên.
  - Thêm UIAutomator2 JSON-RPC `pinchIn`.
  - Tự tìm `u2.jar` trong folder Simplicity, push lên emulator và start server khi cần.
  - Fallback sang ADB multi-swipe nếu UIAutomator2 lỗi.

Kết quả đã test trước đó:

- `dotnet build` thành công.
- BlueStacks được ADB thấy ở `127.0.0.1:5556`.
- UIAutomator2 phản hồi `pong` trên `127.0.0.1:9008`.
- User xác nhận zoomout đã hoạt động.

### Screenshot Retry

Simplicity module `screenshot_utils.py` chụp màn hình bằng:

`adb -s <host> exec-out screencap -p`

và retry nếu stream lỗi, không decode được ảnh, hoặc ảnh gần như blank.

Đã port vào `ADBHelper.TakeScreenshot()`:

- Retry tối đa 3 lần.
- Đọc cả stdout/stderr.
- Decode bằng OpenCV.
- Kiểm tra blank screen bằng độ lệch chuẩn ảnh grayscale.

### Tap Collectors

Simplicity module `Tap_Collectors.py`:

- Chụp một screenshot.
- Match 3 template:
  - `elixir_collector.png`
  - `DE_collector.png`
  - `gold_collector.png`
- Ngưỡng match: `0.65`.
- Tap tâm match tốt nhất.
- Delay giữa các tap: `0.5s`.

Đã port vào `CVAutomationFramework.CollectResourcesPlaceholder()`:

- Không còn tap cứng 3 tọa độ.
- Dùng template matching grayscale giống Simplicity.
- Dùng template trong `E:\Projects\CV-AUT\Templates`.

### Quick Train

Simplicity module `quick_train.py`:

- Mở Army Window bằng tọa độ `(62, 658)`.
- Validate Army Window bằng ROI `(76, 57, 565, 156)` và template `Smart_Auto_train/army_window.png`, ngưỡng `0.6`.
- Tap mở Army Recipes tại `(777, 90)`.
- Chọn slot:
  - slot 1 ROI `(1364, 189, 1574, 425)`.
  - slot 2 ROI `(1368, 486, 1572, 735)`.
- Match `Smart_Auto_train/use_button.png`, ngưỡng `0.9`.
- Nếu popup `Smart_Auto_train/use_army_recipe_window.png` xuất hiện với ngưỡng `0.9`, tap confirm tại `(972, 584)`.
- Đóng Army Window tại `(1545, 81)`.

Đã port vào:

- `Training.cs`
  - `QuickTrain(int quickSlot = 1)`.
  - `ValidateArmyWindow()`.
  - helper match template trong ROI.

- `CVAutomationFramework.cs`
  - Khởi tạo `_training`.
  - `OneCycle()` đọc `train_mode` và `quick_slot`.
  - Nếu `train_mode == quick` thì gọi `_training.QuickTrain(quickSlot)` mỗi 5 chu kỳ giống flow cũ.

### Smart Train

Simplicity module `smart_train.py`:

- Validate Army Window bằng cùng ROI/template `Smart_Auto_train/army_window.png`.
- Validate troop hiện có bằng ROI `(682, 228, 1573, 383)`:
  - main troop từ config attack: `dragon` hoặc `electro_dragon`.
  - fallback main troop trong `s_troops`.
  - `balloon`.
  - match threshold `0.92`.
- Validate spell bằng ROI `(689, 461, 1250, 600)`:
  - `rage`, `freeze`.
  - match threshold `0.92`.
- Validate siege bằng ROI `(1256, 457, 1554, 608)`:
  - `slammer`.
  - match threshold `0.92`.
- Clear queue nếu thấy `to_train/trash_icon.png` trong các ROI trash:
  - army `(1519, 184, 1570, 231)`.
  - spell `(1197, 408, 1250, 455)`.
  - siege `(1511, 406, 1577, 458)`.
- Train tab/tọa độ:
  - army tab `(1063, 305)`, close `(47, 85)`.
  - spell tab `(1008, 531)`, close `(59, 52)`.
  - siege tab `(1398, 533)`, close `(27, 85)`.
- Tap icon trong tab bằng template `Smart_Auto_train/to_train/*.png`, threshold `0.7`.
- Đo army space bằng template fallback `army_space_0..7`.
- Đo spell space bằng template `Spell_space_6/9/11`.

Đã port vào:

- `Training.cs`
  - `SmartTrain(JsonElement cfg)`.
  - `ValidateTroops()`, `ValidateSpells()`, `ValidateSiege()`.
  - `TrainTroops()`, `TrainSpells()`, `TrainSlammer()`.
  - `ClearIfTrash()`, `TapIconInTab()`, `MeasureArmySpaceSecondary()`, `MeasureSpellSpace()`.

- `CVAutomationFramework.cs`
  - Nếu `train_mode != quick` thì gọi `_training.SmartTrain(cfg)` mỗi 3 chu kỳ.

Lưu ý port:

- Không dùng EasyOCR/Python sidecar. Đã chọn hướng OCR chuyên dụng bằng OpenCV C# để nhẹ và deterministic hơn cho font UI cố định.
- `VisionEngine.TryExtractNumericalMetrics()` trả thêm confidence.
- `Training.ValidateTroops()` và `Training.ValidateSpells()` đọc count `xN` ở góc icon sau khi match template:
  - Nếu OCR đủ tin cậy và count thấp hơn số dự kiến, bot train lại.
  - Nếu OCR không đủ tin cậy, bot fallback về logic template icon + army/spell space để tránh false negative.
- Diagnostic offline bằng `--diagnose-saved-army-window` trên ảnh Army Window đã có cho thấy icon runtime match khoảng `0.85-0.86`; ngưỡng validation đã hạ xuống `0.84`.
- ROI count đã chỉnh sang vùng badge count thực tế (`iconLeft + 24`, `iconTop + 8`) để tránh đọc nhầm ký tự `x`/level trên icon; có normalize riêng cho badge count nếu ký tự `x` vẫn bị OCR thành chữ số.
- Chưa chạy live Smart Train lại sau chỉnh này vì yêu cầu hiện tại không chụp màn hình mới; bước live sau là chạy menu `[6]` khi được phép capture ADB.

### Detect Home Base / Ensure Home Base / Boot Recovery

Simplicity module `detect_home_base.py`:

- Chụp screenshot `home.png`.
- Check `game_setting.png` trong ROI `(1445, 499, 1599, 708)`, threshold `0.7`.
- Check `shop.png` toàn màn hình, threshold `0.7`.
- Fallback OCR chữ `shop` trong ROI `(1408, 826, 1582, 886)`.

Simplicity module `ensure_home_base.py`:

- Retry `detect_home_base()` trong tối đa `50s`.
- Mỗi lần fail chờ `5s`.
- Nếu timeout thì gọi `boot_recovery()`.
- Sau recovery, retry lại với `max_wait=20`.

Simplicity module `boot_recovery.py`:

- `am force-stop com.supercell.clashofclans`.
- `monkey -p com.supercell.clashofclans -c android.intent.category.LAUNCHER 1`.
- Chờ `10s`.
- Tap dismiss popup tại `(146, 487)`.

Đã port vào `CVAutomationFramework.cs`:

- `EnsureHomeBase(int maxWaitSeconds = 50, bool allowBootRecovery = true)`.
- `DetectHomeBase(out string reason)`.
- `BootRecovery()`.
- Helper template matching theo ROI bằng grayscale OpenCV.
- `OneCycle()` tiếp tục gọi `EnsureHomeBase()` ở đầu chu kỳ.

Lưu ý port:

- Đã giữ đúng ROI, threshold, timeout, sleep, package name, và tọa độ dismiss popup.
- Chưa port fallback EasyOCR text `"shop"` vì repo C# hiện chỉ có OCR số chuyên dụng, không có OCR text tổng quát. Hai tín hiệu template chính của Simplicity đã được giữ.

### Connection Popup Visible

Simplicity `main.py` function `connection_popup_visible()`:

- Gọi `take_screenshot(output_path="conn.png")`; `conn.png` ở đây là ảnh chụp tạm.
- Đọc screenshot grayscale.
- Match lần lượt các template popup:
  - `Another_device.png`
  - `Connection_lost.png`
  - `Client_error!.png`
  - `rate_coc.png`
- Threshold chung `0.88`.
- Nếu match thì in template name + score và trả `True`.

Đã port vào `CVAutomationFramework.cs`:

- `ConnectionPopupVisible(out string matchInfo)`.
- `RecoverIfConnectionPopup(string warningMessage)`.
- Gọi `BootRecovery()` và kết thúc cycle hiện tại khi phát hiện popup mất kết nối.
- Gắn check vào các điểm tương ứng trong `OneCycle()`:
  - sau tap cleanup đầu cycle `(140, 606)`;
  - sau train;
  - trong vòng đánh giá/scout nhà đối thủ.

Lưu ý port:

- Repo có thêm `Templates/ui/conn.png`, nên C# cũng hỗ trợ match template này nếu có.
- Không ghi screenshot tạm `conn.png` ra disk vì `ADBHelper.TakeScreenshot()` đã trả `Mat` trực tiếp.

### Search / Scout Flow

Simplicity `main.py`:

- `search_attack()`:
  - tap `(113, 797)`;
  - chờ `0.7s`;
  - tap `(272, 659)`;
  - chờ `0.7s`;
  - tap `(1445, 804)`.
- `search_next()`:
  - tap `(1432, 637)`.
- `wait_for_scout_screen()`:
  - match `end_battle.png` trong ROI `(2, 612, 222, 724)`;
  - threshold `0.7`;
  - timeout mặc định `20s`;
  - khi detect thì chờ thêm `0.6s`.
- `is_next_button_present()`:
  - match `next_button.png` trong ROI `(1291, 563, 1592, 721)`;
  - threshold `0.35`;
  - log score `Next-btn match score`.
- Trong `one_cycle`, nếu không thấy Scout UI hoặc Next button sau 3 lần retry thì `boot_recovery()` và kết thúc cycle.

Đã port vào `CVAutomationFramework.cs`:

- `SearchAttack()`.
- `SearchNext()`.
- `WaitForScoutScreen()`.
- `IsNextButtonPresent()`.
- `OneCycle()` giờ chờ Scout UI, xác nhận Next button rồi mới OCR loot; khi loot thấp gọi `SearchNext()` thay vì tap/sleep cứng.

### Battle End / Return Home / Stats

Simplicity `main.py`:

- `wait_battle_end()`:
  - log `Waiting for battle to finish`;
  - check connection popup và gọi `boot_recovery()` nếu mất kết nối;
  - chờ tối đa `MAX_WAIT_BATTLE = 170s`;
  - polling mỗi `1s`.
- `battle_ended()` trong bản gốc dùng EasyOCR trên ROI `(632, 222, 989, 841)` để tìm các keyword như `victory`, `defeat`, `you got:`, `return home`.
- `get_stars_from_screen()`:
  - chụp màn hình `resources_gained.png`;
  - match `one_star.png` ROI `(518, 90, 747, 316)`;
  - match `two_star.png` ROI `(670, 106, 926, 285)`;
  - match `three_star.png` ROI `(840, 96, 1064, 317)`;
  - threshold `0.4`.
- `gain_resources(stars)`:
  - đọc loot chính:
    - gold `(586, 372, 825, 420)`;
    - elixir `(590, 431, 827, 482)`;
    - dark elixir `(643, 489, 826, 539)`;
  - nếu `stars > 0`, đọc thêm bonus:
    - gold `(1012, 444, 1176, 490)`;
    - elixir `(1016, 493, 1176, 537)`;
    - dark elixir `(1036, 541, 1176, 584)`.
- `return_home()`:
  - tap `(788, 768)`;
  - chờ `3s`.

Đã port vào `CVAutomationFramework.cs`:

- `WaitBattleEnd(CancellationToken token)`.
- `BattleEnded()`.
- `GetStarsFromScreen()`.
- `GainResources(int stars)`.
- `ReturnHome()`.
- `UpdateStats(...)` ghi `profiles/Stats_{villageIdx}.json` theo schema Simplicity: `gold`, `elixir`, `de`, `attacks`, `stars`, `last_update_ts`.
- `OneCycle()` sau `_attacks.Run(...)` giờ dùng flow Simplicity: chờ battle end, đọc sao, đọc resource gained, cập nhật stats nếu `enable_stats=true`, rồi `ReturnHome()`.

Lưu ý port:

- C# hiện chưa có OCR text tổng quát như EasyOCR, nên `BattleEnded()` dùng tín hiệu template của result screen (`ui/resources_gained.png` và star templates) thay cho keyword OCR. Phần `get_stars_from_screen()` và `gain_resources()` vẫn giữ đúng ROI/threshold/tọa độ từ Simplicity.

## File Đã Bóc Từ Simplicity

Đã mở được PyInstaller archive của `Simplicity.exe`.

Các module chính được trích ra dạng raw bytecode trong `simplicity_extract/`:

- `main.pyc.raw`
- `zoom_out.pyc.raw`
- `bluestacks_manager.pyc.raw`
- `home_routine.pyc.raw`
- `Tap_Collectors.pyc.raw`
- `detect_home_base.pyc.raw`
- `ensure_home_base.pyc.raw`
- `boot_recovery.pyc.raw`
- `screenshot_utils.pyc.raw`
- `quick_train.pyc.raw`
- `smart_train.pyc.raw`
- `clan_games.pyc.raw`

File hữu ích nhất hiện tại:

- `simplicity_extract/main_selected.dis.txt`

Nó chứa disassembly chọn lọc của:

- `setup_emulator`
- `one_cycle`
- `bot_loop`
- `run_adb`
- `tap`

## Flow `one_cycle` Của Simplicity

Thứ tự chính đã đọc từ disassembly:

1. `ensure_home_base()`
2. Tap popup/cleanup tại `(140, 606)`
3. `connection_popup_visible()` và `boot_recovery()` nếu cần
4. `multi_zoom_out(host)`
5. Nếu bật Clan Capital:
   - `clan_capital(cfg)`
   - recovery/home/zoom lại khi cần
6. Train quân:
   - nếu `train_mode == quick`: `quick_train(cfg, quick_slot)`
   - ngược lại: `smart_train(cfg)`
7. `handle_home_resources(...)`
8. `run_events_open(host)` cho Clan Games/event
9. `auto_request()`
10. `find_and_tap_collectors()`
11. `search_attack()`
12. `wait_for_scout_screen()`
13. Check target resources.
14. Nếu đạt target: `run_attack(...)`
15. `wait_battle_end()`
16. `get_stars_from_screen()`
17. `gain_resources(stars)`
18. `return_home()`
19. Recovery nếu có connection popup.

## Việc Cần Làm Tiếp Theo

### 1. Dịch `clan_games.py`

Simplicity có flow mở event, tìm Clan Games, chọn task, start task.

Port sau khi home/recovery/search đã ổn.

### 2. Dịch Clan Capital

Clan Capital nằm trong `main.py`, logic lớn và nhiều template/ROI.

Nên để sau cùng vì blast radius lớn:

- phase 1: tìm Capital scenery
- phase 2: validate trang Clan Capital
- phase 3: tìm village còn attack
- phase 4: start attack và drop troop/spell

## Lưu Ý Khi Dịch Tiếp

- Ưu tiên port từng module nhỏ, build và test ngay.
- Giữ nguyên thông số từ Simplicity trước khi tối ưu:
  - threshold
  - tọa độ tap
  - delay
  - số retry
- Không đổi đồng thời nhiều flow lớn.
- Sau mỗi module nên chạy:

```powershell
dotnet build
```

- Với phần cần test live, ưu tiên thêm option test riêng trong `Program.cs` trước khi đưa vào `OneCycle`.

## Trạng Thái Build Gần Nhất

`dotnet build` đã thành công sau khi port `quick_train.py` và `smart_train.py`.

Warning còn lại:

- `NU1603`: package `SharpAdbClient >= 2.3.0.22` không có đúng version, NuGet resolve sang `SharpAdbClient 2.3.3`.

Warning này không chặn build.
