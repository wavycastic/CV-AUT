# Roadmap chính — CoC Auto Bot (Avalonia)

Nguồn: `Đặc Tả Thiết Kế Giao Diện — CoC Auto Bot (Avalonia d8d6acda36ea4c72a4cec462b395c53e.md`

## Nguyên tắc bắt buộc

- Runtime state luôn scoped theo `DeviceId`.
- `DeviceViewModel` + `DevicePanelView` là lõi UI, tái dùng cho single/grid.
- ViewModel không gọi ADB trực tiếp; chỉ qua `IDeviceSession`.
- TopBar luôn hiển thị: Start/Pause/Stop All, running summary, theme/lang, license badge.
- License gate kiểm tra trước khi mở app chính.
- Không tạo singleton runtime state cho status/stats/logs/account.

## Phase 0 — Nền móng kỹ thuật

### Mục tiêu
Chuẩn hóa project, DI, base MVVM, naming, theme, layout host.

### Việc cần làm
- Tạo/chuẩn hóa structure:
  - `Views/Shell`, `Views/Dashboard`, `Views/Settings`, `Views/Accounts`, `Views/Advanced`, `Views/Logs`, `Views/License`
  - `ViewModels/*` tương ứng
  - `Services`, `Models`
- Cài stack FE:
  - Avalonia UI
  - CommunityToolkit.Mvvm
  - Microsoft.Extensions.DependencyInjection
  - Fluent Theme dark mode
  - icon lib
- Implement:
  - `ViewModelBase`
  - `ViewLocator`
  - DI composition root
  - `AppStateService`
  - model cơ bản: `Device`, `BotStatus`, `SessionStats`, `LogEntry`

### DoD
- App chạy skeleton.
- View ↔ ViewModel mapping hoạt động.
- DI resolve được MainWindow/MainWindowViewModel.

## Phase 1 — Shell + 1 thiết bị, kiến trúc multi-ready

### Mục tiêu
Có app shell hoàn chỉnh, dashboard single device, session abstraction.

### Việc cần làm
- Shell:
  - `MainWindow.axaml`
  - `TopBarView`
  - `SidebarView`
  - `ContentControl CurrentPage`
- ViewModel tree:
  - `MainWindowViewModel`
  - `TopBarViewModel`
  - `SidebarViewModel`
  - `ObservableCollection<DeviceViewModel> Devices`
  - `ActiveDevice`
  - `IsGridMode`
- Dashboard single:
  - `DashboardView`
  - `DevicePanelView`
  - `DeviceListItemView`
  - `SessionStatsViewModel`
- BE bridge:
  - `IDeviceSession`
  - `IDeviceSessionManager`
  - `DeviceSessionManager` với 1 session đầu tiên
  - mock session nếu BE thật chưa sẵn
- Settings cơ bản:
  - `MainVillageView`
  - `NightVillageView`
  - `ClanGamesView`
- Accounts cơ bản:
  - list account
  - add/edit/delete UI skeleton

### DoD
- TopBar luôn hiện khi đổi page.
- Start/Pause/Stop gọi qua `DeviceViewModel` → `IDeviceSession`.
- Single mode render `ActiveDevice` bằng `DevicePanelView`.
- Không hardcode state runtime ngoài `DeviceViewModel`.

## Phase 2 — Vận hành đầy đủ 1 thiết bị

### Mục tiêu
Một thiết bị chạy đủ luồng: config, profile, logs, stats, wizard.

### Việc cần làm
- Settings đầy đủ:
  - Làng Chính: Attack/Auto Donate, Rồng/Rồng Điện, tài nguyên theo tổng/theo loại + AND/OR, Dầu Đen, xin lính, nâng tường, đầu hàng thông minh
  - Làng Đêm: chỉ farm dầu/farm vàng dầu/tự động, cup min/max, nâng tường
  - Trò Chơi Hội: chọn làng, filter nhiệm vụ, lưu bộ lọc
- Advanced:
  - delay config
  - `CoordinateEditorView`
  - undo/clear/save
  - 4 hướng × 3 loại tọa độ
- Profile:
  - dropdown profile ở TopBar/Settings
  - save new/update/delete/load
  - `IConfigStore`
- Logs:
  - realtime per-device buffer
  - filter level/device
  - auto-scroll
  - copy/export
- Stats:
  - battles, stars, loot, wall upgrades, clan games points/tasks
- Setup Wizard:
  - chọn giả lập
  - autodetect resolution/dpi
  - cảnh báo khác 1600x900/240dpi
  - hướng dẫn ADB
  - chạy thử 1 trận
- Emulator discovery:
  - `IEmulatorDiscovery`
  - detect ADB devices

### DoD
- 1 thiết bị có thể start → log/stats realtime → stop.
- Config load/save ổn.
- Wizard đưa user mới tới trạng thái sẵn chạy.
- UI update realtime qua `Dispatcher.UIThread.Post`.

## Phase 3 — Multi-device thật

### Mục tiêu
Nhiều thiết bị chạy song song, không sửa lõi `DevicePanelView`/`DeviceViewModel`.

### Việc cần làm
- Grid mode:
  - `ItemsControl ItemsSource=Devices`
  - `UniformGrid`/responsive container
  - card compact qua class/data trigger
- Multi-session:
  - `DeviceSessionManager.GetOrCreate(deviceId)`
  - mỗi device 1 `IDeviceSession`
  - mỗi session 1 ADB connection/event stream
- TopBar:
  - `StartAllCommand`
  - `PauseAllCommand`
  - `StopAllCommand`
  - chạy song song bằng task per device
  - `RunningSummary` = running/total
- Logs multi:
  - buffer riêng từng máy
  - filter theo device/level
- Sidebar devices:
  - trạng thái chấm màu
  - chọn `ActiveDevice`
  - thêm/xóa/refresh thiết bị
- Config apply:
  - apply active device
  - apply all

### DoD
- Thêm thiết bị thứ 2 không cần sửa `DevicePanelView`/`DeviceViewModel`.
- Single ↔ Grid chỉ đổi container.
- Start All chạy song song nhiều session.
- Logs/stats không lẫn giữa devices.

## Phase 4 — License/Key system

### Mục tiêu
Chặn app bằng license hợp lệ, hỗ trợ bán/gia hạn/thu hồi.

### Việc cần làm
- License gate:
  - app start → `ILicenseService.VerifyAsync()`
  - invalid/not activated/expired → `ActivationView`
  - active → `MainWindow`
- Models:
  - `LicenseStatus`
  - `LicenseInfo`
- Services:
  - `ILicenseService`
  - activate/verify/deactivate
  - machine id hash
  - cache license signed
  - grace period offline
- UI:
  - `ActivationView`
  - `ActivationViewModel`
  - TopBar license badge
  - badge level green/yellow/red
  - renew/open activation command
- Server contract:
  - activate key
  - verify key
  - revoke key
  - bind key ↔ machineId
  - signed response

### DoD
- `MainWindow` không hiện nếu license invalid.
- TopBar hiện “Còn X ngày”.
- Verify lại định kỳ + mỗi lần Start bot.
- Không lưu bool “activated” plain local.

## Phase 5 — Khác biệt hóa

### Mục tiêu
Tạo lợi thế vượt AutoCOC: vận hành nhiều máy, thông báo, dashboard tổng hợp, an toàn hơn.

### Việc cần làm
- Telegram/Discord notification:
  - bot stopped/error
  - low license days
  - session summary
- Dashboard tổng hợp đa máy:
  - tổng loot
  - uptime
  - running/offline/error count
  - performance per device
- Anti-ban/randomize:
  - random delay
  - random click offset
  - behavior variance profile
- Pricing readiness:
  - license theo thời hạn
  - license theo max device count

### DoD
- User quản trị nhiều máy từ 1 dashboard.
- Notification hoạt động.
- License có tier max device.

## Backlog sản phẩm

- Thêm combo quân theo meta.
- Export/import profile.
- Backup/restore config.
- Per-account analytics.
- Crash recovery/resume session.
- Auto update app.
- Remote control web/mobile sau này.

## Checklist kiểm soát kiến trúc

- [ ] Mọi runtime state nằm trong `DeviceViewModel`, key theo `DeviceId`.
- [ ] `DevicePanelView` lấy dữ liệu từ DataContext, không truyền `DeviceId` xuyên tầng.
- [ ] Component con không gọi ADB/BE trực tiếp.
- [ ] Mỗi thiết bị có log buffer riêng.
- [ ] Layout co giãn; không hardcode viewport.
- [ ] Realtime event marshal về UI thread.
- [ ] Start All/Stop All ở TopBar từ đầu.
- [ ] License verify server-side, có chữ ký số.
- [ ] Không hardcode key trong client.

## Phụ lục — Backend gap → `IDeviceSession`

Trạng thái backend hiện tại (`src/backend/Core`) và việc cần làm để lên tầng session theo spec §8. Đây là công việc Phase 1 (bridge), không phải Phase 0.

### Backend đã có (giữ nguyên)

- `ADBHelper` instance-based, ctor `(host, port, serial)`, có `DeviceAddress` → multi-device khả thi ở tầng ADB.
- `CVAutomationFramework` giữ `_adb`/`_vision`/`_training`/`_attacks`/`_wallUpdater` là instance field → logic device-isolated.
- Lifecycle đủ: `Start/Stop/Pause/Resume` + `Completion` + `_pauseEvent` + `_cts`.
- Session tracking: `_sessionBattlesCompleted`, `_sessionStartedAt`, `UpdateStats`, `ReadClanGamesPoints`.

### Lệch so spec §8 (cần bù)

| # | Spec §8 | Backend hiện tại | Việc cần làm |
|---|---------|------------------|--------------|
| 1 | `string DeviceId` | Runner vô danh; chỉ `ADBHelper.DeviceAddress` | Expose `DeviceId` lên session (derive từ host:port hoặc serial) |
| 2 | `StartAsync(VillageConfig, ct)` + `Pause/Stop/ResumeAsync` | `void Start/Stop/Pause/Resume` (sync) | Wrap async quanh `IAutomationRunner` |
| 3 | `StatusChanged` event | `_isRunning` field nội bộ | Phát `BotStatus` qua event |
| 4 | `LogReceived` event | `Console.WriteLine` global (`AppLog` tee) | Đổi sang callback/event mang `DeviceId` |
| 5 | `StatsUpdated` event | Ghi file đĩa theo `villageIdx` | Phát `SessionStats` qua event, key theo `DeviceId` |
| 6 | `VillageConfig` object | ctor nhận `configPath` (file) | Nhận config object hoặc file riêng mỗi session |
| 7 | `BotStatus` type | không có | Định nghĩa enum/record `BotStatus` |
| 8 | `IDeviceSession` / `IDeviceSessionManager` | không có | Tạo 2 interface + impl |

### Blocker thật cho multi-device (fix trước Phase 3)

- [ ] **Log global**: `AppLog` tee toàn bộ `Console` → 2 session chạy song song log trộn lẫn. Đổi sang per-device event/callback mang `DeviceId`.
- [ ] **Stats file collision**: `StatsFilePath(villageIdx)` → 2 máy cùng village ghi đè nhau. Key theo `DeviceId`.
- [ ] **Config file-driven**: mỗi session cần file riêng hoặc refactor nhận `VillageConfig` object.

### Không phải blocker

- ADB layer (instance, per-device) — OK.
- Vision/attack/training logic (instance field) — OK.
- `WritableLogsDirectory` static — chỉ thư mục gốc, không đụng nhau.

### Thứ tự an toàn

1. Phase 0: DI + Models (`Device`, `BotStatus`, `SessionStats`, `LogEntry`) + `AppStateService`. Giữ prototype chạy.
2. Phase 1: bọc `AutomationRunner` thành `IDeviceSession` (gắn `DeviceId`, async wrap, đổi Console → event), thêm `IDeviceSessionManager` 1 session.
3. Trước Phase 3: fix 3 blocker trên (log global, stats collision, config file).
