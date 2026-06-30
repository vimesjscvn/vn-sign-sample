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
├── app.config                 # Cấu hình
└── installer/setup.iss        # InnoSetup script
```

## Phân phối

Agent được phân phối qua [Releases](https://github.com/vimesjscvn/vn-sign-sample/releases) của repo này.
Ứng dụng VimesSign tự động tải agent khi build trên Windows (qua `build/download-vmsign-agent.ps1`).
