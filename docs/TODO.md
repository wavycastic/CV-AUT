# TODO

Roadmap cập nhật sau khi đối chiếu bot hiện tại với tài liệu tính năng AutoCOC/SimpliMixi.

## Phase 1 — Run tab, emulator selection, and session control
PHASE 1 đã chạy diagnostics toàn project lần cuối: không còn errors/warnings editor.
### 1. Build a real Run tab

- [x] Tạo tab `Run` riêng thay vì chỉ dùng toolbar + tab `Statistics`/`Nhật ký`.
- [x] Hiển thị danh sách giả lập/ADB devices đang chạy.
- [x] Thêm nút refresh để quét lại thiết bị.
- [x] Cho phép chọn device/emulator trước khi Start.
- [x] Lưu device đã chọn vào config.
- [x] Hiển thị trạng thái kết nối ADB rõ ràng trên UI.

### 2. Add play mode selection

- [x] Thêm lựa chọn mode chạy: `Làng Chính`, `Làng Đêm`, `Trò Chơi Hội`.
- [x] Truyền mode đã chọn từ UI xuống backend qua config/facade.
- [x] Chặn Start nếu mode chưa được backend hỗ trợ đầy đủ.
- [x] Hiển thị cảnh báo thân thiện khi chọn mode còn đang phát triển.

### 3. Add auto stop controls

- [x] Thêm tùy chọn dừng sau X trận.
- [x] Thêm tùy chọn dừng sau X phút.
- [x] Backend kiểm tra auto-stop trong vòng lặp chính.
- [x] Log lý do tự dừng bot.
- [x] Đảm bảo Pause/Resume không làm sai bộ đếm thời gian chạy.

### 4. Improve session statistics

- [x] Giữ thống kê hiện có: trận, tài nguyên, sao, tài nguyên/giờ.
- [x] Thêm thống kê số tường đã nâng.
- [x] Thêm thống kê nhiệm vụ Trò Chơi Hội hoàn thành.
- [x] Thêm thống kê điểm Trò Chơi Hội.
- [x] Reset session stats đúng khi Start phiên mới.

## Phase 2 — Main Village farming parity
PHASE 2 đã chạy `dotnet build CV-AUT.csproj -c Release`: build pass, còn warning MSB3277 WindowsBase/WebView2 conflict hiện hữu.
### 1. Add attack mode support

- [x] Thêm UI chọn chế độ: `Tấn công`, `Chỉ Donate`.
- [x] Lưu `attack_mode` vào profile/config.
- [x] Backend phân nhánh luồng theo `attack_mode`.
- [x] `Tấn công`: farm theo ngưỡng tài nguyên.
- [x] `Chỉ Donate`: không vào matchmaking, chỉ chạy donate loop.

### 2. Fix target selection logic

- [x] Thêm `total_resource_threshold` cho Vàng + Dầu Tím.
- [x] Thêm lựa chọn logic lọc base: `OR`, `AND`, `Total` hoặc preset dễ hiểu.
- [x] Sửa logic hiện tại đang yêu cầu đồng thời Vàng/Dầu/Dầu Đen.
- [x] Log rõ lý do accept/skip base.
- [x] Cập nhật UI để người dùng hiểu điều kiện chọn base.

### 3. Complete request troop flow

- [x] Backend có flow request troop cơ bản khi nút request xuất hiện.
- [ ] Sau mỗi trận hoặc theo cooldown, mở Clan Castle/Request UI.
- [x] Gửi request troop bằng message mặc định hoặc cấu hình được.
- [x] Xử lý trường hợp request còn cooldown.
- [x] Log kết quả request thành công/thất bại.

### 4. Add smart surrender

- [x] Thêm cấu hình đầu hàng sau X giây.
- [x] Thêm cấu hình đầu hàng khi tài nguyên còn lại dưới ngưỡng.
- [x] Theo dõi thời gian trận sau khi deploy quân.
- [x] Thực hiện surrender an toàn và về làng.
- [x] Không surrender khi bot đang trong trạng thái cần chờ kết quả bắt buộc.

### 5. Finish event troop support

- [x] Thiết kế thư mục template event, ví dụ `Assets/Templates/event`.
- [x] Load file theo format `ten_linh_soLanTha.png` hoặc format rõ ràng tương đương.
- [x] Parse số lần thả từ tên file.
- [x] Thêm checkbox `Sử dụng lính khác`.
- [x] Thêm nút `Tải lại ảnh sự kiện`.
- [x] Không hard-code danh sách lính event trong `Attacks.cs` nếu có thể.

### 6. Add cake/event item support

- [x] Thêm checkbox `Sử dụng Bánh kem`.
- [x] Xác định template/UI flow để dùng item sự kiện.
- [x] Backend chỉ dùng khi item xuất hiện và config bật.
- [x] Log khi không tìm thấy item.

## Phase 3 — Donate mode

### 1. Implement clan chat scanning

- [ ] Mở Clan Chat từ màn hình home.
- [ ] Quét yêu cầu donate bằng template/OCR phù hợp.
- [ ] Cuộn chat và tránh donate trùng yêu cầu cũ.
- [ ] Xử lý popup/connection lỗi trong lúc donate.

### 2. Implement troop donation

- [ ] Xác định lính donate mặc định.
- [ ] Tap donate đúng troop khi request phù hợp.
- [ ] Bỏ qua request không hỗ trợ.
- [ ] Log request đã donate, không đủ lính, hoặc không nhận diện được.

### 3. Implement donate-only behavior

- [ ] Hoàn thiện `attack_mode = Chỉ Donate` thành donate mode đầy đủ, không chỉ scan/tap cơ bản.
- [ ] Thêm vòng lặp donate với delay/cooldown hợp lý.
- [ ] Thêm tùy chọn auto farm khi Dầu Tím/Dầu Đen thấp nếu cần.
- [ ] Đảm bảo Stop/Pause ngắt được donate loop nhanh.

## Phase 4 — Account management
PHASE 4 đã chạy `dotnet build CV-AUT.csproj -c Release`: build pass, còn warning MSB3277 WindowsBase/WebView2 conflict hiện hữu. Switching hiện dùng template matching và cần kiểm thử thủ công trên giả lập thật.

### 1. Build Account Manager tab

- [x] Tạo tab `Acc` để quản lý tài khoản.
- [x] Hiển thị danh sách account đã lưu.
- [x] Thêm/sửa/xóa account.
- [x] Gắn account với profile/config.
- [x] Chọn làng mục tiêu cho account: Làng Chính, Làng Đêm, Trò Chơi Hội.

### 2. Add account template capture

- [x] Thêm nút `Thêm tài khoản mới`.
- [x] Chụp màn hình hiện tại từ giả lập.
- [x] Cho phép kéo chọn vùng tên tài khoản.
- [x] Lưu template tên account.
- [x] Validate vùng chọn không rỗng và đủ nhỏ/chính xác.

### 3. Implement real account switching

- [x] Mở Settings trong game.
- [x] Mở Switch Account.
- [x] Chọn `Hiển thị tất cả tài khoản` nếu cần.
- [x] Match template account đã lưu.
- [x] Tap đúng account và xác nhận chuyển.
- [x] Thay thế `SwitchToVillagePlaceholder` bằng flow thật.
- [x] Log rõ account hiện tại và account đích.

### 4. Add switch conditions

- [x] Đổi acc sau X trận.
- [x] Đổi acc sau X phút.
- [x] Đổi acc sau X điểm Trò Chơi Hội.
- [x] Cho phép bật/tắt từng điều kiện.
- [x] Ưu tiên điều kiện nào đến trước thì chuyển account.

## Phase 5 — Builder Base / Làng Đêm

### 1. Add Night Village tab

- [x] Tạo tab `Đêm`/`Làng Đêm`.
- [x] Thêm chế độ: `Chỉ farm Dầu`, `Farm Vàng & Dầu`, `Tự động`.
- [x] Thêm cấu hình cúp tối đa/tối thiểu cho mode tự động.
- [x] Lưu config riêng cho Làng Đêm.

### 2. Implement Builder Base navigation

- [ ] Nhận diện đang ở Làng Chính hay Làng Đêm.
- [ ] Chuyển sang Làng Đêm khi cần.
- [ ] Khôi phục nếu đang ở màn hình sai.
- [ ] Đảm bảo tương thích độ phân giải 1600x900 / 300 dpi.

### 3. Implement Builder Base attack loop

- [ ] Mở tấn công Làng Đêm.
- [ ] Đọc tài nguyên/cúp nếu cần.
- [ ] Deploy quân theo chiến thuật Làng Đêm.
- [ ] Chờ trận kết thúc và thu thập kết quả.
- [ ] Cập nhật stats riêng cho Làng Đêm.

### 4. Add Night Village wall upgrades

- [ ] Thêm ngưỡng tài nguyên nâng tường Làng Đêm.
- [ ] Thêm số trận/lần kiểm tra nâng.
- [ ] Thêm lựa chọn tài nguyên dùng để nâng nếu game hỗ trợ.
- [ ] Tách logic khỏi wall upgrade Làng Chính nếu flow UI khác.

## Phase 6 — Clan Games

### 1. Build Clan Games tab

- [x] Tạo tab `Hội`/`Trò Chơi Hội`.
- [x] Thêm lựa chọn nhận nhiệm vụ: `Cả hai`, `Làng Chính`, `Làng Đêm`.
- [x] Hiển thị danh sách nhiệm vụ hỗ trợ.
- [x] Cho phép tick/bỏ tick nhiệm vụ.
- [x] Thêm nút `Lưu nhiệm vụ`.

### 2. Define supported task catalog

- [ ] Tạo model danh mục nhiệm vụ.
- [ ] Nhóm nhiệm vụ theo loại: tài nguyên, tướng, phép, lính, công trình.
- [ ] Gắn mỗi nhiệm vụ với điều kiện nhận diện và strategy xử lý.
- [ ] Bắt đầu với nhóm nhiệm vụ nhỏ, ổn định trước khi mở rộng lên 150+.

### 3. Implement task selection flow

- [ ] Mở bảng Trò Chơi Hội.
- [ ] Quét nhiệm vụ đang có.
- [ ] So khớp với filter người dùng đã lưu.
- [ ] Nhận nhiệm vụ phù hợp.
- [ ] Bỏ qua nhiệm vụ không được chọn.
- [ ] Log nhiệm vụ được nhận hoặc lý do bỏ qua.

### 4. Implement task execution and scoring

- [ ] Chạy đúng mode Làng Chính/Làng Đêm theo nhiệm vụ.
- [ ] Theo dõi tiến độ nhiệm vụ.
- [ ] Xác định hoàn thành nhiệm vụ.
- [ ] Cập nhật số nhiệm vụ hoàn thành.
- [ ] Cập nhật điểm Trò Chơi Hội.
- [ ] Kích hoạt đổi acc khi đạt ngưỡng điểm nếu config bật.

## Phase 7 — Advanced configuration

### 1. Build Config tab

- [x] Tạo tab `Cfg`.
- [x] Thêm checkbox `Dùng cấu hình mặc định`.
- [x] Khi bật default, disable các control nâng cao.
- [x] Khi tắt default, dùng giá trị delay/tọa độ từ config.

### 2. Make attack delays configurable

- [x] Chuyển delay hard-code trong `Attacks.cs` sang config.
- [x] Delay thả quân.
- [x] Delay thả Phép Đóng Băng.
- [x] Delay trước skill Đại Quản Giáo.
- [x] Delay Cuồng Nộ sau.
- [x] Validate min/max delay để tránh nhập giá trị nguy hiểm.

### 3. Make spell coordinates configurable

- [x] Thiết kế schema tọa độ theo hướng tấn công.
- [x] Hỗ trợ 4 hướng: Top-Right, Top-Left, Bottom-Right, Bottom-Left.
- [x] Hỗ trợ nhóm tọa độ: 2 Rage đầu, 3-5 Freeze, Rage còn lại.
- [x] Backend đọc tọa độ từ config thay vì hard-code khi có cấu hình custom.
- [x] Thêm nút reload tọa độ.

### 4. Build coordinate editor tool

- [x] Mở cửa sổ chỉnh tọa độ từ tab `Cfg`.
- [x] Hiển thị ảnh/map tỷ lệ thực.
- [x] Cho phép chọn config, góc nhìn, loại phép.
- [x] Click để thêm điểm tọa độ.
- [x] Hỗ trợ undo và xóa điểm.
- [x] Lưu tọa độ về config.

## Phase 8 — Config preset and localization

### 1. Build Save Config tab

- [x] Tạo tab `Lưu`.
- [x] Hiển thị danh sách cấu hình đã lưu.
- [x] Cho phép chọn cấu hình.
- [x] Cập nhật cấu hình đang chọn bằng thiết lập hiện tại.
- [x] Xóa cấu hình đang chọn.

### 2. Add named config presets

- [x] Thêm input tên cấu hình mới.
- [x] Lưu config mới theo tên.
- [x] Gắn account với preset đã lưu.
- [x] Chặn tên trùng hoặc tên không hợp lệ.
- [x] Đảm bảo config lưu trong `%LocalAppData%\SimpliMixi`.

### 3. Add localization support

- [x] Thêm chọn ngôn ngữ: English / Tiếng Việt.
- [x] Tách text UI khỏi code hard-code.
- [x] Lưu ngôn ngữ đã chọn.
- [x] Apply ngôn ngữ không làm mất config hiện tại.

## Phase 9 — Emulator compatibility and reliability

### 1. Generalize emulator support

- [x] Tách BlueStacks-specific logic khỏi `EmulatorBootstrapper`.
- [x] Thêm cấu hình emulator type: BlueStacks, MEmu, Nox, LDPlayer,MuMu.
- [x] Mỗi emulator có process name, ADB port/default path riêng.
- [x] Cho phép người dùng override host/port.

### 2. Add setup validation

- [x] Kiểm tra ADB enabled.
- [x] Kiểm tra game đã cài.
- [x] Kiểm tra resolution 1600x900.
- [x] Kiểm tra DPI 240 nếu đọc được.
- [x] Hiển thị checklist lỗi/cảnh báo trên UI.

### 3. Improve recovery flows

- [ ] Chuẩn hóa xử lý connection lost/client error/another device.
- [ ] Cho phép bỏ qua restart game khi Start nếu người dùng bật tùy chọn.
- [ ] Log recovery theo format dễ đọc.
- [ ] Không restart game liên tục khi lỗi không thể tự sửa.

## Phase 10 — Documentation and validation

### 1. Update user documentation

- [ ] Viết hướng dẫn dùng từng tab theo UI thật.
- [ ] Ghi rõ tính năng nào đang beta/chưa hỗ trợ.
- [ ] Thêm hướng dẫn cấu hình giả lập 1600x900 / 240 dpi.
- [ ] Thêm hướng dẫn thêm account và template event troop.

### 2. Add validation coverage

- [ ] Build `dotnet build CV-AUT.csproj -c Release` sau mỗi phase lớn.
- [ ] Thêm test nhỏ cho config parsing.
- [ ] Thêm test cho target selection logic.
- [ ] Thêm dry-run/mock mode cho account switching nếu khả thi.
- [ ] Ghi lại checklist test thủ công trên giả lập thật.
