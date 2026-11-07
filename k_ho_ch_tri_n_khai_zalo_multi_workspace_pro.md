# TÀI LIỆU THIẾT KẾ & KẾ HOẠCH TRIỂN KHAI: ZALO MULTI-WORKSPACE PRO

> **Nền tảng:** Windows Desktop Application (C# .NET 8 WPF + Microsoft WebView2)  
> **Cơ chế lõi:** Web-Wrapper cô lập Profile (`UserDataFolder`) kết hợp Proxy riêng biệt & JS Injection  
> **Mục tiêu:** Quản lý đồng thời 10–30+ tài khoản Zalo trên một cửa sổ duy nhất, tối ưu RAM và tích hợp bộ công cụ tăng tốc CSKH/Sale.

---

## PHẦN 1: GIẢI PHÁP KỸ THUẬT CỐT LÕI

### 1.1. Nguyên lý cô lập phiên đăng nhập (Profile Isolation)
Thông thường, khi mở nhiều tab `https://chat.zalo.me` trên cùng một trình duyệt, các tab sẽ dùng chung vùng nhớ `Cookies`, `LocalStorage` và `IndexedDB`, dẫn đến việc tự động ghi đè phiên đăng nhập của nhau.

Giải pháp sử dụng **Microsoft WebView2** khởi tạo mỗi tài khoản Zalo trong một môi trường (`CoreWebView2Environment`) trỏ tới một thư mục lưu trữ độc lập trên ổ cứng:
* `.../AppData/Local/ZaloMultiWorkspace/Profiles/acc_01/` (Chứa Cookie, IndexedDB lịch sử chat, Cache của Nick 1)
* `.../AppData/Local/ZaloMultiWorkspace/Profiles/acc_02/` (Chứa Cookie, IndexedDB lịch sử chat, Cache của Nick 2)

Nhờ cơ chế này, khi tắt ứng dụng mở lại, toàn bộ tài khoản vẫn giữ nguyên trạng thái đăng nhập mà không cần quét lại mã QR.

### 1.2. Các tính năng tạo lợi thế cạnh tranh
1. **Không gian làm việc tập trung (Workspace UI):** Chuyển đổi nhanh giữa các tài khoản bằng thanh Sidebar dọc hoặc phím tắt (`Ctrl + 1`, `Ctrl + 2`...) với độ trễ $0\text{ ms}$.
2. **Chế độ Ngủ đông (Tab Hibernation):** Tự động giải phóng tiến trình `msedgewebview2.exe` đối với các tài khoản không tương tác sau $X$ phút để tiết kiệm RAM.
3. **Gán Proxy riêng biệt cho từng Profile:** Hỗ trợ `HTTP/SOCKS5` (kèm xác thực `User:Pass`) và chống rò rỉ IP qua WebRTC để tránh bị Zalo đánh dấu bất thường khi treo nhiều tài khoản.
4. **Trợ lý CSKH (Productivity Drawer):** Thư viện tin nhắn mẫu (Quick Replies) tự động điền văn bản/hình ảnh vào ô chat bằng 1 cú click hoặc cú pháp tắt (`/baogia`, `/stk`), kèm ghi chú khách hàng (Mini-CRM).
5. **An toàn & Di chuyển dữ liệu:** Khóa màn hình bằng mã PIN (Master Lock) và đóng gói sao lưu (Backup/Restore) toàn bộ Profile sang máy tính khác.

---

## PHẦN 2: PHÁC THẢO GIAO DIỆN CHI TIẾT (UI/UX & XAML)

### 2.1. Bản vẽ bố cục tổng thể (5 Phân vùng)

```text
+--------------------------------------------------------------------------------------------------+
| [Logo] Zalo Multi-Workspace Pro                        [🔒 Khóa nhanh]  [—] [□] [✕] (Title Bar) |
+--------+--------------------------------------------------------------+--------------------------+
|        | 👤 Kỹ thuật 01 [Báo giá] | 🛡️ Proxy: 103.22.x.x (18ms)       | [🔄] [💤 Ngủ] [📂] [📌] | (Zone 2)
|        +--------------------------------------------------------------+--------------------------+
|  (3)   |                                                              | [Tin mẫu] | [Ghi chú KH] |
| [Ava1] |                                                              +--------------------------+
|  🟢    |                                                              | 🔍 Tìm nhanh (/baogia).. |
|        |                                                              |                          |
| [Ava2] |                     KHU VỰC HIỂN THỊ                         | +----------------------+ |
|  🟢    |                   WEBVIEW2 CONTAINER                         | | 📌 /chao             | |
|        |                  (https://chat.zalo.me)                      | | Chào anh/chị, em bên | |
| [Ava3] |                                                              | | bộ phận kỹ thuật...  | |
|  💤    |       (Mỗi tài khoản là 1 control WebView2 độc lập           | | [Chèn]    [Gửi ngay] | |
|        |        nằm chồng lên nhau, ẩn/hiện bằng Visibility)          | +----------------------+ |
| [Ava4] |                                                              | +----------------------+ |
|  🔴    |                                                              | | 📌 /stk              | |
|        |                                                              | | Thông tin thanh toán | |
|  ---   |                                                              | | [Chèn]    [Gửi ngay] | |
|  [+]   |                                                              | +----------------------+ |
|  [⚙]   |                                                              | (Zone 4: Right Drawer)   |
|(Zone 1)|                           (Zone 3)                           | (Đóng/Mở linh hoạt)      |
+--------+--------------------------------------------------------------+--------------------------+
| 🔔 Chưa đọc: 3 tin   | 🟢 Đang chạy: 2/4   💤 Ngủ đông: 1   | ⚡ RAM WebView2: 410 MB  (Zone 5) |
+--------------------------------------------------------------------------------------------------+
```

* **Zone 1 – Left Account Rail (`68px`):** Hiển thị danh sách Avatar tài khoản, vạch chỉ thị đang chọn (`#0068FF`), số tin nhắn chưa đọc (Badge đỏ góc trên phải) và chấm trạng thái (🟢 Online, 💤 Ngủ đông, 🔴 Lỗi Proxy).
* **Zone 2 – Top Header Bar (`46px`):** Hiển thị tên gợi nhớ, nhãn phân loại (Tag), địa chỉ IP Proxy kèm độ trễ Ping ($\text{ms}$) và các nút thao tác nhanh trên tab hiện tại.
* **Zone 3 – WebView2 Host Container (`*`):** Chứa các control `WebView2` thực tế hoặc màn hình chờ khi tài khoản đang ở chế độ Ngủ đông / Lỗi kết nối.
* **Zone 4 – Right Productivity Drawer (`300px`):** Ngăn kéo có thể thu gọn, chứa danh sách Tin nhắn mẫu (Quick Replies) và Ghi chú khách hàng.
* **Zone 5 – Bottom Status Bar (`28px`):** Thống kê tổng tin chưa đọc, số tài khoản đang chạy/ngủ đông và tổng mức tiêu thụ RAM của các tiến trình `WebView2`.

### 2.2. Bảng màu chuẩn Dark Mode

| Thành phần giao diện | Mã màu (HEX) | Vai trò thị giác |
| :--- | :--- | :--- |
| **Account Rail (Zone 1) & Status Bar** | `#141417` | Tông tối sâu nhất, tạo khung viền vững chắc cho ứng dụng. |
| **Right Drawer (Zone 4) & Header** | `#1C1C21` | Nền phụ phân tách với khu vực trình duyệt ở giữa. |
| **Card / Item Background** | `#26262E` | Nền của các thẻ tin nhắn mẫu, ô tìm kiếm, nút bấm. |
| **Card Hover / Active** | `#32323D` | Hiệu ứng sáng nhẹ khi di chuột qua các phần tử. |
| **Border / Divider Line** | `#2E2E38` | Đường kẻ mảnh (`1px`) chia cắt các phân vùng. |
| **Primary Accent (Zalo Blue)** | `#0068FF` | Điểm nhấn thương hiệu Zalo, vạch Active, nút chính. |
| **Text Primary / Secondary** | `#F4F4F6` / `#9CA3AF` | Chữ tiêu đề trắng dịu và chữ mô tả xám bạc. |

### 2.3. Hai nguyên tắc kỹ thuật bắt buộc khi dựng UI WPF với WebView2
1. **Xử lý lỗi đè lớp hiển thị (WPF Airspace Issue):** `WebView2` là một cửa sổ Win32 (`HWND`) nên luôn nổi đè lên trên mọi control WPF nằm cùng vị trí. Khi mở bất kỳ Popup/Modal Dialog nào (hoặc màn hình khóa mật khẩu), phải tạm đặt `WebViewHostGrid.Visibility = Visibility.Hidden;` (hoặc chụp ảnh màn hình `CapturePreviewAsync` làm nền) và bật lại `Visible` sau khi đóng hộp thoại.
2. **Không dùng `TabControl` mặc định:** Việc chuyển tab trên `TabControl` của WPF sẽ kích hoạt `Unload/Load` cây giao diện làm `WebView2` bị nháy trắng hoặc tải lại trang. Thay vào đó, đặt tất cả các thực thể `WebView2` vào chung một `Grid` (`WebViewHostGrid`) và chuyển đổi bằng cách bật/tắt thuộc tính `Visibility` (`Visible` / `Collapsed`).

### 2.4. Mã khung giao diện chuẩn (`MainWindow.xaml`)

```xml
<Window x:Class="ZaloMultiWorkspace.MainWindow"
        xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        Title="Zalo Multi-Workspace Pro" Height="760" Width="1280"
        MinHeight="600" MinWidth="960"
        WindowStartupLocation="CenterScreen"
        Background="#141417" Foreground="#F4F4F6"
        FontFamily="Segoe UI">

    <Window.Resources>
        <Style x:Key="ToolbarButtonStyle" TargetType="Button">
            <Setter Property="Background" Value="#26262E"/>
            <Setter Property="Foreground" Value="#E4E4E7"/>
            <Setter Property="BorderBrush" Value="#32323D"/>
            <Setter Property="BorderThickness" Value="1"/>
            <Setter Property="Padding" Value="10,5"/>
            <Setter Property="Margin" Value="4,0"/>
            <Setter Property="Cursor" Value="Hand"/>
            <Setter Property="Template">
                <Setter.Value>
                    <ControlTemplate TargetType="Button">
                        <Border x:Name="border" Background="{TemplateBinding Background}" 
                                BorderBrush="{TemplateBinding BorderBrush}" 
                                BorderThickness="{TemplateBinding BorderThickness}" 
                                CornerRadius="5" Padding="{TemplateBinding Padding}">
                            <ContentPresenter HorizontalAlignment="Center" VerticalAlignment="Center"/>
                        </Border>
                        <ControlTemplate.Triggers>
                            <Trigger Property="IsMouseOver" Value="True">
                                <Setter TargetName="border" Property="Background" Value="#32323D"/>
                            </Trigger>
                        </ControlTemplate.Triggers>
                    </ControlTemplate>
                </Setter.Value>
            </Setter>
        </Style>

        <Style x:Key="QuickReplyCardStyle" TargetType="Border">
            <Setter Property="Background" Value="#26262E"/>
            <Setter Property="BorderBrush" Value="#2E2E38"/>
            <Setter Property="BorderThickness" Value="1"/>
            <Setter Property="CornerRadius" Value="6"/>
            <Setter Property="Padding" Value="12"/>
            <Setter Property="Margin" Value="0,0,0,10"/>
        </Style>
    </Window.Resources>

    <Grid>
        <Grid.RowDefinitions>
            <RowDefinition Height="*"/>
            <RowDefinition Height="28"/>
        </Grid.RowDefinitions>

        <Grid Grid.Row="0">
            <Grid.ColumnDefinitions>
                <ColumnDefinition Width="68"/>
                <ColumnDefinition Width="*"/>
                <ColumnDefinition Width="300" x:Name="RightDrawerColumn"/>
            </Grid.ColumnDefinitions>

            <!-- ZONE 1: LEFT ACCOUNT RAIL -->
            <Border Grid.Column="0" Background="#141417" BorderBrush="#2E2E38" BorderThickness="0,0,1,0">
                <DockPanel LastChildFill="True">
                    <StackPanel DockPanel.Dock="Bottom" Margin="0,10,0,14" HorizontalAlignment="Center">
                        <Separator Background="#2E2E38" Margin="10,0,10,12"/>
                        <Button Width="44" Height="44" Margin="0,0,0,10" ToolTip="Thêm tài khoản Zalo mới"
                                Style="{StaticResource ToolbarButtonStyle}" Background="#1F2937" BorderBrush="#0068FF">
                            <TextBlock Text="+" FontSize="22" FontWeight="Bold" Foreground="#0068FF" Margin="0,-2,0,0"/>
                        </Button>
                        <Button Width="44" Height="44" ToolTip="Cài đặt hệ thống &amp; Sao lưu"
                                Style="{StaticResource ToolbarButtonStyle}">
                            <TextBlock Text="⚙" FontSize="18"/>
                        </Button>
                    </StackPanel>

                    <ScrollViewer VerticalScrollBarVisibility="Hidden" Padding="0,12,0,0">
                        <StackPanel HorizontalAlignment="Center">
                            <!-- Account 1: Active -->
                            <Grid Width="68" Height="56" Margin="0,0,0,8" Cursor="Hand" ToolTip="Kỹ thuật 01 - Chuyên Báo Giá">
                                <Border Width="4" Height="36" Background="#0068FF" CornerRadius="0,3,3,0" HorizontalAlignment="Left"/>
                                <Border Width="46" Height="46" CornerRadius="23" Background="#0068FF" BorderBrush="#60A5FA" BorderThickness="2">
                                    <TextBlock Text="KT1" FontWeight="Bold" FontSize="14" HorizontalAlignment="Center" VerticalAlignment="Center"/>
                                </Border>
                                <Border Background="#EF4444" CornerRadius="9" MinWidth="18" Height="18" Padding="4,0"
                                        HorizontalAlignment="Right" VerticalAlignment="Top" Margin="0,2,8,0" BorderBrush="#141417" BorderThickness="2">
                                    <TextBlock Text="3" FontSize="10" FontWeight="Bold" Foreground="White" HorizontalAlignment="Center" VerticalAlignment="Center"/>
                                </Border>
                                <Ellipse Width="12" Height="12" Fill="#22C55E" Stroke="#141417" StrokeThickness="2"
                                         HorizontalAlignment="Right" VerticalAlignment="Bottom" Margin="0,0,10,4"/>
                            </Grid>

                            <!-- Account 2: Running in background -->
                            <Grid Width="68" Height="56" Margin="0,0,0,8" Cursor="Hand" ToolTip="Sale 02 - CSKH">
                                <Border Width="44" Height="44" CornerRadius="22" Background="#26262E" BorderBrush="#3F3F46" BorderThickness="1">
                                    <TextBlock Text="S02" FontWeight="SemiBold" FontSize="13" Foreground="#A1A1AA" HorizontalAlignment="Center" VerticalAlignment="Center"/>
                                </Border>
                                <Ellipse Width="12" Height="12" Fill="#22C55E" Stroke="#141417" StrokeThickness="2"
                                         HorizontalAlignment="Right" VerticalAlignment="Bottom" Margin="0,0,10,4"/>
                            </Grid>

                            <!-- Account 3: Hibernated -->
                            <Grid Width="68" Height="56" Margin="0,0,0,8" Cursor="Hand" ToolTip="Tài khoản Dự phòng (Đang ngủ đông)">
                                <Border Width="44" Height="44" CornerRadius="22" Background="#1C1C21" BorderBrush="#2E2E38" BorderThickness="1" Opacity="0.6">
                                    <TextBlock Text="DP" FontWeight="SemiBold" FontSize="13" Foreground="#71717A" HorizontalAlignment="Center" VerticalAlignment="Center"/>
                                </Border>
                                <Border Background="#F59E0B" CornerRadius="7" Width="15" Height="15"
                                        HorizontalAlignment="Right" VerticalAlignment="Bottom" Margin="0,0,9,3" BorderBrush="#141417" BorderThickness="2">
                                    <TextBlock Text="z" FontSize="9" FontWeight="Bold" Foreground="#141417" HorizontalAlignment="Center" VerticalAlignment="Center" Margin="0,-1,0,0"/>
                                </Border>
                            </Grid>
                        </StackPanel>
                    </ScrollViewer>
                </DockPanel>
            </Border>

            <!-- ZONE 2 & 3: CENTER WORKSPACE -->
            <Grid Grid.Column="1">
                <Grid.RowDefinitions>
                    <RowDefinition Height="46"/>
                    <RowDefinition Height="*"/>
                </Grid.RowDefinitions>

                <!-- ZONE 2: Header -->
                <Border Grid.Row="0" Background="#1C1C21" BorderBrush="#2E2E38" BorderThickness="0,0,0,1" Padding="16,0">
                    <DockPanel>
                        <StackPanel Orientation="Horizontal" VerticalAlignment="Center">
                            <TextBlock Text="Kỹ thuật 01" FontSize="15" FontWeight="Bold" VerticalAlignment="Center" Margin="0,0,10,0"/>
                            <Border Background="#1E3A8A" CornerRadius="4" Padding="6,2" Margin="0,0,12,0" VerticalAlignment="Center">
                                <TextBlock Text="Báo giá &amp; Dự án" FontSize="11" Foreground="#93C5FD"/>
                            </Border>
                            <Border Background="#26262E" BorderBrush="#32323D" BorderThickness="1" CornerRadius="4" Padding="8,3" VerticalAlignment="Center">
                                <StackPanel Orientation="Horizontal">
                                    <Ellipse Width="7" Height="7" Fill="#22C55E" VerticalAlignment="Center" Margin="0,0,6,0"/>
                                    <TextBlock Text="Proxy: 103.45.12.88:8080" FontSize="11" Foreground="#A1A1AA"/>
                                    <TextBlock Text=" (18ms)" FontSize="11" Foreground="#22C55E" FontWeight="SemiBold"/>
                                </StackPanel>
                            </Border>
                        </StackPanel>

                        <StackPanel Orientation="Horizontal" HorizontalAlignment="Right" VerticalAlignment="Center">
                            <Button Content="🔄 Tải lại" Style="{StaticResource ToolbarButtonStyle}" FontSize="12"/>
                            <Button Content="💤 Ngủ đông" Style="{StaticResource ToolbarButtonStyle}" FontSize="12"/>
                            <Button Content="📂 Mở Profile" Style="{StaticResource ToolbarButtonStyle}" FontSize="12"/>
                            <Button Content="📌 Trợ lý CSKH" Style="{StaticResource ToolbarButtonStyle}" FontSize="12" Background="#0068FF" BorderBrush="#0068FF"/>
                        </StackPanel>
                    </DockPanel>
                </Border>

                <!-- ZONE 3: WebView2 Host -->
                <Grid Grid.Row="1" x:Name="WebViewHostGrid" Background="#18181B">
                    <Border Background="#18181B">
                        <StackPanel HorizontalAlignment="Center" VerticalAlignment="Center" MaxWidth="420">
                            <Border Width="64" Height="64" CornerRadius="32" Background="#26262E" HorizontalAlignment="Center" Margin="0,0,0,16">
                                <TextBlock Text="💬" FontSize="28" HorizontalAlignment="Center" VerticalAlignment="Center"/>
                            </Border>
                            <TextBlock Text="Khu vực hiển thị WebView2 (https://chat.zalo.me)" 
                                       FontSize="16" FontWeight="Bold" HorizontalAlignment="Center" Margin="0,0,0,8"/>
                            <TextBlock Text="Mỗi tài khoản Zalo được khởi tạo một đối tượng WebView2 riêng với đường dẫn UserDataFolder độc lập nằm trong vùng chứa này." 
                                       FontSize="13" Foreground="#9CA3AF" TextWrapping="Wrap" TextAlignment="Center" Margin="0,0,0,20"/>
                            <Button Content="⚡ Khởi chạy Phiên Zalo (Demo)" Width="220" Height="38"
                                    Style="{StaticResource ToolbarButtonStyle}" Background="#0068FF" BorderBrush="#0068FF" FontWeight="SemiBold"/>
                        </StackPanel>
                    </Border>
                </Grid>
            </Grid>

            <!-- ZONE 4: RIGHT PRODUCTIVITY DRAWER -->
            <Border Grid.Column="2" Background="#1C1C21" BorderBrush="#2E2E38" BorderThickness="1,0,0,0">
                <DockPanel>
                    <Border DockPanel.Dock="Top" Height="46" BorderBrush="#2E2E38" BorderThickness="0,0,0,1" Padding="14,0">
                        <Grid VerticalAlignment="Center">
                            <TextBlock Text="⚡ Tin nhắn mẫu (Quick Reply)" FontWeight="SemiBold" FontSize="13" VerticalAlignment="Center"/>
                            <Button Content="+ Thêm" HorizontalAlignment="Right" Style="{StaticResource ToolbarButtonStyle}" Padding="8,3" FontSize="11"/>
                        </Grid>
                    </Border>

                    <Border DockPanel.Dock="Top" Padding="12,12,12,8">
                        <Border Background="#26262E" BorderBrush="#32323D" BorderThickness="1" CornerRadius="5" Padding="8,6">
                            <TextBlock Text="🔍 Tìm theo phím tắt (VD: /baogia)..." Foreground="#71717A" FontSize="12"/>
                        </Border>
                    </Border>

                    <ScrollViewer VerticalScrollBarVisibility="Auto" Padding="12,4,12,12">
                        <StackPanel>
                            <Border Style="{StaticResource QuickReplyCardStyle}">
                                <StackPanel>
                                    <DockPanel Margin="0,0,0,6">
                                        <Border Background="#065F46" CornerRadius="3" Padding="5,1" HorizontalAlignment="Left">
                                            <TextBlock Text="/chao" FontSize="11" FontWeight="Bold" Foreground="#6EE7B7"/>
                                        </Border>
                                        <TextBlock Text="Chào khách mới" FontSize="12" FontWeight="SemiBold" Margin="8,0,0,0" VerticalAlignment="Center"/>
                                    </DockPanel>
                                    <TextBlock Text="Dạ chào anh/chị ạ! Anh/chị cần tư vấn thông số kỹ thuật hay báo giá hạng mục nào cứ nhắn em hỗ trợ ngay nhé."
                                               FontSize="12" Foreground="#A1A1AA" TextWrapping="Wrap" Margin="0,0,0,10"/>
                                    <StackPanel Orientation="Horizontal" HorizontalAlignment="Right">
                                        <Button Content="Chèn vào ô chat" Style="{StaticResource ToolbarButtonStyle}" FontSize="11" Padding="8,3"/>
                                        <Button Content="Gửi ngay ➔" Style="{StaticResource ToolbarButtonStyle}" FontSize="11" Padding="8,3" Background="#0068FF" BorderBrush="#0068FF"/>
                                    </StackPanel>
                                </StackPanel>
                            </Border>
                        </StackPanel>
                    </ScrollViewer>
                </DockPanel>
            </Border>
        </Grid>

        <!-- ZONE 5: BOTTOM STATUS BAR -->
        <Border Grid.Row="1" Background="#141417" BorderBrush="#2E2E38" BorderThickness="0,1,0,0" Padding="14,0">
            <DockPanel VerticalAlignment="Center">
                <StackPanel Orientation="Horizontal">
                    <TextBlock Text="🔔 Tổng chưa đọc: 3 tin" FontSize="11" Foreground="#F87171" FontWeight="SemiBold" Margin="0,0,16,0"/>
                    <TextBlock Text="|" Foreground="#3F3F46" Margin="0,0,16,0"/>
                    <TextBlock Text="🟢 Đang chạy: 2/3 tài khoản" FontSize="11" Foreground="#A1A1AA" Margin="0,0,16,0"/>
                    <TextBlock Text="💤 Ngủ đông: 1" FontSize="11" Foreground="#FBBF24"/>
                </StackPanel>
                <StackPanel Orientation="Horizontal" HorizontalAlignment="Right">
                    <TextBlock Text="⚡ RAM tiến trình WebView2: ~340 MB" FontSize="11" Foreground="#9CA3AF" Margin="0,0,16,0"/>
                    <TextBlock Text="🔒 Tự động khóa: Bật" FontSize="11" Foreground="#60A5FA"/>
                </StackPanel>
            </DockPanel>
        </Border>
    </Grid>
</Window>
```

---

## PHẦN 3: KIẾN TRÚC SOLUTION & PACKAGES

### 3.1. Các gói NuGet cần cài đặt
* `Microsoft.Web.WebView2`: Nhân trình duyệt Edge Chromium nhúng.
* `CommunityToolkit.Mvvm`: Triển khai kiến trúc MVVM gọn nhẹ (`ObservableObject`, `RelayCommand`).
* `LiteDB` (hoặc `Microsoft.Data.Sqlite`): Cơ sở dữ liệu nội bộ dạng đơn file (`.db`), lưu cấu hình Profile, Proxy, Tin nhắn mẫu và Ghi chú khách hàng.
* `Hardcodet.NotifyIcon.Wpf`: Quản lý chạy ngầm dưới khay hệ thống (System Tray) và thông báo góc màn hình.

### 3.2. Cấu trúc thư mục dự án

```text
ZaloMultiWorkspace/ (C# .NET 8 WPF Solution)
├── Models/
│   ├── ZaloAccountProfile.cs      # Thông tin tài khoản, đường dẫn Profile, Proxy, trạng thái
│   ├── QuickReplyTemplate.cs      # Mẫu tin nhắn nhanh (Phím tắt, nội dung, đường dẫn ảnh)
│   └── CustomerNote.cs            # Ghi chú khách hàng theo cuộc hội thoại
├── Services/
│   ├── DatabaseService.cs         # Đọc/ghi dữ liệu cấu hình từ file LiteDB/SQLite
│   ├── WebViewSessionManager.cs   # Quản lý vòng đời, khởi tạo, ngủ đông các control WebView2
│   ├── ProxyHealthService.cs      # Kiểm tra kết nối và đo Ping Proxy trước khi mở tab
│   └── JsBridgeService.cs         # Chứa các script JS tiêm vào Zalo Web & bắt sự kiện trả về
├── ViewModels/
│   ├── MainViewModel.cs           # Điều phối màn hình chính, chuyển tab, cập nhật Status Bar
│   └── AddAccountViewModel.cs     # Xử lý logic form thêm/sửa tài khoản
└── Views/
    ├── MainWindow.xaml            # Giao diện Workspace 5 phân vùng
    └── Dialogs/
        └── AddAccountWindow.xaml  # Hộp thoại cấu hình Profile & Proxy
```

---

## PHẦN 4: LỘ TRÌNH TRIỂN KHAI CHI TIẾT (5 GIAI ĐOẠN)

| Giai đoạn | Trọng tâm kỹ thuật | Kết quả đầu ra (Deliverables) | Thời gian dự kiến |
| :--- | :--- | :--- | :--- |
| **Giai đoạn 1** | Core Engine & Cô lập Profile | Thêm/xóa tài khoản, lưu phiên độc lập, chuyển đổi tab $0\text{ ms}$ độ trễ. | 2 – 3 ngày |
| **Giai đoạn 2** | Mạng, Proxy & Chống lộ IP | Gán Proxy riêng cho từng tab, tự động xác thực `User:Pass`, kiểm tra Ping. | 2 ngày |
| **Giai đoạn 3** | JS Bridge, Thông báo & Ngủ đông | Bắt số tin nhắn chưa đọc, lấy Avatar thật từ Zalo, giải phóng RAM tự động. | 3 ngày |
| **Giai đoạn 4** | Bộ công cụ CSKH (Productivity) | Bơm tin nhắn mẫu + hình ảnh vào ô chat Zalo, lưu ghi chú khách hàng. | 2 – 3 ngày |
| **Giai đoạn 5** | Bảo mật, Sao lưu & Đóng gói | Chạy ngầm System Tray, khóa mật khẩu, nén Backup Profile, tạo bộ cài. | 2 ngày |

---

### Giai đoạn 1: Xây dựng Core Engine & Cơ chế cô lập Profile
1. **Thiết kế Model `ZaloAccountProfile`:**
   * Thuộc tính lưu DB: `Id` (GUID), `DisplayName`, `TagCategory`, `TagColor`, `ProxyAddress`, `ProxyUser`, `ProxyPass`, `AutoHibernateMinutes`.
   * Thuộc tính Runtime: `UnreadCount`, `IsHibernated`, `ConnectionStatus`, `RamUsageMb`, `AvatarUrl`.
2. **Xây dựng `WebViewSessionManager`:**
   * Quản lý `Dictionary<Guid, WebView2>` chứa các thực thể đang chạy.
   * Khởi tạo thư mục riêng `Path.Combine(BaseProfileDir, $"acc_{profile.Id}")` và truyền vào `CoreWebView2Environment.CreateAsync(null, userDataFolder, options)`.
   * Tắt các tính năng trình duyệt dư thừa: `AreDefaultContextMenusEnabled = false`, `AreDevToolsEnabled = false`, `IsStatusBarEnabled = false`.
3. **Chuyển đổi Tab:** Duyệt qua `WebViewHostGrid.Children`, bật `Visibility.Visible` cho `WebView2` đang chọn và đặt `Visibility.Collapsed` cho các control còn lại.

---

### Giai đoạn 2: Tích hợp Proxy riêng & Cơ chế chống rò rỉ IP
1. **Cấu hình tham số dòng lệnh Chromium:**
   ```csharp
   var options = new CoreWebView2EnvironmentOptions
   {
       AdditionalBrowserArguments = $"--proxy-server={profile.ProxyType}://{profile.ProxyHost}:{profile.ProxyPort} " +
                                    "--disable-features=WebRtcHideLocalIpsWithMdns " +
                                    "--force-webrtc-ip-handling-policy=default_public_interface_only"
   };
   ```
   *(Cờ `--force-webrtc-ip-handling-policy` giúp chặn WebRTC làm lộ IP mạng gốc khi gọi thoại/video trên Zalo Web).*
2. **Tự động xác thực Proxy có mật khẩu (`User:Pass`):**
   Đăng ký sự kiện `BasicAuthenticationRequested` ngay khi khởi tạo xong `CoreWebView2`:
   ```csharp
   webView.CoreWebView2.BasicAuthenticationRequested += (sender, args) =>
   {
       args.Response.UserName = profile.ProxyUser;
       args.Response.Password = profile.ProxyPass;
   };
   ```
3. **Kiểm tra Proxy trước khi kết nối (`ProxyHealthService`):** Dùng `HttpClient` gắn `WebProxy` kiểm tra kết nối và đo Ping trước khi cho `WebView2` điều hướng tới `https://chat.zalo.me`. Nếu Proxy lỗi, chặn tải trang và hiển thị cảnh báo để bảo vệ tài khoản.

---

### Giai đoạn 3: Cầu nối Javascript (JS Bridge), Thông báo & Tối ưu RAM
1. **Tiêm kịch bản theo dõi tự động (`JsBridgeService`):**
   Sử dụng `AddScriptToExecuteOnDocumentCreatedAsync` tiêm `MutationObserver` theo dõi thẻ `<title>` của Zalo Web:
   ```javascript
   const observer = new MutationObserver(() => {
       const match = document.title.match(/^\((\d+)\)/);
       const unreadCount = match ? parseInt(match[1], 10) : 0;
       if (window.chrome && window.chrome.webview) {
           window.chrome.webview.postMessage({ type: 'UNREAD_UPDATE', count: unreadCount });
       }
   });
   observer.observe(document.querySelector('title'), { childList: true });
   ```
2. **Đo lường RAM thực tế:** Định kỳ mỗi $5\text{ giây}$, lấy `webView.CoreWebView2.BrowserProcessId` và đọc `Process.GetProcessById((int)pid).WorkingSet64` để cập nhật tổng mức chiếm dụng bộ nhớ lên Status Bar.
3. **Cơ chế Ngủ đông (Tab Hibernation):** Khi một tài khoản không tương tác quá $X$ phút (và không có tin nhắn chưa đọc), hoặc khi người dùng bấm nút **Ngủ đông**:
   * Gỡ control `WebView2` khỏi `WebViewHostGrid.Children` và gọi `webView.Dispose()` để giải phóng toàn bộ tiến trình con.
   * Hiển thị màn hình chờ kèm nút **`[⚡ Đánh thức tài khoản]`**.

---

### Giai đoạn 4: Bộ công cụ tăng tốc CSKH (Quick Reply & Mini-CRM)
1. **Bơm văn bản vào ô nhập liệu Zalo Web:**
   Ô chat của Zalo Web là thẻ `<div id="richInput" contenteditable="true">`. Để bộ khung React của Zalo nhận diện được văn bản chèn vào, sử dụng `document.execCommand('insertText', false, text)` hoặc phát sự kiện `new InputEvent('input', { bubbles: true })`.
2. **Tự động đính kèm hình ảnh:**
   Từ C#, nạp ảnh báo giá/kỹ thuật vào `Clipboard.SetImage()`, focus vào `WebView2` hiện tại và kích hoạt lệnh dán (`Paste`) để Zalo Web tự động đưa ảnh vào khung xem trước (Preview).

---

### Giai đoạn 5: Bảo mật, Sao lưu Profile & Đóng gói bộ cài đặt
1. **System Tray & Master Lock:** Thu nhỏ xuống khay hệ thống khi đóng cửa sổ (`e.Cancel = true`). Dùng Win32 API `GetLastInputInfo` để tự động khóa màn hình ứng dụng bằng mã PIN khi máy tính không hoạt động sau $X$ phút.
2. **Sao lưu & Khôi phục (Backup/Restore):** Trước khi nén thư mục `Profiles` thành file `.zip`, tự động chuyển toàn bộ tài khoản về trạng thái **Ngủ đông (`Dispose`)** để mở khóa các file Cookie/IndexedDB đang bị hệ điều hành giữ.
3. **Đóng gói Inno Setup:** Kiểm tra Registry xem máy khách đã cài **Microsoft Edge WebView2 Runtime** hay chưa; nếu chưa, tự động chạy trình cài đặt `MicrosoftEdgeWebView2RuntimeInstallerX64.exe /silent /install`.

---

## PHẦN 5: QUẢN TRỊ RỦI RO KỸ THUẬT

* **Thay đổi cấu trúc DOM của Zalo Web:** Tách toàn bộ các chuỗi CSS Selector (như `#richInput`, class ảnh đại diện...) ra file cấu hình `selectors.json` nằm ngoài mã nguồn biên dịch. Khi Zalo cập nhật giao diện web, chỉ cần cập nhật file JSON mà không cần build lại file `.exe`.
* **Dọn dẹp tiến trình mồ côi (Orphan Processes):** Khi khởi động ứng dụng (`Application_Startup`), kiểm tra và đóng các tiến trình `msedgewebview2.exe` bị treo từ phiên làm việc trước (do mất điện hoặc Crash) đang giữ khóa thư mục `%LocalAppData%\ZaloMultiWorkspace\Profiles\`.