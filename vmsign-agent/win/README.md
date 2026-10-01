# VMSignAgent — Windows (.NET Framework 4.6.1)

Agent chạy nền trên Windows, cung cấp HTTP API để ký số qua USB Token (PKCS#11) và Windows Certificate Store (CNG).

## Tính năng

- **HTTP API** tại `localhost:9999` — ký hash, liệt kê chứng thư
- **PKCS#11** — ký trực tiếp qua USB Token (Pkcs11Interop)
- **CNG fallback** — ký qua Windows Certificate Store nếu không có PIN
- **MQTT** — hỗ trợ ký từ xa qua MQTT broker
- **Tự khởi động** — có thể cấu hình chạy cùng Windows

## Yêu cầu

- Windows 10/11 x64
- .NET Framework 4.6.1 (có sẵn trên Windows 10+, không cần cài thêm)
- USB Token với driver đã cài (bit4id, SafeNet, ePass...)

## Build

```bash
dotnet build VMSignAgent.csproj -c Release
# Output: bin\Release\net461\VMSignAgent.exe
```

## Chạy

```bash
# Chạy nền với tray icon
VMSignAgent.exe

# Hoặc sau khi cài setup.exe, chạy từ Program Files
```

## HTTP API

| Method | Path | Mô tả |
|--------|------|-------|
| POST | `/certs` | Liệt kê chứng thư số trong Windows Personal store |
| POST | `/login` | Tìm chứng thư theo serial/CN |
| POST | `/signHash` | Ký SHA-256 digest (PKCS#11 nếu có PIN, CNG nếu không) |

## Cấu hình

File `app.config`:

```xml
<appSettings>
  <add key="Port" value="9999" />
  <add key="Token:Pin" value="" />
  <add key="Token:SelectedCertificateSerial" value="" />
  <add key="Ui:ShowSignSuccessToast" value="true" />
  <add key="EndUser:PhoneNumber" value="" />
  <add key="Token:Pkcs11Module" value="" />
  <add key="Mqtt:BrokerHost" value="108.108.108.251" />
  <add key="Mqtt:BrokerPort" value="1883" />
  <add key="Mqtt:Username" value="" />
  <add key="Mqtt:Password" value="" />
</appSettings>
```

For SignSDK USB API over MQTT, call with `mid = USB`, `user_name = EndUser:PhoneNumber`, and `password = Token:Pin`. Leave MQTT username/password blank when the broker allows anonymous connections.

To check the broker link: **Settings → Test Connection** tries the values on screen (before saving). While running, the tray tooltip shows `MQTT connected` / `MQTT disconnected`, a balloon pops up when the link comes up, fails or drops, and **Status** shows the last reason (e.g. `Broker refused the connection: NotAuthorized`).

## Ký thử (Test Sign PDF)

Tray menu → **Test Sign PDF...** ký một PDF mẫu đúng như phần mềm bệnh viện ký, và báo khâu nào hỏng:

| Bước | Kiểm tra | Hỏng thường là |
|------|----------|----------------|
| 1/4 USB token | ký một digest ngẫu nhiên ngay trên máy rồi verify bằng chứng thư | sai PIN, token chưa cắm, chọn nhầm chứng thư |
| 2/4 Signing server | `POST /api/v1/Signature/login` (mid=USB) | sai API URL, máy không tới được server |
| 3/4 Server → MQTT → agent | `credentials/list`: server tìm agent theo SĐT qua broker, kiểm tra SĐT+PIN | server không tới broker, agent và server khác broker, sai SĐT |
| 4/4 Sign | `sign/multi` với PDF mẫu, server gửi hash về chính agent này qua MQTT | lỗi phía server khi đính chữ ký |

Cần điền **Settings → Signing Server → API URL** (địa chỉ phần mềm bệnh viện ký qua; dán cả dạng `.../api/v1/Signature` cũng được). SĐT, PIN, chứng thư lấy từ Settings đã lưu — đổi xong phải restart agent thì phần MQTT mới dùng giá trị mới. Nếu bước 1 báo sai PIN thì dừng ngay, không gửi lên server, để không tốn thêm một lần thử PIN của token.

File đã ký lưu ở `Documents\VMSignAgent\test-signed-<giờ>.pdf` và tự mở ra. Mỗi lần ký thử tạo một dòng log ký thật trên server, `trans_id` bắt đầu bằng `VMSIGN-TEST-`.

For USB token signing, install the token vendor driver normally. If you need to ship a local driver DLL for testing, place `bit4xpki.dll` beside `VMSignAgent.exe`; the app will use that local DLL first, then fall back to `C:\Windows\System32\bit4xpki.dll`. Do not commit this vendor DLL to git.

## Cấu trúc

```
vmsign-agent/win/
├── VMSignAgent.csproj       # .NET Framework 4.6.1 project
├── Program.cs                 # Entry point, HTTP listener
├── TokenSigner.cs             # Logic ký (PKCS#11 + CNG)
├── Pkcs11Signer.cs            # PKCS#11 wrapper (Pkcs11Interop)
├── MqttSigningResponder.cs    # MQTT transport
├── MqttTlsConfig.cs           # MQTT TLS configuration
├── SignTest.cs                # Test Sign: token → API → MQTT → signed PDF
├── SignTestForm.cs            # Test Sign window
├── SamplePdf.cs               # one-page PDF sent by Test Sign
├── app.config                 # Cấu hình
└── installer/setup.iss        # InnoSetup script
```

## Phân phối

Agent được phân phối qua [Releases](https://github.com/vimesjscvn/vn-sign-sample/releases) của repo này.
Ứng dụng VimesSign tự động tải agent khi build trên Windows (qua `build/download-vmsign-agent.ps1`).
