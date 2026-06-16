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
| **MySign** | Remote CA | Ký số đám mây Viettel MySign |
| **SmartCA** | Remote CA | Ký số đám mây VNPT SmartCA |
| **BCY** | Remote CA | Ký số đám mây BKAV (HSM) |
| **CMC** | Remote CA | Ký số đám mây CMC CA |
| **InTrust** | Remote CA | Ký số đám mây InTrust CA |
| **SIM** | Remote CA | Ký số qua SIM/MSSP (OTP SMS) |
| **USB** | Local | Ký số bằng USB Token / Smart Card |
| **Self** | Local | Ký số bằng file chứng thư cục bộ (.p12 / .pfx) |

---

## Yêu Cầu Hệ Thống

- **Windows** (WinForms yêu cầu Windows)
- **.NET 9.0 SDK** trở lên — [tải tại đây](https://dotnet.microsoft.com/download)
- Visual Studio 2022 **hoặc** VS Code với extension C#
- Thông tin tài khoản từ nhà cung cấp chữ ký số (xem phần [Cấu Hình](#cấu-hình) bên dưới)

---

## Bắt Đầu Nhanh

### 1. Tải mã nguồn và cấu hình

```bash
git clone https://github.com/vimesjscvn/vn-sign-sample.git
cd vn-sign-sample/WinFormsSample
```

Sao chép file cấu hình mẫu và điền thông tin thực tế:

```bash
copy appsettings.example.json appsettings.json
```

Mở `appsettings.json` và thay thế tất cả các giá trị `[YOUR_...]` bằng thông tin tài khoản thực (xem [Cấu Hình](#cấu-hình) bên dưới).

### 2. Chạy ứng dụng

```bash
dotnet run
```

Hoặc mở `WinFormsSample.sln` trong Visual Studio 2022 và nhấn **F5**.

---

## Cấu Hình

File `appsettings.json` bị loại khỏi git (chứa thông tin xác thực thực tế). Dùng `appsettings.example.json` làm mẫu.

### Các mục cấu hình chính

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
    "SmartCASetting": {
      "BaseUrl": "https://gwsca.vnpt.vn",
      "ProfileId": "[YOUR_SMARTCA_PROFILE_ID]",
      "ClientId": "[YOUR_SMARTCA_CLIENT_ID]",
      "ClientSecret": "[YOUR_SMARTCA_CLIENT_SECRET]"
    },
    "BCYSetting": {
      "BaseUrl": "[YOUR_BCY_BASE_URL]",
      "ClientId": "[YOUR_BCY_CLIENT_ID]",
      "ClientSecret": "[YOUR_BCY_CLIENT_SECRET]"
    },
    "MsspSetting": {
      "ApId": "[YOUR_AP_ID]",
      "ApPassword": "[YOUR_AP_PASSWORD]",
      "ServiceUrl": "[YOUR_MSSP_SERVICE_URL]"
    }
  }
}
```

> **Không bao giờ commit `appsettings.json`** — file này đã được thêm vào `.gitignore`.

---

## Tính Năng

### Ký Số PDF

1. Chọn nhà cung cấp (merchant) và đăng nhập bằng thông tin tài khoản.
2. Nhấn **Lấy chứng thư** để hiển thị danh sách chứng thư số khả dụng.
3. Mở file PDF cần ký.
4. Đặt vị trí chữ ký bằng cách kéo vẽ hình chữ nhật trực tiếp trên PDF, hoặc nhập tọa độ X/Y/Chiều rộng/Chiều cao thủ công.
5. Chọn số trang và nhấn **Ký Số**.

Các tùy chọn hỗ trợ:
- Chữ ký hiển thị hoặc ẩn
- Ảnh chữ ký tùy chỉnh (PNG/JPG)
- Thông tin người ký: tên, lý do, vị trí địa lý
- Hỗ trợ PDF nhiều trang với xem trước từng trang

### Ký Số XML

1. Chuyển sang tab **XML**.
2. Tải một hoặc nhiều file XML.
3. Cấu hình thẻ chữ ký và namespace.
4. Nhấn **Ký XML** — SDK tính hash nội dung, gửi lên merchant và nhúng chữ ký ECDSA vào file.

### Ký Hàng Loạt PDF

1. Chuyển sang tab **Batch**.
2. Chọn thư mục chứa các file PDF cần ký.
3. Cấu hình vị trí chữ ký và tùy chọn ký dùng chung cho toàn bộ.
4. Nhấn **Ký Tất Cả** — tiến trình hiển thị theo từng file trong bảng.

### Đặt Ghi Chú (Note)

- Chọn **Loại chữ ký** là `Ghi chú`.
- Vẽ hoặc nhập tọa độ để đặt chú thích văn bản lên PDF mà không cần chữ ký mã hóa.

### Ký Nhanh (Quick Sign)

- Cấu hình thông tin đăng nhập và chứng thư một lần.
- Nhấn một nút để ký bất kỳ PDF nào đang mở với cài đặt đã lưu.

---

## Chế Độ Build

Dự án hỗ trợ hai chế độ build được điều khiển bởi tham số `UseSdkSource`:

| Chế độ | Lệnh | Sử dụng |
|--------|------|---------|
| **Mặc định (NuGet)** | `dotnet build` | Các package đã phát hành từ nuget.org |
| **Source SDK** | `dotnet build -p:UseSdkSource=true` | Mã nguồn SDK cục bộ tại `..\..\sign-sdk-nuget\src` |

**Dành cho developer thông thường**: chỉ cần `dotnet build` — không cần clone SDK về máy.

### Danh sách NuGet Package

Tất cả các package SDK được phát hành tại phiên bản `1.0.17`:

| Package | Mô tả |
|---------|-------|
| `Vimes.SignSDK` | SDK client chính |
| `Vimes.SignSDK.ViewModels` | View model dùng chung |
| `Vimes.SignSDK.Merchants.MySign` | Viettel MySign |
| `Vimes.SignSDK.Merchants.SmartCA` | VNPT SmartCA |
| `Vimes.SignSDK.Merchants.BCY` | Ban Cơ Yếu  |
| `Vimes.SignSDK.Merchants.CMC` | CMC CA |
| `Vimes.SignSDK.Merchants.InTrust` | InTrust CA |
| `Vimes.SignSDK.Merchants.SIM` | SIM/MSSP mobile |
| `Vimes.SignSDK.Merchants.USB` | USB Token |
| `Vimes.SignSDK.Merchants.Self` | Ký số điện tử |

---

## Cấu Trúc Dự Án

```
WinFormsSample/
├── appsettings.example.json   # File cấu hình mẫu (commit file này)
├── appsettings.json           # Thông tin xác thực thực tế (bị gitignore)
├── Program.cs                 # Cấu hình DI, đăng ký merchant
├── MainForm.cs                # Giao diện chính và các luồng ký số
├── wwwroot/
│   └── certs/                 # File chứng thư cục bộ (bị gitignore)
└── WinFormsSample.csproj
```

---

## Bảo Mật

- `appsettings.json` đã có trong `.gitignore` — tuyệt đối không commit file này.
- Các file chứng thư (`*.p12`, `*.pfx`, `*.cer`) trong `wwwroot/certs/` cũng bị gitignore.
- Chỉ commit `appsettings.example.json` với các giá trị `[YOUR_...]` làm mẫu cho người dùng mới.
