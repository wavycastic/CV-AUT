# Kế Hoạch Nâng Cấp Bảo Mật Bằng Native AOT & Hardening
**Dự án:** `CV-AUT` / `SimpliMixi`

Tài liệu này vạch ra lộ trình chi tiết để chuyển đổi phần mềm từ kiến trúc biên dịch trung gian (MSIL) kết hợp Obfuscator thông thường sang công nghệ **Native AOT (.NET 9.0)** để đạt mức độ bảo mật mã nguồn tối đa.

---

## 1. Phân Tích Hiện Trạng & Lý Do Nâng Cấp

### Hiện trạng bảo mật:
* Dự án đang sử dụng **Obfuscar** để làm mờ mã nguồn cho tệp `Simplimixi.Backend.dll`.
* Chạy cơ chế Anti-debug định kỳ trong `ReleaseSecurity.cs` để phát hiện phần mềm gỡ lỗi.
* Mã hóa ảnh mẫu (Templates) thành `.dat` và giải mã động trên RAM qua thư viện C++ native `simplimixi_native.dll`.

### Lý do cần nâng cấp lên Native AOT:
1. **Làm mờ mã nguồn bằng Obfuscar rất dễ bị giải mã:** Các công cụ tự động như `de4dot` có thể gỡ bỏ lớp bảo vệ của Obfuscar chỉ trong vài giây. Các cracker vẫn dễ dàng đọc được cấu trúc code C# thông qua `dnSpy`.
2. **Cơ chế chống Debugger có lỗ hổng:** Tồn tại biến môi trường bypass `SIMPLIMIXI_ALLOW_DEBUGGER` cho phép vượt qua toàn bộ cơ chế bảo vệ mà không cần sửa code.
3. **Cơ hội từ thiết kế Hybrid Web:** Giao diện điều khiển chạy trong WebView2 sử dụng cơ chế tin nhắn JSON (`WebMessageReceived`) rất sạch sẽ, hoàn toàn không phụ thuộc vào Reflection động của COM. Đây là điều kiện lý tưởng để áp dụng Native AOT.

---

## 2. Mục Tiêu Bảo Mật Mới

* **Triệt tiêu khả năng dịch ngược về C#:** Khi cracker dùng `dnSpy` hoặc `ILSpy` mở phần mềm, công cụ sẽ báo lỗi *"Not a .NET assembly"*.
* **Tăng rào cản bẻ khóa lên gấp 100 lần:** Buộc cracker phải sử dụng các công cụ dịch ngược mã máy phức tạp như IDA Pro, Ghidra hoặc x64dbg.
* **Cải thiện hiệu năng ứng dụng:** Rút ngắn thời gian khởi động của bot, tối ưu hóa lượng RAM/CPU tiêu thụ và chạy trực tiếp không cần cài đặt .NET Runtime.

---

## 3. Lộ Trình Triển Khai Chi Tiết (Roadmap)

### Bước 1: Nâng cấp Target Framework lên .NET 9.0
Do hỗ trợ Native AOT cho Windows Forms trong .NET 8.0 chỉ ở mức thử nghiệm (experimental), ta cần nâng cấp lên .NET 9.0 để đảm bảo tính ổn định tối đa cho phần UI.

* **Công việc:**
  1. Thay đổi cấu hình trong file [CV-AUT.csproj](file:///E:/Projects/CV-AUT/CV-AUT.csproj):
     ```xml
     <TargetFramework>net9.0-windows</TargetFramework>
     ```
  2. Thay đổi cấu hình trong file [Simplimixi.Backend.csproj](file:///E:/Projects/CV-AUT/src/Simplimixi/Backend/Simplimixi.Backend.csproj):
     ```xml
     <TargetFramework>net9.0-windows</TargetFramework>
     ```
  3. Cài đặt **.NET 9.0 SDK** và C++ Build Tools (qua Visual Studio Installer) trên máy build.

### Bước 2: Tương thích hóa cơ chế JSON (Loại bỏ Reflection)
Hệ thống Native AOT không cho phép phân tích lớp động lúc runtime. Toàn bộ các lệnh gọi `JsonSerializer` bằng Reflection cần chuyển sang sử dụng Source Generator.

* **Công việc:**
  1. Khai báo Class ngữ cảnh biên dịch JSON (`JsonSerializerContext`) cho các Model:
     ```csharp
     using System.Text.Json.Serialization;
     
     namespace CvAut
     {
         [JsonSerializable(typeof(ReleaseSecurity.IntegrityManifest))]
         [JsonSerializable(typeof(object))]
         internal partial class SourceGenerationContext : JsonSerializerContext
         {
         }
     }
     ```
  2. Sửa cơ chế đọc Manifest kiểm tra tính toàn vẹn trong [ReleaseSecurity.cs](file:///E:/Projects/CV-AUT/src/Simplimixi/Backend/Core/ReleaseSecurity.cs):
     ```diff
     - JsonSerializer.Deserialize<IntegrityManifest>(stream, JsonOptions);
     + JsonSerializer.Deserialize(stream, SourceGenerationContext.Default.IntegrityManifest);
     ```
  3. Sửa hàm gửi lệnh JSON-RPC trong [ADBHelper.cs](file:///E:/Projects/CV-AUT/src/Simplimixi/Backend/Core/ADBHelper.cs) để không sử dụng `Dictionary<string, object?>` động:
     * Thay thế bằng cấu trúc `JsonObject` và `JsonArray` của namespace `System.Text.Json.Nodes`.

### Bước 3: Đóng chặt các lỗ hổng Anti-Debug
* **Công việc:**
  1. Khóa cứng biến môi trường bypass trong [ReleaseSecurity.cs](file:///E:/Projects/CV-AUT/src/Simplimixi/Backend/Core/ReleaseSecurity.cs) bằng chỉ thị biên dịch để ngăn chặn việc bỏ qua Anti-debug ở môi trường Production:
     ```csharp
     private static bool DebuggerBypassEnabled()
     {
         #if DEBUG
         return string.Equals(Environment.GetEnvironmentVariable(AllowDebuggerVariable), "1", StringComparison.OrdinalIgnoreCase);
         #else
         return false; // Bản Release khóa cứng, không cho phép bypass
         #endif
     }
     ```

### Bước 4: Xử lý tương thích thư viện bên thứ ba (Dependencies)
* **Công việc:**
  1. Kiểm tra tính tương thích AOT của `OpenCvSharp4`. Nếu có cảnh báo Trim, tạo tệp chỉ thị runtime `rd.xml` để giữ lại các phương thức P/Invoke cần thiết.
  2. Thay thế thư viện cũ `SharpAdbClient` (v2.3.3) bằng phiên bản mới hơn là `AdvancedSharpAdbClient` để tránh các lỗi Reflection nội bộ khi giao tiếp với cổng ADB.

### Bước 5: Cấu hình Build và Biên Dịch Thử Nghiệm
* **Công việc:**
  1. Thêm cấu hình AOT vào file `CV-AUT.csproj` cho cấu hình `Release`:
     ```xml
     <PropertyGroup Condition="'$(Configuration)' == 'Release'">
       <PublishAot>true</PublishAot>
       <StripSymbols>true</StripSymbols>
       <PublishReadyToRun>false</PublishReadyToRun> <!-- Tắt ReadyToRun vì đã có AOT -->
     </PropertyGroup>
     ```
  2. Thực hiện lệnh build đóng gói thử nghiệm bằng Terminal:
     ```bash
     dotnet publish -c Release -r win-x64 --self-contained
     ```
  3. Phân tích các lỗi cảnh báo Trim/AOT (nếu có) phát sinh trong quá trình build để tinh chỉnh lại code.

---

## 4. Danh Sách Kiểm Tra Hoàn Thành (Acceptance Checklist)

Trước khi phát hành bản build Native AOT:
- [ ] Ứng dụng biên dịch thành công không sinh lỗi AOT nghiêm trọng.
- [ ] Kéo thử file `.exe` vừa build vào `dnSpy`, phần mềm phải báo lỗi không đọc được mã nguồn.
- [ ] Khởi chạy ứng dụng ngoài môi trường Visual Studio hoạt động bình thường.
- [ ] Giao diện Web (WebView2) hiển thị đầy đủ, nhận và phản hồi tin nhắn từ Backend bình thường.
- [ ] Kết nối ADB và giả lập hoạt động trơn tru.
- [ ] Kiểm tra cơ chế mã hóa asset `.dat` qua native DLL chạy ổn định.
- [ ] Bật debugger ngoài và kiểm tra xem ứng dụng có đóng lập tức hay không.
