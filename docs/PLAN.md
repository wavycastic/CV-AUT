# CV-AUT — Kế hoạch chuyển đổi giao diện: Gaming Sidebar dọc

> Tài liệu thực thi cho AI agent / dev. Mục tiêu: **chuyển đổi toàn bộ giao diện
> từ layout ngang (1040×700, FluentAvalonia + shadcn/ui) sang layout dọc
> (380×720, Avalonia SimpleTheme + Custom Gaming Console).**  
> Đọc xong file này là đủ để làm mà không cần hỏi lại.

---

## 0. TL;DR cho agent

- **Xóa** FluentAvalonia (`FluentAvaloniaUI` NuGet, `FAAppWindow`, `FluentAvaloniaTheme`).
- **Thay bằng** Avalonia built-in `SimpleTheme` + bộ Design Token tự custom.
- **Chuyển layout** từ ngang `1040×700` (Sidebar trái + TopBar + Content) sang
  dọc `380×720` (Header nhỏ gọn ở trên → danh sách thiết bị cuộn dọc → Bottom Nav).
- **Bỏ** tab Cài đặt chung (đã xong). Cấu hình chỉ qua từng instance (nút Config trên mỗi device).
- **Bảng màu mới**: Dark Gaming Console lấy cảm hứng từ tài nguyên Clash of Clans
  (Gold / Elixir / Dark Elixir) trên nền đen carbon.
- Stack: Avalonia `12.0.5`, `net10.0-windows`, MVVM Toolkit `8.4.2`, DI,
  `Material.Icons.Avalonia`. Namespace gốc `CvAut`. Project
  `src/frontend/Simplimixi.csproj`.
- Native AOT: `PublishAot=true`, `TrimMode=full`,
  `AvaloniaUseCompiledBindingsByDefault=true`. Cấm reflection.
- Trước khi sửa symbol: `gitnexus_impact`. Trước khi commit:
  `gitnexus_detect_changes`.

---

## 1. Tại sao chuyển đổi

| Vấn đề hiện tại | Giải pháp |
|---|---|
| Layout ngang chiếm toàn bộ màn hình → che giả lập game | Layout dọc 380px nép bên cạnh giả lập |
| FluentAvalonia mang phong cách Windows 11 / văn phòng → không hợp tool game | Custom Gaming Theme tối, viền neon, cảm giác "command center" |
| shadcn/ui (web-oriented) không tạo được bản sắc riêng cho tool CoC | Bảng màu lấy cảm hứng trực tiếp từ Clash of Clans |
| Tab Cài đặt chung + Cài đặt Instance gây nhầm lẫn | Chỉ còn cấu hình per-instance, truy cập từ Dashboard |
| Sidebar trái + TopBar ngang chiếm quá nhiều diện tích | Bottom Navigation Bar (4 icon) + Header compact |

---

## 2. Thiết kế mới: Gaming Sidebar dọc

### 2.1 Kích thước cửa sổ

```
Width:    380px  (min 340, max 420)
Height:   720px  (min 600, max ∞)
Resize:   Chỉ cho phép kéo dọc (chiều cao). Chiều ngang cố định hoặc hạn chế.
Position: Mặc định neo góc phải màn hình (CenterScreen → thay bằng Manual, tính toạ độ).
```

### 2.2 Cấu trúc layout (từ trên xuống dưới)

```
┌──────────────────────────────┐
│  HEADER (48px)               │  Logo + Tên app + Nút License/Minimize
│  ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─  │
│  STATUS BAR (32px)           │  ● 3 running • ★ 12 sao • ⚠ 0 lỗi
├──────────────────────────────┤
│                              │
│  CONTENT AREA (flex)         │  Tuỳ theo tab đang chọn ở Bottom Nav:
│                              │  - Dashboard: danh sách device cuộn dọc
│                              │  - Thiết lập: Setup wizard
│                              │  - Nâng cao: Advanced tuning
│                              │  - Nhật ký: Log viewer
│                              │
├──────────────────────────────┤
│  BOTTOM NAV (56px)           │  4 icon: Dashboard | Thiết lập | Nâng cao | Nhật ký
└──────────────────────────────┘
```

### 2.3 Dashboard dọc — Danh sách thiết bị

Mỗi thiết bị là một **Card dọc** xếp chồng lên nhau (vertical scroll):

```
┌─────────────────────────────┐
│ 📱 LDPlayer-1               │  Tên + loại giả lập
│ ● Running  •  Làng chính    │  Trạng thái + Chế độ chơi
│                              │
│  ★ 5   🏆 3200   ⚔ 12      │  Sao / Cup / Số trận
│  🪙 1.2M  💎 800K  🛢 45K   │  Gold / Elixir / Dark
│                              │
│ [▶ Start] [⚙ Config] [📋]  │  Nút điều khiển compact
└─────────────────────────────┘
```

### 2.4 Cấu hình Instance (khi nhấn Config)

Chuyển sang chế độ toàn màn hình dọc (ẩn danh sách device, hiện cấu hình):

```
┌──────────────────────────────┐
│  ← Quay lại    Cấu hình      │  Header với nút Back + Lưu
│  ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─  │
│  ▸ Cấu hình tấn công         │  Accordion section (mở/đóng)
│    Template: [Dragon_Att ▾]  │
│    Target:   [Tổng TN    ▾]  │
│    ☑ Tự động đánh             │
│    ☐ Tự động xin quân        │
│    ...                        │
│  ▸ Ngưỡng tài nguyên          │  Accordion section
│  ▸ Cấu hình nâng tường        │  Accordion section
│  ▸ Đầu hàng thông minh        │  Accordion section
│                              │
│        [Lưu]    [Hủy]        │  Nút hành động ở dưới cùng
└──────────────────────────────┘
```

---

## 3. Bảng màu Gaming Console — Clash of Clans

### 3.1 Nền & Bề mặt (Deep Carbon Black)

| Token | Hex | Mô tả |
|---|---|---|
| `AppBackgroundColor` | `#0a0a0c` | Nền chính — đen carbon siêu sâu |
| `AppSurfaceColor` | `#131316` | Card / Panel — xám than đen |
| `AppSurfaceAltColor` | `#1c1c21` | Tile / Input background |
| `AppSurfaceHoverColor` | `#2a2a31` | Hover state |
| `AppBorderColor` | `#1e1e24` | Viền card mờ |
| `AppBorderStrongColor` | `#2d2d36` | Viền active / focus |
| `AppConsoleColor` | `#050508` | Nền log console |

### 3.2 Màu nhấn — Tài nguyên Clash of Clans

| Token | Hex | Mô tả |
|---|---|---|
| `AppAccentColor` | `#d4af37` | **Gold** — màu chủ đạo, nút primary, tiêu đề |
| `AppAccentHoverColor` | `#e6c44a` | Gold sáng hơn khi hover |
| `AppAccentSoftColor` | `#1a1608` | Gold tối cho nền nhấn nhẹ |
| `AppAccentTextColor` | `#0a0a0c` | Chữ trên nền Gold (đen) |
| `AppElixirColor` | `#d946ef` | **Elixir Pink** — chỉ số dầu hồng |
| `AppElixirSoftColor` | `#1a0a1e` | Elixir nền mờ |
| `AppDarkElixirColor` | `#8b5cf6` | **Dark Elixir Purple** — chỉ số dầu đen |
| `AppDarkElixirSoftColor` | `#0f0a1e` | Dark Elixir nền mờ |

### 3.3 Trạng thái (Semantic)

| Token | Hex | Mô tả |
|---|---|---|
| `AppSuccessColor` | `#22c55e` | Bot đang chạy — xanh lá neon |
| `AppWarningColor` | `#f59e0b` | Tạm dừng — vàng amber |
| `AppDangerColor` | `#ef4444` | Lỗi — đỏ |
| `AppInfoColor` | `#06b6d4` | Thông tin — cyan |

### 3.4 Text

| Token | Hex | Mô tả |
|---|---|---|
| `AppTextPrimaryColor` | `#f0f0f2` | Chữ chính — trắng ấm |
| `AppTextSecondaryColor` | `#a0a0aa` | Chữ phụ |
| `AppTextMutedColor` | `#55555e` | Chữ mờ — timestamp, caption |

---

## 4. Loại bỏ FluentAvalonia — Chi tiết kỹ thuật

### 4.1 Gỡ NuGet package

```xml
<!-- XÓA dòng này trong Simplimixi.csproj -->
<PackageReference Include="FluentAvaloniaUI" Version="3.0.0" />
```

### 4.2 Thay FAAppWindow → Window

| File | Thay đổi |
|---|---|
| `Views/MainWindow.axaml` | `<Windowing:FAAppWindow ...>` → `<Window ...>`. Xóa xmlns Windowing |
| `Views/MainWindow.axaml.cs` | `using FluentAvalonia.UI.Windowing;` → xóa. `FAAppWindow` → `Avalonia.Controls.Window` |
| `App.axaml` | Xóa `xmlns:sty="using:FluentAvalonia.Styling"`. Xóa `<sty:FluentAvaloniaTheme .../>` |

### 4.3 Thay bằng SimpleTheme

```xml
<!-- App.axaml — Application.Styles -->
<SimpleTheme />
<materialIcons:MaterialIconStyles />
<!-- ...rồi đến toàn bộ custom styles hiện có (đã tự override hết) -->
```

> **Lưu ý quan trọng:** Hiện tại App.axaml đã custom override hầu hết các control
> (Button, TextBox, ComboBox, CheckBox, Border.Card, Border.Tile...) nên việc
> chuyển từ FluentAvaloniaTheme sang SimpleTheme sẽ không ảnh hưởng nhiều đến
> giao diện — các style tự viết sẽ ghi đè lên base theme.

---

## 5. Chuyển đổi layout — Chi tiết từng file

### 5.1 MainWindow.axaml — Từ ngang sang dọc

**Hiện tại:**
```
Grid: ColumnDefinitions="220,*" RowDefinitions="Auto,*"
      TopBar (span 2 cột) | Sidebar (cột 0) | Content (cột 1)
```

**Mới:**
```
Grid: RowDefinitions="Auto,Auto,*,Auto"
      Row 0: Header compact (48px)
      Row 1: Status bar (32px)
      Row 2: Content area (flex, cuộn dọc)
      Row 3: Bottom navigation (56px)
```

Kích thước cửa sổ:
```xml
Width="380" Height="720"
MinWidth="340" MinHeight="600"
MaxWidth="420"
```

### 5.2 TopBarView → HeaderView (thu gọn)

**Hiện tại:** Dòng ngang chứa tiêu đề + Profile ComboBox + Start/Pause/Stop All + Grid toggle + License.

**Mới:** Header compact 48px:
- Bên trái: Logo icon + "SimpliMixi" (text nhỏ)
- Bên phải: Nút License (icon) + Nút Minimize (icon)
- Các nút Start All / Stop All / Pause All → chuyển vào Dashboard content hoặc context menu

### 5.3 SidebarView → BottomNavView (thanh điều hướng dưới)

**Hiện tại:** Sidebar dọc 220px bên trái chứa logo + 4 nav items.

**Mới:** Bottom Navigation Bar 56px nằm ở đáy cửa sổ:
```
┌────────┬────────┬────────┬────────┐
│   🏠   │   🔧   │   ⚙   │   📋   │
│ Trang  │ Thiết  │ Nâng   │ Nhật   │
│ chính  │  lập   │  cao   │  ký    │
└────────┴────────┴────────┴────────┘
```

- Mỗi tab là một icon + label nhỏ 10px.
- Tab active: icon đổi màu Gold (`AppAccentColor`), có thanh indicator 3px phía trên.

### 5.4 DashboardView — Danh sách device dọc

**Hiện tại:** Danh sách device ngang (mỗi device là 1 dòng dài với nhiều cột).

**Mới:** Mỗi device là một Card dọc nhỏ gọn (full-width, height ~120px), xếp chồng
trong ScrollViewer. Mỗi card chứa:
- Dòng 1: Icon + Tên thiết bị + Badge trạng thái (Running/Stopped/Error)
- Dòng 2: Chế độ chơi (ComboBox nhỏ) + Nguồn (LDPlayer/BlueStacks)
- Dòng 3: Chỉ số stats (nếu đang chạy): Stars / Attacks / Gold looted
- Dòng 4: Nút hành động: [▶ Start] [■ Stop] [⚙ Config] [📋 Log]

### 5.5 SettingsView — Accordion thay cho Sidebar Sections

**Hiện tại:** Sidebar trái chứa danh sách Sections + Content bên phải.

**Mới (chỉ dùng ở Instance Mode):** Cuộn dọc đơn thuần, các nhóm cấu hình
(Cấu hình tấn công, Ngưỡng tài nguyên, Nâng tường, Đầu hàng thông minh)
sử dụng **Expander** (Accordion) gập mở. Mỗi Expander:
- Header: Icon + Tên nhóm + Chevron mở/đóng
- Content: Các control cấu hình xếp dọc (1 cột, không 2 cột như hiện tại vì chiều ngang hẹp)

### 5.6 Các View khác

| View | Thay đổi |
|---|---|
| `AdvancedView` | Thu từ 2 cột → 1 cột dọc, cuộn dọc |
| `LogsView` | Giữ nguyên logic, resize chiều ngang cho vừa 340px |
| `SetupWizardView` | Thu gọn form thành 1 cột dọc |
| `DevicePanelView` | Redesign thành card dọc compact (xem mục 5.4) |
| `LicenseView` | Overlay modal giữ nguyên nhưng giảm width xuống ~340px |

---

## 6. Hiệu ứng giao diện (Aesthetics)

### 6.1 Viền phát sáng nhẹ (Glow Border)

Card khi hover sẽ có hiệu ứng viền phát sáng mờ màu Gold:
```xml
<Style Selector="Border.Card:pointerover">
    <Setter Property="BorderBrush" Value="{DynamicResource AppAccentBrush}" />
    <Setter Property="BoxShadow" Value="0 0 8 0 #30d4af37" />
</Style>
```

### 6.2 Status indicator LED

Trạng thái bot hiển thị bằng chấm tròn nhỏ phát sáng (giống đèn LED):
- Running: ● xanh lá (`#22c55e`) + BoxShadow glow `0 0 6 0 #5022c55e`
- Paused: ● vàng + glow
- Error: ● đỏ + glow
- Stopped: ● xám (không glow)

### 6.3 Bottom Nav indicator

Tab đang active có thanh sáng 3px phía trên icon, màu Gold:
```xml
<Border Height="3" Width="24" CornerRadius="2"
        Background="{DynamicResource AppAccentBrush}"
        IsVisible="{Binding IsActive}" />
```

### 6.4 Font chữ log console

Log console sử dụng font monospace với màu xanh cyan (`AppInfoColor: #06b6d4`):
```xml
<Style Selector="TextBlock.log">
    <Setter Property="FontFamily" Value="Cascadia Mono, Consolas, monospace" />
    <Setter Property="FontSize" Value="10" />
    <Setter Property="Foreground" Value="{DynamicResource AppInfoBrush}" />
</Style>
```

---

## 7. Ràng buộc kiến trúc (giữ nguyên)

| Loại state | Ví dụ | Nơi lưu |
|---|---|---|
| Global | Theme, ngôn ngữ, config đã lưu | `AppStateService` |
| Device-scoped | Status, stats phiên, logs, giả lập | `DeviceViewModel` |

- VM không gọi ADB trực tiếp; chỉ qua `IDeviceSession`.
- BE→FE realtime: `IObservable<T>`/event, marshal UI bằng `Dispatcher.UIThread.Post`.
- Thêm View/VM → cập nhật `ViewLocator.cs` tường minh; thêm `.cs` FE → cập nhật
  `<Compile>` trong `Simplimixi.csproj`.

---

## 8. Thứ tự thực thi

### Phase 1: Loại bỏ FluentAvalonia + Đổi kích thước (Foundation)
1. Gỡ `FluentAvaloniaUI` khỏi `.csproj`
2. `MainWindow.axaml`: `FAAppWindow` → `Window`, xóa xmlns
3. `MainWindow.axaml.cs`: `FAAppWindow` → `Window`, xóa using
4. `App.axaml`: `FluentAvaloniaTheme` → `SimpleTheme`, xóa xmlns sty
5. `MainWindow.axaml`: đổi Width/Height/MinWidth/MinHeight sang dọc
6. Build → verify 0 error

### Phase 2: Bảng màu Gaming Console
7. `App.axaml`: thay toàn bộ Color tokens theo bảng màu mới (mục 3)
8. Thêm tokens mới: `AppElixirColor`, `AppDarkElixirColor`, `AppInfoColor` + Brushes
9. Build → verify

### Phase 3: Layout dọc — Shell (Header + Bottom Nav + Content)
10. Tạo `BottomNavView.axaml` + `BottomNavView.axaml.cs` (thay SidebarView)
11. Sửa `MainWindow.axaml`: layout Grid từ ngang sang dọc (Row-based)
12. Thu gọn `TopBarView` → `HeaderView` compact 48px
13. Tạo Status bar view (chỉ số running / stars / errors)
14. Cập nhật `ViewLocator.cs`, `Simplimixi.csproj`
15. Build → verify

### Phase 4: Dashboard dọc — Device cards
16. Redesign `DashboardView.axaml`: device list dọc, card compact
17. Redesign `DevicePanelView.axaml` (nếu cần cho single-device mode)
18. Build → verify

### Phase 5: Cấu hình Instance — Accordion
19. Redesign `SettingsView.axaml` cho chế độ Instance: loại bỏ sidebar sections,
    dùng Expander accordion xếp dọc
20. Redesign `MainVillageView.axaml`: từ 2 cột → 1 cột dọc
21. Tương tự cho `NightVillageView`, `ClanGamesView`, `ClanCapitalView`
22. Build → verify

### Phase 6: Các view phụ
23. Thu gọn `AdvancedView.axaml` → 1 cột dọc
24. Resize `LogsView.axaml` cho chiều ngang 340px
25. Thu gọn `SetupWizardView.axaml` → 1 cột dọc
26. Thu gọn `LicenseView` overlay → width 340px
27. Build → verify

### Phase 7: Hiệu ứng & Polish
28. Thêm glow border hover cho Card
29. Thêm LED status indicator
30. Thêm Bottom Nav active indicator
31. Thêm log console styling (cyan monospace)
32. Kiểm tra toàn bộ binding, empty-state
33. Build cuối → chạy thử `dotnet run`

---

## 9. Lệnh verify

```powershell
dotnet build src/frontend/Simplimixi.csproj       # build hẹp
dotnet run   --project src/frontend/Simplimixi.csproj    # chạy thử UI
rg -o '#[0-9A-Fa-f]{6,8}' -g 'Views/*.axaml' -c   # hex hardcode check
rg 'FluentAvalonia' src/frontend/ -l               # phải rỗng sau Phase 1
```

## 10. Definition of Done

- [ ] `rg 'FluentAvalonia' src/frontend/` → rỗng (đã gỡ hoàn toàn).
- [ ] Cửa sổ mặc định 380×720, layout dọc hoạt động đúng.
- [ ] Bottom Navigation 4 tab hoạt động chuyển trang.
- [ ] Danh sách device hiển thị dạng card dọc, có thể cuộn.
- [ ] Cấu hình Instance mở dạng accordion, lưu/hủy hoạt động.
- [ ] Bảng màu Gaming Console (Gold accent, carbon background) hiển thị đúng.
- [ ] Build 0 warning, 0 error; không phá compiled binding / AOT.
- [ ] Chạy `dotnet run` trên Windows: mọi trang render đúng trong khung dọc.
