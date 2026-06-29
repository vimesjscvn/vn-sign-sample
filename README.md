# Vimes SignSDK — Desktop Studio & Samples

Kho mã nguồn chứa các ứng dụng mẫu trực quan minh họa toàn bộ tính năng của **Vimes SignSDK**: ký số PDF, ký số XML, định vị vị trí chữ ký trực quan, ký hàng loạt, và tích hợp với tất cả các nhà cung cấp dịch vụ chữ ký số (CA) hỗ trợ tại Việt Nam.

Kho chứa hiện tại bao gồm hai dự án:
1. **[VimesSignSample](file:///Volumes/DATA/vimes/sources/4.Sign/vn-sign-sample/VimesSignSample)** (Mới): Ứng dụng cross-platform phát triển trên **Avalonia UI**, chạy mượt mà trên cả **macOS** (Apple Silicon & Intel) và **Windows**.
2. **[WinFormsSample](file:///Volumes/DATA/vimes/sources/4.Sign/vn-sign-sample/WinFormsSample)**: Phiên bản Windows Forms gốc dành riêng cho hệ điều hành **Windows**.

---

## Tính Năng & Các Nhà Cung Cấp Hỗ Trợ

| Merchant | Loại | Mô tả |
|----------|------|-------|
| **MySign**  | Remote CA | Ký số đám mây Viettel MySign |
| **SmartCA** | Remote CA | Ký số đám mây VNPT SmartCA |
| **BCY**     | Remote CA | Ký số đám mây BKAV (Ban Cơ Yếu) |
| **CMC**     | Remote CA | Ký số đám mây CMC CA |
| **InTrust** | Remote CA | Ký số đám mây InTrust CA |
| **SIM**     | Remote CA | Ký số qua SIM/MSSP (OTP SMS) |
| **USB**     | Local     | Ký số bằng USB Token / Smart Card qua PKCS#11 |
| **Self**    | Local     | Ký số bằng file chứng thư cục bộ (.p12 / .pfx) |

---

## Hướng Dẫn Chạy & Cài Đặt Nhanh

### Yêu Cầu Hệ Thống
- **.NET 8.0 SDK** trở lên — [tải xuống tại đây](https://dotnet.microsoft.com/download)
- Hệ điều hành: **Windows 10/11** hoặc **macOS 14 (Sonoma)** trở lên.

### Cài đặt và Chạy thử:

```bash
git clone https://github.com/vimesjscvn/vn-sign-sample.git
cd vn-sign-sample

# Tạo file cấu hình từ file mẫu
cp VimesSignSample/appsettings.example.json VimesSignSample/appsettings.json
# (Điền thông tin tài khoản CA của bạn vào file appsettings.json)

# Chạy ứng dụng cross-platform Avalonia
dotnet run --project VimesSignSample/VimesSignSample.csproj
```

Nếu bạn ở trên Windows và muốn chạy phiên bản WinForms cũ:
```bash
cp WinFormsSample/appsettings.example.json WinFormsSample/appsettings.json
dotnet run --project WinFormsSample/WinFormsSample.csproj
```

---

## Cấu Hình Cài Đặt (`appsettings.json`)

Các thiết lập quan trọng trong `appsettings.json`:
- **MySignSetting / SmartCASetting**: Điền `ClientId`, `ClientSecret`, và `ProfileId` được cung cấp bởi nhà cung cấp.
- **UsbSetting**: Thiết lập IP và Port kết nối tới USB Token Agent cục bộ (Mặc định `127.0.0.1:9999`).

---

## USB Token Agent cho từng nền tảng

Tính năng ký qua **USB Token** (Local) yêu cầu một phần mềm trung gian chạy ngầm để giao tiếp với phần cứng USB Token thông qua thư viện PKCS#11.

### 1. Trên Windows
Ứng dụng sẽ tự động tải và khởi chạy `UsbTokenAgent.exe` chạy ngầm. Quá trình này hoàn toàn tự động khi bạn build dự án trên hệ điều hành Windows.

### 2. Trên macOS
Trên macOS, phần mềm agent được viết bằng Swift.
- Bạn có thể tải file cài đặt đã ký số **`UsbTokenAgent-mac-arm64-signed.pkg`** từ thư mục `usb-token-agent-mac/dist` trong máy hoặc từ kho lưu trữ.
- Khi cài đặt thành công, chạy ứng dụng **UsbTokenAgent** từ thư mục Applications. Agent sẽ khởi động một Web Server cục bộ tại cổng `9999` để lắng nghe yêu cầu ký từ ứng dụng Studio.
- Thiết lập đường dẫn chạy agent trong tab **Cài Đặt** của Studio nếu bạn muốn kích hoạt khởi động tự động.

---

## Phát Hành Ứng Dụng (Publish)

Để đóng gói ứng dụng độc lập tự chạy (Self-contained) không cần cài .NET runtime trên máy người dùng:

- **Windows x64**:
  ```bash
  dotnet publish VimesSignSample/VimesSignSample.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o ./publish-win
  ```
- **macOS Apple Silicon (M1/M2/M3)**:
  ```bash
  dotnet publish VimesSignSample/VimesSignSample.csproj -c Release -r osx-arm64 --self-contained true -p:PublishSingleFile=true -o ./publish-mac-arm64
  ```
- **macOS Intel**:
  ```bash
  dotnet publish VimesSignSample/VimesSignSample.csproj -c Release -r osx-x64 --self-contained true -p:PublishSingleFile=true -o ./publish-mac-x64
  ```

---

## Bản Quyền & Bảo Mật
- Không bao giờ commit file `appsettings.json` có chứa key nhạy cảm của bạn lên git.
- Mọi thông tin chữ ký số và dữ liệu chứng thư đều được mã hóa hoặc truyền tải trực tiếp thông qua các cổng bảo mật (HTTPS/SSL).
