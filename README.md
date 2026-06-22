# Vimes SignSDK — WinForms Sample

Ứng dụng Windows Forms minh họa toàn bộ tính năng của **Vimes SignSDK**: ký số PDF, ký số XML, đặt vị trí chữ ký trực quan, ký hàng loạt và tích hợp với tất cả các nhà cung cấp chữ ký số được hỗ trợ.

---

## Tải Xuống & Chạy Ngay

> Không cần cài đặt, không cần build — tải về, cấu hình và chạy.

1. Vào trang **[Releases](https://github.com/vimesjscvn/vn-sign-sample/releases/latest)** và tải file `VimesSignSDK-WinForms-vX.X.X.zip`.
2. Giải nén vào thư mục bất kỳ.
3. Mở `appsettings.json`, điền thông tin tài khoản nhà cung cấp chữ ký số (thay thế các giá trị `[YOUR_...]`).
4. Chạy `WinFormsSample.exe`.

> Ứng dụng đã tích hợp sẵn .NET runtime — **không cần cài thêm phần mềm nào khác**.

---

## Nhà Cung Cấp Được Hỗ Trợ

| Merchant | Loại | Mô tả |
|----------|------|-------|
| **MySign**  | Remote CA | Ký số đám mây Viettel MySign |
| **SmartCA** | Remote CA | Ký số đám mây VNPT SmartCA |
| **BCY**     | Remote CA | Ký số đám mây BKAV (HSM) |
| **CMC**     | Remote CA | Ký số đám mây CMC CA |
| **InTrust** | Remote CA | Ký số đám mây InTrust CA |
| **SIM**     | Remote CA | Ký số qua SIM/MSSP (OTP SMS) |
| **USB**     | Local     | Ký số bằng USB Token / Smart Card |
| **Self**    | Local     | Ký số bằng file chứng thư cục bộ (.p12 / .pfx) |

---

## Yêu Cầu Hệ Thống

- **Windows 10/11** (WinForms yêu cầu Windows)
- **.NET 8.0 SDK** trở lên — [tải tại đây](https://dotnet.microsoft.com/download)
- Visual Studio 2022 **hoặc** VS Code với extension C#
- Thông tin tài khoản từ nhà cung cấp chữ ký số

---

## Bắt Đầu Nhanh

```bash
git clone https://github.com/vimesjscvn/vn-sign-sample.git
cd vn-sign-sample/WinFormsSample
copy appsettings.example.json appsettings.json
# Điền thông tin vào appsettings.json
dotnet run
```

---

## Cấu Hình

File `appsettings.json` bị loại khỏi git. Dùng `appsettings.example.json` làm mẫu.

```json
{
  "AppSettings": {
    "InternalSetting": {
      "HospitalName": "Tên đơn vị",
      "CompanyName": "Tên công ty",
      "DefaultMerchantId": "MYSIGN"
    },
    "MySignSetting": {
      "BaseUrl": "https://remotesigning.viettel.vn",
      "ProfileId": "[YOUR_MYSIGN_PROFILE_ID]",
      "ClientId": "[YOUR_MYSIGN_CLIENT_ID]",
      "ClientSecret": "[YOUR_MYSIGN_CLIENT_SECRET]"
    },
    "UsbSetting": {
      "UsbAgentIp":   "127.0.0.1",
      "UsbAgentPort": 9999,
      "UsbTokenPin":  "[YOUR_USB_PIN]"
    }
  }
}
```

---

## USB Token Agent

Khi chọn merchant **USB**, ứng dụng tự động khởi động `UsbTokenAgent.exe` ở nền.
File này được **tải tự động** từ [usb-token-agent-dist](https://github.com/tamnguyendev/usb-token-agent-dist) trong quá trình build — không cần cài đặt thủ công.

**Luồng ký USB:**
```
WinFormsSample
  → khởi động UsbTokenAgent.exe (ẩn nền)
  → POST /login  → tìm chứng thư trong Windows store
  → POST /certs  → lấy danh sách chứng thư
  → hash PDF cục bộ (iText)
  → POST /signHash { hashBase64, serial, pin }
  → Agent ký bằng PKCS#11 (bit4id) hoặc Windows CNG
  → nhúng chữ ký vào PDF
```

Xem [usb-token-agent-dist](https://github.com/tamnguyendev/usb-token-agent-dist) để biết thêm chi tiết cài đặt và cấu hình.

---

## Tính Năng

### Ký Số PDF
- Đặt vị trí chữ ký bằng cách kéo vẽ trực tiếp trên PDF
- Hỗ trợ ảnh chữ ký tùy chỉnh (PNG/JPG)
- Ký hiển thị hoặc ẩn, nhiều trang

### Ký Số XML
- Ký XML với chữ ký ECDSA/RSA
- Hỗ trợ XML BHXH, GD, LyLich và các định dạng tùy chỉnh

### Ký Hàng Loạt
- Chọn thư mục → ký toàn bộ PDF với một thao tác

---

## Chế Độ Build

| Chế độ | Lệnh | Sử dụng |
|--------|------|---------|
| **NuGet** (mặc định) | `dotnet build` | Package phát hành từ nuget.org |
| **Source SDK** | `dotnet build -p:UseSdkSource=true` | Mã nguồn SDK cục bộ tại `..\..\sign-sdk-nuget\src` |

### NuGet Packages (v1.0.20)

| Package | Mô tả |
|---------|-------|
| `Vimes.SignSDK` | SDK client chính |
| `Vimes.SignSDK.ViewModels` | View model dùng chung |
| `Vimes.SignSDK.Merchants.MySign` | Viettel MySign |
| `Vimes.SignSDK.Merchants.SmartCA` | VNPT SmartCA |
| `Vimes.SignSDK.Merchants.BCY` | Ban Co Yeu |
| `Vimes.SignSDK.Merchants.CMC` | CMC CA |
| `Vimes.SignSDK.Merchants.InTrust` | InTrust CA |
| `Vimes.SignSDK.Merchants.SIM` | SIM/MSSP |
| `Vimes.SignSDK.Merchants.USB` | USB Token |
| `Vimes.SignSDK.Merchants.Self` | Ky so dien tu |

---

## Bao Mat

- `appsettings.json` da co trong `.gitignore` — tuyet doi khong commit file nay.
- Cac file chung thu (`*.p12`, `*.pfx`, `*.cer`) trong `wwwroot/certs/` cung bi gitignore.
- Chi commit `appsettings.example.json` voi cac gia tri `[YOUR_...]`.
