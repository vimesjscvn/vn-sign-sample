# VimesSign — Ứng dụng ký số đa nền tảng

Ứng dụng desktop ký số PDF/XML sử dụng **Avalonia UI** (.NET 8), chạy trên cả **macOS** và **Windows**.

## Tính năng

- **Ký PDF**: Ký có vị trí tùy chỉnh, xem trước trang PDF, kéo thả vùng ký
- **Ký XML**: Phân tích cấu trúc Học Bạ/Lý Lịch/Tổng Kết, hỗ trợ NEAC
- **Ký hàng loạt**: Ký nhiều file cùng lúc
- **Đa merchant**: MySign, SmartCA, BCY, CMC, InTrust, SIM, USB, Self

## Yêu cầu hệ thống

- macOS 14+ (Sonoma) hoặc Windows 10/11 x64
- .NET 8 SDK (chỉ cần cho build từ mã nguồn)
- USB Token + driver PKCS#11 (nếu dùng merchant USB)

## Chạy nhanh

```bash
# Copy cấu hình
cp appsettings.example.json appsettings.json
# Chỉnh sửa appsettings.json với thông tin merchant

# Chạy
dotnet run
```

## Cấu hình (`appsettings.json`)

File cấu hình chứa thông tin kết nối các nhà cung cấp. Khi chạy lần đầu trên macOS, file được tự động copy sang `~/.config/vimes-sign/appsettings.json`.

| Section | Mô tả |
|---------|-------|
| `MySignSetting` | ClientId, ClientSecret cho Viettel MySign |
| `SmartCASetting` | ClientId, ClientSecret cho VNPT SmartCA |
| `UsbSetting` | IP/Port kết nối UsbTokenAgent (`127.0.0.1:9999`) |
| `TerminalSetting` | Thông tin MPKI (BCY/CA Gov) |
| `MsspSetting` | Cấu hình SIM CA (MSSP) |

## Ký XML — Phân tích tài liệu

Khi nhấn **Phân tích**, ứng dụng tự nhận diện loại tài liệu:

| Loại | Dấu hiệu | SignTag mặc định | ReferenceId |
|------|-----------|-----------------|-------------|
| **HOC_BA** | Có `DANH_SACH_THONG_TIN_KY` | CBQL | data |
| **TONG_KET** | Có `TONG_KET_CA_NAM` | (trống) | Id đầu tiên |
| **LY_LICH** | Có `THONG_TIN[@Id]` | (trống) | lyLich |

Với HOC_BA, XPath dropdown hiển thị các vai trò ký:
- **CBQL** (cán bộ quản lý) → RefID=data
- **KY_PHAT_HANH** (phát hành) → RefID=data  
- **GVCN** (giáo viên chủ nhiệm) → RefID=thongtinhocba
- **GVBM** (giáo viên bộ môn) → RefID=Id của DIEM_TONG_KET tương ứng

## Build cho phát hành

```bash
# macOS (arm64)
dotnet publish VimesSignSample.csproj -c Release -r osx-arm64 --self-contained true -p:PublishSingleFile=false

# Windows (x64)
dotnet publish VimesSignSample.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true
```

## Cấu trúc thư mục

```
sign-app/
├── VimesSignSample.csproj     # Project file (.NET 8, Avalonia)
├── Program.cs                 # Entry point, DI, cấu hình
├── MainWindow.axaml           # Giao diện XAML
├── MainWindow.axaml.cs        # Logic xử lý (ký PDF, XML, batch)
├── App.axaml(.cs)             # Avalonia application
├── Resources/                 # Icon ứng dụng (.icns, .png)
├── entitlements.plist         # macOS hardened runtime (JIT, network)
├── installer/windows/         # InnoSetup script cho Windows
├── build/                     # PowerShell script download agent
├── appsettings.json           # Cấu hình (gitignored - có secrets)
├── appsettings.example.json   # Template cấu hình
└── nuget.config               # NuGet source
```
