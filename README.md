<div align="center">

# 💬 Zalo Multi-Accounts Desktop Manager

**Giải pháp quản lý và vận hành đồng thời nhiều tài khoản Zalo trên máy tính**  
*Xây dựng trên nền tảng C# .NET 8 WPF và Microsoft WebView2 với cơ chế cô lập dữ liệu (Profile Isolation) độc lập.*

[![.NET](https://img.shields.io/badge/.NET-8.0-512BD4?style=for-the-badge&logo=dotnet&logoColor=white)](https://dotnet.microsoft.com/en-us/download/dotnet/8.0)
[![Platform](https://img.shields.io/badge/Platform-Windows%2010%2F11-0078D6?style=for-the-badge&logo=windows&logoColor=white)](https://www.microsoft.com/windows)
[![WebView2](https://img.shields.io/badge/Engine-Microsoft%20WebView2-0078D7?style=for-the-badge&logo=microsoft-edge&logoColor=white)](https://developer.microsoft.com/en-us/microsoft-edge/webview2/)
[![UI](https://img.shields.io/badge/Theme-VS%20Code%20Dark-1E1E1E?style=for-the-badge)](https://github.com/dn2dn2/zalo-multi-accounts)
[![License](https://img.shields.io/badge/License-MIT-green.svg?style=for-the-badge)](LICENSE)

</div>

---

## 📖 Giới thiệu

**Zalo Multi-Accounts** là ứng dụng Desktop gọn nhẹ dành cho Windows, giúp giải quyết bài toán đăng nhập và sử dụng cùng lúc nhiều tài khoản Zalo (cá nhân, kinh doanh, CSKH, dự án) trên một giao diện duy nhất mà không cần dùng đến máy ảo, trình duyệt ẩn danh hay phần mềm giả lập tốn tài nguyên.

Ứng dụng sử dụng cơ chế **Web-Wrapper kết hợp cô lập môi trường (Environment & Profile Isolation)** thông qua nhân Chromium của Microsoft Edge WebView2, đảm bảo mỗi tài khoản hoạt động trong một vùng dữ liệu (`UserDataFolder`) hoàn toàn tách biệt.

---

## ✨ Tính năng nổi bật

- 🔐 **Cô lập phiên đăng nhập tuyệt đối (Profile Isolation):** Mỗi tài khoản sở hữu Cookie, LocalStorage, IndexedDB và Cache riêng biệt. Đăng xuất hoặc xóa một tài khoản không làm ảnh hưởng đến các tài khoản còn lại.
- ⚡ **Chuyển đổi tức thì (Zero-Latency Switching):** Giữ trạng thái hoạt động nền cho các tài khoản, chuyển đổi qua lại giữa các khung chat chỉ với 1 cú click chuột mà không cần tải lại trang.
- 🌙 **Giao diện Dark Mode hiện đại (3-Column Layout):** Thiết kế lấy cảm hứng từ hệ màu tối chuẩn VS Code (`#1E1E1E` / `#252526`), giúp giảm mỏi mắt khi làm việc liên tục trong thời gian dài:
  - **Cột 1 (Sidebar):** Danh sách tài khoản trực quan kèm trạng thái kết nối, chấm báo tin nhắn mới (Unread Badge) và nút thêm nhanh tài khoản.
  - **Cột 2 (Main Workspace):** Khung hiển thị Zalo Web (`https://chat.zalo.me`) toàn màn hình với đầy đủ tính năng gửi file, ảnh, sticker, gọi thoại.
  - **Cột 3 (Quick Tools / Notes - Tùy chọn):** Khu vực ghi chú nhanh theo từng khách hàng/tài khoản hoặc quản lý mẫu tin nhắn nhanh (Quick Replies).
- 🔔 **Đồng bộ thông báo & Khay hệ thống (System Tray):** Hỗ trợ thu nhỏ xuống System Tray, tự động bắt sự kiện thay đổi tiêu đề trang (`DocumentTitleChanged`) để đếm số lượng tin nhắn chưa đọc và hiển thị cảnh báo.
- 🧹 **Quản lý tài nguyên thông minh:** Cho phép "Đóng băng" (Hibernate/Suspend) các tài khoản ít sử dụng để giải phóng RAM và khởi tạo lại ngay khi cần.

---

## 🛠 Công nghệ sử dụng

| Thành phần | Công nghệ / Thư viện | Mục đích |
| :--- | :--- | :--- |
| **Core Framework** | `.NET 8.0` (C# 12) | Nền tảng thực thi hiệu năng cao trên Windows |
| **UI Framework** | `WPF (Windows Presentation Foundation)` | Thiết kế giao diện XAML tùy biến sâu, hỗ trợ Dark Theme |
| **Browser Engine** | `Microsoft.Web.WebView2` | Nhúng nhân Chromium và quản lý `CoreWebView2Environment` độc lập |
| **Kiến trúc** | `MVVM (Model-View-ViewModel)` | Tách biệt rõ ràng giữa giao diện (XAML) và logic nghiệp vụ |
| **Lưu trữ cấu hình** | `System.Text.Json` / `SQLite` | Lưu trữ danh sách Profile, thứ tự sắp xếp và cài đặt người dùng |

---

## 📂 Cấu trúc thư mục dự án

```text
zalo-multi-accounts/
├── 📁 ZaloMultiAccounts/
│   ├── 📁 Models/                  # Định nghĩa cấu trúc dữ liệu (AccountProfile, AppConfig...)
│   ├── 📁 ViewModels/              # Logic điều khiển giao diện theo mô hình MVVM
│   ├── 📁 Views/                   # Các cửa sổ và UserControl XAML (MainWindow, AccountItem...)
│   ├── 📁 Services/                # Xử lý khởi tạo WebView2 Environment, lưu/đọc dữ liệu
│   ├── 📁 Themes/                  # ResourceDictionary định nghĩa bảng màu Dark Mode & Styles
│   ├── 📁 Assets/                  # Icons, hình ảnh, tài nguyên tĩnh
│   ├── 📄 App.xaml                 # Điểm khởi chạy ứng dụng & cấu hình Global Styles
│   └── 📄 MainWindow.xaml          # Bố cục giao diện chính của phần mềm
├── 📁 ProfilesData/                # (Tự động tạo khi chạy) Chứa thư mục cache riêng của từng tài khoản
├── 📄 ZaloMultiAccounts.sln        # File Solution của Visual Studio
└── 📄 README.md                    # Tài liệu hướng dẫn dự án
```

---

## 🚀 Hướng dẫn cài đặt & Khởi chạy

### 1. Yêu cầu hệ thống
- **Hệ điều hành:** Windows 10 (버전 1809 trở lên) hoặc Windows 11 (64-bit).
- **Runtime:**
  - [.NET 8.0 Desktop Runtime](https://dotnet.microsoft.com/en-us/download/dotnet/8.0)
  - [Microsoft Edge WebView2 Runtime](https://developer.microsoft.com/en-us/microsoft-edge/webview2/) *(Mặc định đã được cài sẵn trên Windows 10/11 bản mới)*.
- **Môi trường phát triển (Dành cho Developer):** Visual Studio 2022 (có cài đặt workload *.NET desktop development*).

### 2. Dành cho người dùng cuối (End-Users)
1. Truy cập mục [**Releases**](https://github.com/dn2dn2/zalo-multi-accounts/releases) của repository.
2. Tải về bản đóng gói mới nhất (`.zip` hoặc bộ cài đặt `Setup.exe`).
3. Giải nén và chạy file `ZaloMultiAccounts.exe` để bắt đầu sử dụng.

### 3. Dành cho nhà phát triển (Build từ mã nguồn)
Mở Terminal / PowerShell và thực hiện các lệnh sau:

```bash
# 1. Clone repository về máy
git clone [https://github.com/dn2dn2/zalo-multi-accounts.git](https://github.com/dn2dn2/zalo-multi-accounts.git)

# 2. Di chuyển vào thư mục dự án
cd zalo-multi-accounts

# 3. Khôi phục các gói thư viện NuGet (WebView2, CommunityToolkit.Mvvm...)
dotnet restore

# 4. Build và chạy thử nghiệm dự án
dotnet run --project ZaloMultiAccounts
```

*Để xuất bản thành file thực thi độc lập (Single-file Publish):*
```bash
dotnet publish -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true -o ./publish
```

---

## 💡 Cơ chế hoạt động của Profile Isolation

Trái tim của ứng dụng nằm ở việc khởi tạo mỗi `WebView2` với một đường dẫn `UserDataFolder` riêng biệt trước khi gọi `EnsureCoreWebView2Async`:

```csharp
// Tạo đường dẫn lưu trữ độc lập cho từng Account ID
string profilePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ProfilesData", account.Id.ToString());
Directory.CreateDirectory(profilePath);

// Khởi tạo môi trường WebView2 tách biệt
var env = await CoreWebView2Environment.CreateAsync(null, profilePath);
await webViewControl.EnsureCoreWebView2Async(env);

// Điều hướng tới trang Zalo Web
webViewControl.CoreWebView2.Navigate("[https://chat.zalo.me](https://chat.zalo.me)");
```

Nhờ cấu trúc này, mọi dữ liệu phiên làm việc được lưu an toàn tại `./ProfilesData/{Account_GUID}/`, giúp ứng dụng tự động đăng nhập lại ngay khi mở máy mà không cần quét mã QR nhiều lần.

---

## 🗺 Lộ trình phát triển (Roadmap)

- [x] Khởi tạo kiến trúc đa phiên (Multi-Instance WebView2) với Profile độc lập.
- [x] Thiết kế giao diện Dark Mode trực quan, chuyển đổi mượt mà giữa các tab.
- [x] Lưu trữ danh sách tài khoản và tự động khôi phục phiên khi mở lại app.
- [ ] Tích hợp cấu hình **Proxy riêng (HTTP/SOCKS5)** cho từng tài khoản để tránh giới hạn IP.
- [ ] Thêm tính năng đặt mật khẩu khóa ứng dụng (Master Password) bảo vệ quyền riêng tư.
- [ ] Tích hợp thanh công cụ Gửi tin nhắn mẫu (Quick Templates) và Ghi chú khách hàng (Mini-CRM).
- [ ] Đóng gói bộ cài đặt chuẩn thông qua **Inno Setup**.

---

## 🤝 Đóng góp (Contributing)

Mọi đóng góp nhằm cải thiện hiệu năng và bổ sung tính năng cho dự án đều được hoan nghênh!
1. **Fork** dự án này.
2. Tạo nhánh tính năng mới (`git checkout -b feature/TenTinhNang`).
3. **Commit** các thay đổi của bạn (`git commit -m 'Add: Thêm tính năng XYZ'`).
4. **Push** lên nhánh (`git push origin feature/TenTinhNang`).
5. Tạo một **Pull Request** mới.

---

## 📄 Giấy phép (License)

Dự án được phân phối dưới giấy phép **MIT License**. Xem chi tiết tại file [LICENSE](LICENSE).

---

<div align="center">
  <i>Phát triển bởi <b><a href="https://github.com/dn2dn2">dn2dn2</a></b> • Nếu thấy dự án hữu ích, hãy tặng một ⭐ trên GitHub nhé!</i>
</div>
