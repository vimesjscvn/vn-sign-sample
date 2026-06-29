# Hướng Dẫn Sử Dụng VimesSign

Tài liệu hướng dẫn cài đặt và sử dụng ứng dụng ký số **VimesSign** dành cho người dùng cuối.

---

## Mục lục

1. [Yêu cầu hệ thống](#1-yêu-cầu-hệ-thống)
2. [Cài đặt](#2-cài-đặt)
3. [Cấu hình lần đầu](#3-cấu-hình-lần-đầu)
4. [Ký PDF bằng USB Token](#4-ký-pdf-bằng-usb-token)
5. [Ký PDF bằng chữ ký số đám mây](#5-ký-pdf-bằng-chữ-ký-số-đám-mây)
6. [Ký XML (Học Bạ, Lý Lịch)](#6-ký-xml-học-bạ-lý-lịch)
7. [Ký hàng loạt](#7-ký-hàng-loạt)
8. [Xử lý sự cố](#8-xử-lý-sự-cố)

---

## 1. Yêu cầu hệ thống

| | macOS | Windows |
|--|-------|---------|
| Phiên bản OS | macOS 14 (Sonoma) trở lên | Windows 10/11 x64 |
| Chip | Apple Silicon (M1/M2/M3/M4) | Intel hoặc AMD x64 |
| USB Token | Có driver PKCS#11 (bit4id, SafeNet, ePass) | Có driver đã cài |
| Dung lượng | ~60 MB | ~40 MB |

---

## 2. Cài đặt

### macOS

1. Tải file `VimesSign-mac-arm64-X.Y.Z.pkg` từ trang [Releases](https://github.com/vimesjscvn/vn-sign-sample/releases)
2. Nhấp đúp vào file `.pkg`
3. Trình cài đặt mở — nhấn **Continue** → **Install**
4. Nhập mật khẩu máy nếu được yêu cầu
5. Sau khi cài xong, hai ứng dụng xuất hiện trong `/Applications/`:
   - **VimesSign** — Ứng dụng ký số chính
   - **UsbTokenAgent** — Agent USB Token (chạy nền)

> **Lưu ý**: File `.pkg` đã được Apple notarize — không có cảnh báo Gatekeeper.

### Windows

1. Tải file `VimesSign-win-x64-X.Y.Z-setup.exe` từ trang Releases
2. Nhấp đúp vào file `.exe`
3. **Nếu xuất hiện cảnh báo "Windows protected your PC"** (SmartScreen):
   - Nhấn **"More info"** (Thông tin thêm)
   - Nhấn **"Run anyway"** (Vẫn chạy)
   - Đây là cảnh báo bình thường cho ứng dụng chưa có chứng chỉ ký số Windows — ứng dụng an toàn
4. Nhấn **Next** → Chọn thư mục cài đặt → **Install**
5. Tùy chọn: Tạo biểu tượng Desktop
6. Sau khi cài xong, mở từ Start Menu → **VimesSign**

### Cài riêng UsbTokenAgent (standalone)

Nếu chỉ cần agent ký USB (không cần giao diện VimesSign), tải bản cài riêng:

| Nền tảng | File | Mô tả |
|----------|------|-------|
| macOS | `UsbTokenAgent-mac-arm64-X.Y.Z.pkg` | Cài vào `/Applications/UsbTokenAgent.app` |
| Windows | `UsbTokenAgent-win-x64-X.Y.Z-setup.exe` | Cài vào `C:\Program Files\UsbTokenAgent\` |
| Windows (portable) | `UsbTokenAgent-vX.Y.Z.zip` | Giải nén và chạy trực tiếp |

Tải từ [Releases](https://github.com/vimesjscvn/vn-sign-sample/releases) hoặc [usb-token-agent-dist](https://github.com/tamnguyendev/usb-token-agent-dist/releases).

---

## 3. Cấu hình lần đầu

Khi mở VimesSign lần đầu tiên, ứng dụng tự động tạo file cấu hình:

- **macOS**: `~/.config/vimes-sign/appsettings.json`
- **Windows**: Trong thư mục cài đặt (`C:\Program Files\VimesSign\appsettings.json`)

### Mở file cấu hình

**macOS:**
```bash
open ~/.config/vimes-sign/appsettings.json
```

**Windows:**
Mở thư mục cài đặt → nhấp đúp `appsettings.json` → chỉnh sửa bằng Notepad.

### Các thông tin cần điền

Tùy thuộc nhà cung cấp chữ ký số bạn sử dụng:

| Nhà cung cấp | Thông tin cần điền |
|---------------|--------------------|
| **USB Token** | Chỉ cần cắm token + nhập PIN khi ký |
| **Viettel MySign** | `ClientId`, `ClientSecret` (do Viettel cung cấp) |
| **VNPT SmartCA** | `ClientId`, `ClientSecret` (do VNPT cung cấp) |
| **BCY** | `RelyingParty`, `RelyingPartyUser`, `RelyingPartyPassword` |
| **SIM CA** | `ApId`, `ApPassword`, certificate `.pfx` |

> **Quan trọng**: Nếu chỉ dùng USB Token, không cần chỉnh sửa file cấu hình.

---

## 4. Ký PDF bằng USB Token

### Bước 1: Cắm USB Token

Cắm USB Token vào cổng USB của máy. Đảm bảo đèn token sáng (driver đã nhận).

### Bước 2: Chọn merchant

Trong dropdown **Nhà Cung Cấp** (góc trên), chọn **USB**.

> Trường "Tên đăng nhập" sẽ tự động bị vô hiệu hóa (không cần nhập).

### Bước 3: Nhập PIN

Nhập **Mã PIN** của USB Token vào ô "Mật khẩu / Mã PIN".

### Bước 4: Đăng nhập

Nhấn nút **Đăng Nhập**. Nếu thành công:
- Trạng thái chuyển sang xanh lá
- Dropdown "Chứng Thư Số" hiển thị danh sách chứng thư trên token

### Bước 5: Chọn file PDF

Nhấn **Duyệt Tệp** → Chọn file PDF cần ký (có thể chọn nhiều file).

### Bước 6: Chọn vị trí ký

- Trên khung xem trước bên phải, **kéo chuột** để vẽ vùng đặt chữ ký
- Hoặc nhập tọa độ X, Y, Width, Height thủ công

### Bước 7: Ký

Nhấn nút **⚡ KÝ PDF**. File đã ký được lưu trong thư mục `Signed/` cạnh file gốc.

---

## 5. Ký PDF bằng chữ ký số đám mây

### Bước 1: Chọn nhà cung cấp

Chọn merchant phù hợp: **VIETTEL** (MySign), **VNPT** (SmartCA), **BCY**, **CMC**, **INTRUST**.

### Bước 2: Đăng nhập

- Nhập **số điện thoại** (hoặc username) và **mật khẩu** đã đăng ký với nhà cung cấp
- Nhấn **Đăng Nhập**
- Xác nhận OTP trên điện thoại (nếu có)

### Bước 3: Chọn chứng thư và ký

Tương tự bước 5-7 ở phần USB Token.

---

## 6. Ký XML (Học Bạ, Lý Lịch)

### Bước 1: Chuyển sang tab Ký XML

Nhấn menu **Chức Năng → Ký XML** hoặc chọn tab tương ứng.

### Bước 2: Đăng nhập

Đăng nhập giống như ký PDF (cùng merchant, cùng chứng thư).

### Bước 3: Chọn file XML

Nhấn **Duyệt** → Chọn file XML (có thể chọn nhiều file).

### Bước 4: Phân tích

Nhấn nút **Phân tích**. Ứng dụng sẽ:
- Tự nhận diện loại tài liệu (Học Bạ, Lý Lịch, Tổng Kết...)
- Tự chọn **Thẻ Ký** (SignTag) và **ReferenceId** phù hợp
- Hiển thị gợi ý XPath cho từng vai trò ký

### Bước 5: Chọn vai trò ký (Học Bạ)

Nếu là file Học Bạ, chọn vai trò trong dropdown **XPath**:
- **CBQL** — Cán bộ quản lý (phát hành học bạ)
- **GVCN** — Giáo viên chủ nhiệm
- **GVBM** — Giáo viên bộ môn (chọn cụ thể từng GV)

> ReferenceId sẽ tự động cập nhật khi chọn XPath.

### Bước 6: Ký

Nhấn **⚡ KÝ XML**. File đã ký được lưu cùng thư mục với tên `*_signed.xml`.

---

## 7. Ký hàng loạt

### Bước 1: Chuyển sang tab Ký Hàng Loạt

### Bước 2: Chọn thư mục

Nhấn **Duyệt** → Chọn thư mục chứa nhiều file PDF.

### Bước 3: Chọn file

Danh sách file hiển thị trong bảng. Đánh dấu ✓ các file muốn ký.

### Bước 4: Ký

Nhấn **📚 BẮT ĐẦU KÝ HÀNG LOẠT**. Thanh tiến trình hiển thị số file đã xử lý.

---

## 8. Xử lý sự cố

### Ứng dụng không mở được (macOS)

**Triệu chứng**: Nhấp đúp VimesSign nhưng không thấy gì.

**Giải pháp**: Mở Terminal và chạy:
```bash
/Applications/VimesSign.app/Contents/MacOS/VimesSignSample
```
Xem thông báo lỗi hiển thị.

### "Connection refused (127.0.0.1:9999)"

**Nguyên nhân**: UsbTokenAgent chưa chạy hoặc chưa sẵn sàng.

**Giải pháp**:
1. Mở `/Applications/UsbTokenAgent.app` (macOS) hoặc chạy `UsbTokenAgent.exe` (Windows)
2. Đợi 2-3 giây rồi thử lại
3. Kiểm tra USB Token đã cắm đúng chưa

### "Không tìm thấy thông tin CTS từ USB Token"

**Nguyên nhân**: Token chưa cắm, hoặc driver chưa cài.

**Giải pháp**:
1. Kiểm tra đèn USB Token sáng
2. macOS: Kiểm tra `libbit4xpki.dylib` trong Resources
3. Windows: Kiểm tra driver bit4id đã cài từ Control Panel

### "Please login first to sync certificates"

**Nguyên nhân**: Chưa nhấn "Đăng Nhập" trước khi nhấn "Tải Chứng Thư Số".

**Giải pháp**: Nhấn **Đăng Nhập** trước, sau đó mới nhấn **Tải Chứng Thư Số**.

### File ký xong nhưng NEAC báo lỗi chữ ký

**Nguyên nhân**: SignTag hoặc ReferenceId không đúng cho loại tài liệu.

**Giải pháp**:
1. Nhấn **Phân tích** để ứng dụng tự chọn cấu hình phù hợp
2. Đối với Học Bạ, đảm bảo chọn đúng vai trò (CBQL, GVCN, GVBM) trong dropdown XPath
3. Xem log bên dưới để kiểm tra thông số đã dùng

### Cần hỗ trợ thêm

- Email: thientam1992@gmail.com
- GitHub Issues: [vimesjscvn/vn-sign-sample/issues](https://github.com/vimesjscvn/vn-sign-sample/issues)
