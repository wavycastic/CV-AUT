# Plan port Làng đêm / Builder Base theo phase

> Mục tiêu: hoàn tất phần Builder Base/Làng đêm từ MBR sang repo hiện tại theo từng phase,
> tránh nhồi một lần vào `CVAutomationFramework` và giữ flow hiện tại ổn định.

## 1. Mục tiêu tổng quát

- Port đủ các luồng Builder Base từ MBR sang backend C#.
- Ưu tiên phần ảnh hưởng trực tiếp đến gameplay trước.
- Tách rõ phần nào đã có, phần nào chỉ là bản giản lược, phần nào chưa port.
- Không phá flow Làng chính đang chạy ổn định.

## 2. Hiện trạng rút gọn

### Đã có trong repo hiện tại

- Detect / switch Builder Base
- Report cơ bản
- Collect cơ bản
- Clock Tower boost cơ bản
- Army prep cơ bản
- Attack cơ bản
- Maintenance gộp: clean yard, suggested upgrades, star laboratory, hero upgrade, BOB upgrades

### Chưa port đầy đủ hoặc mới port một phần

- `PrepareAttackBB`
- `AttackBB`
- `CollectElixirCart`
- `StarLaboratory`
- `SuggestedUpgrades`
- `UpgradeBattleMachine`
- `UpgradeBattleCopter`
- `BOBBuildingUpgrades`
- các nhánh recovery / retry / bonus / challenge complete / cloud handling

## 3. Kế hoạch theo phase

### Phase 1 — Baseline ổn định

Mục tiêu:

- Khóa scope Builder Base flow tối thiểu nhưng chạy ổn.
- Xác định rõ chỗ nào đang thiếu asset/template để tránh click mù.

Việc làm:

- Rà lại toàn bộ mapping MBR ↔ C#.
- Gắn log chuẩn cho từng bước của cycle Builder Base.
- Đảm bảo detect/switch/report/collect/attack skeleton hoạt động.
- Bổ sung danh sách template còn thiếu nếu cần.

Done when:

- Cycle Builder Base chạy end-to-end mà không crash.
- Có log rõ từng phase.
- Không ảnh hưởng `main_village`.

#### Phase 1 checklist thực tế

- [x] Xác nhận `CVAutomationFramework.OneBuilderBaseCycle(...)` là entry point của flow Builder Base.
- [x] Xác nhận có các module nền: navigator, report, army manager, attacks, clock tower, maintenance.
- [x] Xác nhận log đã có prefix riêng theo phase: `[BB-CS]`, `[BB-REPORT]`, `[BB-ARMY]`, `[BB-ATTACK]`, `[BB-CLOCK]`, `[BB-MAINT]`.
- [x] Xác nhận flow có guard để quay về `main_village` sau Builder Base.
- [x] Xác nhận config `night_village` đã có các cờ điều khiển chính: attack, army management, boost clock tower, upgrade wall, maintenance options.
- [x] Xác định các template/asset còn thiếu cần hoàn thiện ở phase sau.

#### Phase 1 mapping nhanh

| MBR ref | Backend hiện tại | Trạng thái |
| --- | --- | --- |
| `Collect.au3` | `BuilderBaseResources` + `OneBuilderBaseCycle` | partial |
| `StartClockTowerBoost.au3` | `BuilderBaseClockTower.TryBoost` | partial |
| `PrepareAttackBB.au3` | `BuilderBaseArmyManager.EnsureReadyForAttack` | partial |
| `AttackBB.au3` | `BuilderBaseAttacks.RunSingleAttack` | partial |
| `SuggestedUpgrades.au3` | `BuilderBaseMaintenance.SuggestedUpgrades` | partial |
| `StarLaboratory.au3` | `BuilderBaseMaintenance.TryStartStarLaboratoryResearch` | partial |
| `UpgradeBattleMachine.au3` | `BuilderBaseMaintenance.TryUpgradeHero` | partial |
| `UpgradeBattleCopter.au3` | `BuilderBaseMaintenance.TryUpgradeHero` | partial |
| `BOBBuildingUpgrades.au3` | `BuilderBaseMaintenance.TryBobUpgrades` | partial |
| `Report/Harvest/Detect` helpers | `BuilderBaseNavigator` + `BuilderBaseReport` | present |

#### Phase 1 gaps còn để phase sau

- `CollectElixirCart`
- logic attack chuẩn MBR cho `PrepareAttackBB` / `AttackBB`
- `StarLaboratory` chi tiết hơn
- `SuggestedUpgrades` theo rule MBR đầy đủ
- tách riêng hero/building upgrade rule chi tiết hơn

### Phase 2 — Port attack flow đầy đủ

Mục tiêu:

- Mang phần chiến đấu từ MBR sang gần đúng nhất.

Việc làm:

- Port đầy đủ logic `PrepareAttackBB`.
- Port đầy đủ logic `AttackBB`.
- Bổ sung xử lý:
  - loot/stars availability
  - trophy range
  - cloud / wait / retry
  - obstacle / obstructed layout
  - end battle / return home / surrender
  - stage 2

Done when:

- Attack flow tương đương MBR ở mức hành vi chính.
- Có thể lặp nhiều trận ổn định.

#### Phase 2 checklist thực tế

- [x] `BuilderBaseAttacks.RunSingleAttack(...)` có retry khi mở attack / start battle.
- [x] Có wait/retry cho troop bar trước khi deploy, tránh deploy mù.
- [x] Có `Find Now` handling, cloud wait/retry tới 30 vòng và retry ở mốc 21 giống hướng MBR.
- [x] Có obstructed-layout detection và log rõ trước khi tiếp tục safe drop.
- [x] Có handling stage 2 khi damage đạt 100% và redeploy phần còn lại.
- [x] Có handling popup bonus / challenge complete / return-home templates.
- [x] Có surrender fallback và retry return-home sau timeout hoặc khi damage đứng yên lâu.
- [x] Có config guard tương đương `PrepareAttackBB`: loot availability, trophy range, force clan-games attack flag, storage-full guard log.
- [x] Build backend pass sau thay đổi Phase 2.

#### Phase 2 còn chưa port 1:1 MBR

- [ ] `GetAttackBarBB` đã có banner/slot scan và giữ state `_startSlotMem/_startSlotMem2` gần `$iStartSlotMem/$iStartSlotMem2`, nhưng slot detection vẫn dựa template/OCR thay vì toàn bộ QuickMIS + pixel banner của AutoIt.
- [ ] `DeployBBTroop` đã có vector-out-zone 101 điểm gần `_GetVectorOutZone(...)`, nhưng chưa port exact ExternalArea/red-area dynamic polygon.
- [ ] `CheckBMLoop` / `CheckBomberLoop` đã có pixel-state heuristics gần MBR hơn, nhưng vẫn chưa 100% exact theo banner color/ability state của AutoIt.
- [ ] `CheckCGCompleted` đã có complete-bar heuristic + yellow pixel sampling, nhưng chưa port đầy đủ 12-step exact pixel counter như MBR.

### Phase 3 — Port maintenance chi tiết

Mục tiêu:

- Làm phần hậu cần Builder Base gần với MBR hơn.

Việc làm:

- Port `CollectElixirCart`.
- Làm lại `SuggestedUpgrades` theo row/icon/cost giống MBR hơn.
- Làm lại `StarLaboratory` với:
  - OCR thời gian
  - chọn troop theo cost
  - handling max / unavailable / no loot
- Tách rõ `UpgradeBattleMachine` và `UpgradeBattleCopter`.
- Tách logic BOB upgrades theo building riêng.

Done when:

- Các tác vụ maintenance không còn là generic fallback quá nhiều.
- Có kiểm tra level / cost / trạng thái riêng cho từng nhánh.

#### Phase 3 checklist thực tế

- [x] `CollectElixirCart` đã nằm trong `BuilderBaseResources.Collect(...)`, chỉ claim khi thấy template cart thật; các claim template chỉ dùng làm nút sau khi xác nhận cart.
- [x] `SuggestedUpgrades` có phân loại resource row gold/elixir/new-building, guard ignore gold/elixir/hall và log resource rõ hơn.
- [x] `SuggestedUpgrades` đã tách candidate selection riêng, đọc OCR cost theo row, sort cheapest/score và dùng confirm template theo đúng resource candidate thay vì biến fallback chung.
- [x] `StarLaboratory` có guard busy/max/unavailable/no-elixir, OCR timer/cost, chọn troop theo config hoặc fallback auto, kiểm tra cost trước khi research.
- [x] `StarLaboratory` đã có persistence vào `profiles/Village_N.json` cho vị trí lab, level OCR, thời gian research finish UTC và last-checked; locate dùng stored coordinate trước rồi validate bằng Research button + OCR level, fallback template locate nếu stored coordinate sai.
- [x] `StarLaboratory` đã có bảng 12 troop theo MBR (`Raged Barbarian` tới `Electrofire Wizard`), alias template/key, grid fallback coordinate và pixel-state mapping chi tiết hơn cho not-unlocked/no-loot/max/lab-required.
- [x] `StarLaboratory` đã có OCR helper tách riêng cho cost/time, debug screenshot runtime, và log vị trí lưu ảnh StarLabUpgrade khi bật `star_laboratory_debug_screenshots` hoặc `debug_screenshots`.
- [x] `UpgradeBattleMachine` và `UpgradeBattleCopter` dùng template target riêng và log hero riêng.
- [x] `BOB` upgrades chạy theo danh sách target building riêng thay vì một fallback gộp.

#### Phase 3 còn chưa port 1:1 MBR

- [ ] `StarLaboratory.au3` đã có lưu thời gian upgrade/profile persistence, locate/validate lab bằng stored coordinate + OCR level, bảng 12 troop, chọn candidate/cheapest theo cost OCR và pixel-state guard; vẫn chưa port GUI status update, reset combo về Any sau upgrade user-choice và OCR/time/resource + saved-state parity hoàn toàn giống MBR.
- [ ] `SuggestedUpgrades` đã có candidate/cost/resource selection tốt hơn nhưng chưa tái tạo đầy đủ rule row/icon/cost MBR cho từng loại công trình và chưa có per-building priority giống GUI MBR.

### Phase 4 — Nâng chất lượng và khớp hành vi MBR

Mục tiêu:

- Giảm độ lệch giữa MBR và backend C#.

Việc làm:

- So khớp log event giữa MBR và C#.
- So khớp ROI/template cho các màn Builder Base.
- Tinh chỉnh threshold / retry / fallback.
- Chuẩn hóa stats cập nhật sau từng tác vụ.

Done when:

- Hành vi chính khớp đủ sát để thay thế MBR trong thực tế.

#### Phase 4 checklist thực tế

- [x] Chuẩn hóa log các module Builder Base theo prefix `[BB-CS]`, `[BB-REPORT]`, `[BB-ARMY]`, `[BB-ATTACK]`, `[BB-CLOCK]`, `[BB-MAINT]`, `[BB-STATS]`.
- [x] Có asset audit baseline (`phase=asset_audit`) để ghi rõ template còn thiếu thay vì giả định asset đã có.
- [x] Các nhánh attack/maintenance quan trọng có retry/fallback và log reason/action.
- [x] Stats Builder Base cập nhật sau attack/maintenance.

### Phase 5 — Dọn cấu trúc và tách module

Mục tiêu:

- Giảm coupling của Builder Base trong backend.

Việc làm:

- Tách các service lớn thành module nhỏ nếu cần.
- Tách config riêng cho Builder Base nếu thấy quá nhiều tham số.
- Gom utility dùng chung cho detect / OCR / tap.

Done when:

- Builder Base code dễ bảo trì hơn, ít phụ thuộc chéo.

#### Phase 5 checklist thực tế

- [x] Builder Base đã tách thành các module riêng: navigator, report, resources, army manager, attacks, clock tower, wall updater, maintenance.
- [x] `CVAutomationFramework.OneBuilderBaseCycle(...)` giữ vai trò orchestration, không nhét chi tiết attack/maintenance vào cycle chính.
- [x] Các option Builder Base được gom theo record option/result cho army, battle, maintenance.
- [x] Flow Builder Base vẫn guard quay về `main_village` sau cycle để không phá Làng chính.

## 4. Ưu tiên thực hiện

1. Phase 1
2. Phase 2
3. Phase 3
4. Phase 4
5. Phase 5

## 5. Tiêu chí chấp nhận

- Không làm hỏng luồng Làng chính.
- Builder Base có thể chạy riêng và ổn định.
- Không còn phụ thuộc vào các nhánh MBR chưa port nhưng đang được giả định là có.
- Các tác vụ lớn có log đủ để debug.

## 6. Ghi chú kỹ thuật

- Không nên port nguyên xi AutoIt.
- Ưu tiên giữ kiến trúc hiện tại của C#.
- Nếu asset/template chưa có thì ghi rõ vào checklist, không giả lập bằng click cứng.
- Các nhánh attack/maintenance nên chạy có guard và fallback an toàn.
