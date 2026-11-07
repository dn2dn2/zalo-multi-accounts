# Nhật ký thay đổi

Mọi thay đổi đáng chú ý của **Chat Workspace Pro** (trước đây là Zalo Multi-Workspace Pro) được ghi tại đây.
Định dạng theo [Keep a Changelog](https://keepachangelog.com/vi/1.1.0/), phiên bản theo [Semantic Versioning](https://semver.org/lang/vi/).

## [1.0.0] - 2026-11-08

Phiên bản phát hành đầu tiên dưới tên **Chat Workspace Pro**.

### Thêm mới

**Nhiều nền tảng chat**
- Hỗ trợ tài khoản **Telegram** (web.telegram.org) bên cạnh Zalo. Chọn nền tảng khi thêm tài khoản.
- Huy hiệu nền tảng trên avatar và nhãn nền tảng trên thanh tiêu đề.
- `selectors.json` tách theo từng nền tảng (`Zalo`, `Telegram`), sửa được khi trang web đổi giao diện mà không cần build lại.

**Dịch tin nhắn**
- Tab **Dịch** ở khung bên phải:
  - Dịch đoạn đã chọn: bôi đen trong khung chat rồi bấm **Ctrl+Shift+D**.
  - Soạn tin tiếng Việt, dịch, sửa rồi **Chèn** hoặc **Gửi**.
- 28 ngôn ngữ, tự nhận diện ngôn ngữ nguồn, nút đảo chiều, ngôn ngữ ghim. Ứng dụng nhớ ngôn ngữ đã dùng cho từng cuộc chat.
- Hai cách dịch: **Google Dịch** (không cần khóa) hoặc **AI (API)**:
  - Nhập API URL, API key và tên mô hình. Có sẵn mẫu cho DeepSeek, Gemini, OpenAI, OpenRouter.
  - URL Gemini dạng `generateContent` tự lấy tên mô hình từ URL. Các URL khác dùng chuẩn tương thích OpenAI.
- **Prompt dịch tùy chỉnh** được, có nút khôi phục mặc định và mẫu thuật ngữ ngành **thiết kế điện**.
- Báo lỗi rõ ràng (sai khóa, hết hạn mức, hết tiền, sai mô hình) và chuyển sang Google Dịch bằng một nút.

**Luôn nhận tin nhắn**
- Tự khởi chạy tài khoản khi mở ứng dụng (bật/tắt riêng từng tài khoản).
- **Mở cùng Windows**: chạy ngầm ở khay khi bật máy.
- Mặc định không tự tạm dừng tài khoản (0 = không bao giờ).
- Tài khoản đang tạm dừng hiển thị rõ, kèm ghi chú "không nhận tin mới".

**Thông báo**
- Chấm đỏ trên icon khay và badge số tin chưa đọc trên nút taskbar.
- Tooltip khay hiện số tin chưa đọc của từng tài khoản.
- Cài đặt thông báo:
  - Bật/tắt popup, ẩn nội dung tin, thanh thông báo trong ứng dụng, âm thanh, badge.
  - **Không làm phiền** theo khung giờ hoặc tạm tắt 1 giờ.
- Tắt thông báo riêng cho từng tài khoản (chuột phải avatar).
- Tùy chọn **Báo khi ẩn xuống khay** (mặc định tắt).

**Giao diện**
- Giao diện **Sáng / Tối / Theo Windows**, kể cả thanh tiêu đề cửa sổ.
- Danh sách tài khoản dạng **tab ngang** (mặc định) hoặc **thanh dọc**.
- Thanh công cụ dạng icon kèm tooltip. Chip **Ping** đo lại mỗi 60 giây.
- Thanh trạng thái thu gọn, có tooltip đầy đủ.
- Hộp thoại Cài đặt chia tab: Chung, Thông báo, Bảo mật, Sao lưu, Dịch, **Thông tin**.
- Tab **Thông tin**: phiên bản, ngày build, giới thiệu ứng dụng, thông tin tác giả (email, điện thoại, website).
- Style dùng chung: nút, ô nhập, danh sách thả xuống, menu chuột phải, công tắc, thanh cuộn.

**Tin mẫu**
- Sửa tin mẫu: nút ✏️ hoặc nhấp đúp vào thẻ.
- Kiểm tra phím tắt trùng, nút **Bỏ ảnh**, hỏi xác nhận khi xóa.
- Ô tìm kiếm gọn, có nút xóa (Esc) và dòng đếm kết quả.

**Ghi chú khách hàng**
- Tự nhận cuộc chat đang mở. Ghi chú của khách đang chat được đưa lên đầu và đánh dấu.

**Đóng gói**
- Đóng gói thành **một file exe** chạy độc lập:
  `dotnet publish ZaloMultiWorkspace/ZaloMultiWorkspace.csproj -p:PublishProfile=SingleFile`
- File chạy đổi tên thành `ChatWorkspacePro.exe`, kèm icon mới.
- Chỉ cho chạy một bản ứng dụng. Mở thêm sẽ đưa cửa sổ đang chạy lên trên.

### Thay đổi
- Đổi tên ứng dụng thành **Chat Workspace Pro**. Dữ liệu cũ (tài khoản, đăng nhập, tin mẫu, cài đặt) vẫn dùng tiếp, không cần làm gì.
- Ngôn ngữ giao diện gọn và dễ hiểu hơn cho người dùng văn phòng ("Ngủ đông" → "Tạm dừng", bỏ thuật ngữ kỹ thuật).
- Màn hình khóa không còn hiện mã PIN mặc định.

### Sửa lỗi
- Badge tin chưa đọc đếm sai hoặc tự mất khi tài khoản đang chạy nền.
- Nhiều tài khoản cùng hiện "đang chọn" do trạng thái lúc chạy bị lưu nhầm vào dữ liệu.
- Thông báo bị lặp 2–3 lần cho cùng một tin.
- Tên khách hàng trong ô ghi chú bị ghi đè liên tục.
- Đo RAM sai (tính cả tiến trình WebView2 của ứng dụng khác).
- Sửa proxy không áp dụng cho phiên đang chạy. Proxy SOCKS5 có mật khẩu (chưa hỗ trợ) nay được báo lỗi rõ ràng.
- Sao lưu lỗi khi file đang bị khóa. Khôi phục nay có cơ chế hoàn tác khi lỗi.
- Badge taskbar bị mờ, logo trong tab Thông tin bị mờ.
- Nội dung Cài đặt bị che khi dài hơn hộp thoại.

### Bảo mật
- Mật khẩu proxy và khóa API được mã hóa bằng Windows (DPAPI).
- Mã PIN được lưu dạng băm (PBKDF2), không còn lưu văn bản thô.
- Dọn tiến trình WebView2 mồ côi một cách an toàn (chỉ khi ứng dụng chủ đã thoát).

## [0.1.0] - 2026-11-07

### Thêm mới
- Bản khởi tạo **Zalo Multi-Workspace Pro** (.NET 8 WPF + WebView2).
- Quản lý nhiều tài khoản Zalo, mỗi tài khoản có dữ liệu đăng nhập và proxy riêng.
- Tin nhắn mẫu, ghi chú khách hàng, khóa ứng dụng bằng mã PIN, sao lưu và khôi phục dữ liệu.

[1.0.0]: https://github.com/dn2dn2/zalo-multi-accounts/compare/86fb9d3...HEAD
[0.1.0]: https://github.com/dn2dn2/zalo-multi-accounts/commit/86fb9d3
