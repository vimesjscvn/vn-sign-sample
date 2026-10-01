# VMSignAgent — macOS (Swift Native)

Agent chạy nền trên thanh menu macOS, cung cấp HTTP API để ký số qua USB Token (PKCS#11).

## Tính năng

- **HTTP API** tại `localhost:9999` — ký hash, liệt kê chứng thư
- **MQTT** — hỗ trợ ký từ xa qua MQTT broker (cross-subnet)
- **MQTT Auth** — xác thực phone + PIN qua MQTT (tương thích Windows agent)
- **UDP Discovery** — tự phát hiện agent trên mạng LAN
- **Menu bar** — chạy nền, không Dock icon
- **Cửa sổ cài đặt** — cấu hình MQTT, PIN, PKCS#11 module, phone number
- **Ký thử (Test Sign PDF)** — menu bar → 🧪 Test Sign PDF: ký thử token, đăng nhập API, server → MQTT → agent, rồi ký một PDF mẫu qua server; báo khâu nào hỏng (giống bản Windows, xem `../win/README.md`)

## Ký thử

Cần `SignApi.BaseUrl` (Settings → Signing Server → API URL), `EndUser.PhoneNumber`, MQTT, và PIN qua biến môi trường `TOKEN__PIN`. Lấy chứng thư ký đầu tiên trên token (bỏ qua chứng thư CA). File đã ký lưu ở `~/Library/Application Support/VMSignAgent/test-signed/` (không dùng `~/Documents` để khỏi bật hộp thoại xin quyền) và tự mở. Mỗi lần ký thử tạo một dòng log ký thật trên server, `trans_id` bắt đầu bằng `VMSIGN-TEST-`.

## Yêu cầu

- macOS 14 (Sonoma) trở lên
- Apple Silicon (arm64)
- USB Token với driver PKCS#11 (bit4id `libbit4xpki.dylib` đi kèm)
- Swift 5.9+ (cho build từ mã nguồn)

## Build

```bash
swift build -c release
# Output: .build/release/VMSignAgent
```

## Chạy

```bash
.build/release/VMSignAgent
# Hoặc sau khi cài .pkg:
open /Applications/VMSignAgent.app
```

## HTTP API

| Method | Path | Mô tả |
|--------|------|-------|
| POST | `/certs` | Liệt kê chứng thư số trên USB Token |
| POST | `/login` | Tìm chứng thư theo serial/CN |
| POST | `/signHash` | Ký SHA-256 digest qua PKCS#11 |

## Cấu hình

File `Resources/appsettings.json` (hoặc copy sang `~/.config/vimes-sign/`):

```json
{
  "Port": 9999,
  "DiscoveryPort": 9998,
  "Token": {
    "Pkcs11Module": "pkcs11/libbit4xpki.dylib"
  },
  "EndUser": {
    "PhoneNumber": "0912345678"
  },
  "SignApi": {
    "BaseUrl": "http://10.0.0.5:8081"
  },
  "Mqtt": {
    "BrokerHost": "mqtt.example.com",
    "BrokerPort": 8883,
    "UseTls": true
  }
}
```

## Cấu trúc

```
vmsign-agent/mac/
├── Package.swift              # Swift Package Manager manifest
├── Sources/
│   ├── main.swift             # Entry point (NSApplication)
│   ├── AppDelegate.swift      # Menu bar, tray icon
│   ├── HttpServer.swift       # HTTP API server
│   ├── Pkcs11.swift           # PKCS#11 bridge (ký, liệt kê cert)
│   ├── MqttClient.swift       # MQTT transport
│   ├── UdpDiscovery.swift     # UDP broadcast discovery
│   ├── SettingsWindow.swift   # Cửa sổ cài đặt
│   ├── SignTest.swift         # Ký thử: token → API → MQTT → PDF đã ký
│   ├── SignTestWindow.swift   # Cửa sổ ký thử
│   ├── SamplePdf.swift        # PDF mẫu một trang gửi khi ký thử
│   └── AppConfig.swift        # Đọc/ghi config
├── CPkcs11/                   # C bridge header cho PKCS#11
├── Resources/
│   ├── AppIcon.icns           # Icon ứng dụng
│   ├── appsettings.example.json
│   └── pkcs11/libbit4xpki.dylib  # Driver PKCS#11
└── entitlements.plist         # Hardened runtime (USB, network, dylib)
```
