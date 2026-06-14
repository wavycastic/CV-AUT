# Kế Hoạch Bảo Mật Frontend Bằng Embedded Resources & Obfuscation
**Dự án:** `CV-AUT` / `SimpliMixi`

Tài liệu này hướng dẫn cách bảo vệ giao diện người dùng (Frontend HTML/JS/CSS) chạy trên WebView2 bằng cách làm rối mã nguồn và nhúng toàn bộ tài nguyên vào trong file chạy nhị phân C# (Embedded Resources).

---

## 1. Hiện Trạng & Rủi Ro

Hiện tại, các tệp tin của giao diện web (HTML, JS, CSS) trong thư mục `src/Simplimixi/Frontend/Web/` được cấu hình copy trực tiếp ra thư mục cài đặt của người dùng (`web/*`) dưới dạng các file văn bản thô.

### Rủi ro:
* **Rò rỉ mã nguồn giao diện:** Người dùng dễ dàng đọc hiểu toàn bộ cấu trúc mã nguồn Javascript (`app.js`), cách gửi nhận dữ liệu và các link API của hệ thống.
* **Nguy cơ sửa đổi (Tamper):** Kẻ xấu có thể chỉnh sửa mã nguồn giao diện Web để bỏ qua các bước kiểm tra, thay thế quảng cáo hoặc chèn mã độc vào giao diện.

---

## 2. Giải Pháp Kép Đề Xuất

Để bảo mật tối đa, chúng ta áp dụng **02 lớp bảo vệ**:

```
[Mã nguồn HTML/JS gốc] 
         │
         ▼ (Lớp 1: Làm rối & Nén)
[Code JS bị làm mờ / đổi tên biến (app.min.js)]
         │
         ▼ (Lớp 2: Nhúng vào C# assembly)
[Embedded Resources (.exe / AOT Compiled)]
         │
         ▼ (Nạp trực tiếp từ RAM)
[WebView2 hiển thị giao diện]
```

### Lớp 1: Nén và Làm rối mã nguồn (Minification & Obfuscation)
* Sử dụng các công cụ build (như **Vite** hoặc **javascript-obfuscator**) để gộp tất cả file Javascript, đổi tên các hàm/biến thành ký tự ngắn vô nghĩa, mã hóa chuỗi ký tự và nén dung lượng file.

### Lớp 2: Nhúng tài nguyên vào Binary (Embedded Resources)
* Thay vì phân phối thư mục `web/` dưới dạng các file vật lý trên đĩa cứng, chúng ta khai báo nhúng toàn bộ thư mục web đã build thành tài nguyên nằm bên trong tệp nhị phân `.exe` của C#.
* WebView2 sẽ đọc dữ liệu từ bộ nhớ thông qua cơ chế luồng tài nguyên (`Resource Stream`) thay vì đọc file trên ổ cứng.

---

## 3. Các Bước Triển Khai Chi Tiết

### Bước 1: Làm rối và Nén Javascript (Frontend)
1. Thêm gói thư viện làm rối mã nguồn vào dự án frontend:
   ```bash
   npm install --save-dev javascript-obfuscator
   ```
2. Cấu hình script build trong `package.json` để tự động gộp và làm rối file JS đầu ra trước khi C# build.

### Bước 2: Cấu hình nhúng tài nguyên trong `CV-AUT.csproj`
Thay đổi cấu hình copy file cũ thành cấu hình nhúng tài nguyên của MSBuild:

```xml
<!-- Loại bỏ cấu hình copy Content cũ -->
<ItemGroup>
  <Content Remove="src\Simplimixi\Frontend\Web\**\*" />
</ItemGroup>

<!-- Khai báo nhúng thư mục Web (đã nén/obfuscate) làm tài nguyên của Assembly -->
<ItemGroup>
  <EmbeddedResource Include="src\Simplimixi\Frontend\Web\dist\**\*" Link="web\%(RecursiveDir)%(Filename)%(Extension)" />
</ItemGroup>
```

### Bước 3: Viết Code C# nạp giao diện từ Resource Stream
[WinForms frontend đã bị xóa — tham khảo lịch sử git để xem code cũ]

Sử dụng sự kiện `WebResourceRequested` của WebView2:

```csharp
// 1. Cấu hình bộ lọc yêu cầu (chỉ lọc các yêu cầu gửi tới domain ảo)
_webView.CoreWebView2.AddWebResourceRequestedFilter("https://simplimixi.local/*", CoreWebView2WebResourceContext.All);

// 2. Bắt sự kiện yêu cầu tài nguyên
_webView.CoreWebView2.WebResourceRequested += (sender, args) =>
{
    string uri = args.Request.Uri;
    // Chuyển đổi URI thành đường dẫn Resource tương ứng
    string resourcePath = MapUriToResourcePath(uri); 
    
    // Đọc luồng dữ liệu (Stream) từ Assembly
    var assembly = System.Reflection.Assembly.GetExecutingAssembly();
    Stream? resourceStream = assembly.GetManifestResourceStream(resourcePath);
    
    if (resourceStream != null)
    {
        string mimeType = GetMimeType(uri);
        // Trả kết quả trực tiếp từ RAM về cho WebView2
        args.Response = _webView.CoreWebView2.Environment.CreateWebResourceResponse(
            resourceStream, 200, "OK", $"Content-Type: {mimeType}");
    }
    else
    {
        args.Response = _webView.CoreWebView2.Environment.CreateWebResourceResponse(
            null, 404, "Not Found", "");
    }
};
```

### Bước 4: Khởi chạy WebView2 từ URL ảo
Thay vì điều hướng WebView2 tới đường dẫn file vật lý trên đĩa:
```csharp
_webView.Source = new Uri("https://simplimixi.local/index.html");
```

---

## 4. Danh Sách Kiểm Tra Nghiệm Thu (Acceptance Checklist)

- [ ] Khi build/publish dự án Release, thư mục `web/` không còn xuất hiện trong thư mục đầu ra.
- [ ] Giao diện WebView2 hiển thị bình thường khi gọi URL ảo `https://simplimixi.local/index.html`.
- [ ] Mở ứng dụng bằng công cụ debug mạng (Fiddler/Wireshark) không thể quét thấy các file JS tĩnh được ghi ra đĩa.
- [ ] Code Javascript nhúng đã được xác nhận là bị biến dạng (obfuscated) khi đọc bằng cách giải nén thử tài nguyên của Assembly.
