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
# Chạy nền (không cửa sổ)
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
  <add key="Pkcs11Module" value="C:\bit4id\bit4xpki.dll" />
</appSettings>
```

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

Agent được phân phối qua repo công khai [tamnguyendev/vmsign-agent-dist](https://github.com/tamnguyendev/vmsign-agent-dist).  
Ứng dụng VimesSign tự động tải agent khi build trên Windows (qua `build/download-vmsign-agent.ps1`).
