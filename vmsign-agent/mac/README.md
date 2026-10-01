# VMSignAgent — macOS (Swift Native)

Agent chạy nền trên thanh menu macOS, cung cấp HTTP API để ký số qua USB Token (PKCS#11).

## Tính năng

- **HTTP API** tại `localhost:9999` — ký hash, liệt kê chứng thư
- **MQTT** — hỗ trợ ký từ xa qua MQTT broker (cross-subnet)
- **MQTT Auth** — xác thực phone + PIN qua MQTT (tương thích Windows agent)
- **UDP Discovery** — tự phát hiện agent trên mạng LAN
- **Menu bar** — chạy nền, không Dock icon
- **Trạng thái MQTT** — menu bar báo agent có nối được broker không, kèm lý do; Settings → Test Connection thử kết nối trước khi lưu (giống bản Windows)
- **Cửa sổ cài đặt** — cấu hình MQTT, PIN, PKCS#11 module, phone number
- **Ký thử (Test Sign PDF)** — menu bar → 🧪 Test Sign PDF: ký thử token, đăng nhập API, server → MQTT → agent, rồi ký một PDF mẫu qua server; báo khâu nào hỏng (giống bản Windows, xem `../win/README.md`)

## Ký thử

Cần `SignApi.BaseUrl` (Settings → Signing Server → API URL), `EndUser.PhoneNumber`, MQTT, và PIN qua biến môi trường `TOKEN__PIN`. Lấy chứng thư ký đầu tiên trên token (bỏ qua chứng thư CA). File đã ký lưu ở `~/Library/Application Support/VMSignAgent/test-signed/` (không dùng `~/Documents` để khỏi bật hộp thoại xin quyền) và tự mở. Mỗi lần ký thử tạo một dòng log ký thật trên server, `trans_id` bắt đầu bằng `VMSIGN-TEST-`.

Ký thử gọi server bằng `URLSession`, nên chịu App Transport Security: mặc định chặn `http://` tới tên miền, và từ macOS 14 chặn cả `http://` tới địa chỉ IP. Info.plist của bản build (`build-vmsign-agent-mac.yml`, và bản agent trong `build-sign-app.yml`) bật `NSAllowsArbitraryLoads` để gọi được `http://host:port`. Không thêm `NSAllowsLocalNetworking` hay `NSAllowsArbitraryLoadsForMedia`/`InWebContent` cạnh nó: có một trong các key đó là macOS bỏ qua `NSAllowsArbitraryLoads`. Chạy thẳng `.build/release/VMSignAgent` thì không có Info.plist, `http://` bị chặn và bước 2 báo `macOS refused plain http ... (App Transport Security)`.

## Kết nối MQTT

- Menu bar, ngay dưới dòng trạng thái (chỉ khi đã cấu hình MQTT): `MQTT: connecting...` lúc khởi động, sau đó `MQTT: connected to host:port` hoặc `MQTT: not connected - <lý do>`. Khi không nối được, icon đổi từ `lock.fill` sang `lock.slash`. Lý do chỉ ra khâu hỏng, ví dụ `Cannot connect to host:1883: Connection refused`, `... - if this port uses TLS, turn on Use TLS`, `Broker refused the connection: not authorized (CONNACK 5) - check Username and Password`.
- Agent thử lại mỗi 5 giây; menu chỉ đổi khi trạng thái hoặc lý do đổi. Mất mạng đột ngột: 30 giây không nhận được gì thì agent gửi PINGREQ, thêm 30 giây không có trả lời thì coi là mất kết nối, nên menu báo trong khoảng 60 giây.
- **Settings → MQTT → Test Connection** kết nối một lần rồi ngắt, với giá trị đang nhập (host, port, username, password, Use TLS), chưa cần lưu. Không gửi presence, không đặt Last Will, dùng client id riêng, nên không ảnh hưởng agent đang chạy.
- Bản macOS chỉ dùng trust store của hệ thống: `CA Cert`, `Client PFX`, `Allow Untrusted` trong Settings chưa được dùng, kể cả khi Test Connection.
- macOS 15 trở lên: app phải được cho phép trong System Settings → Privacy & Security → Local Network thì mới tới được broker, server ký và UDP discovery trong LAN. Chưa cho phép (hoặc đã từ chối) thì kết nối hỏng với `No route to host - on macOS 15 or later, also check Privacy & Security > Local Network`. Agent thử lại mỗi 5 giây, nên bấm Allow xong là tự nối lại.
- Agent subscribe cả topic `usbagent/<agentId>/status` của chính nó. Sau khi mạng chập chờn khoảng một phút, broker có thể đăng Last Will của kết nối cũ (`online:false`, retained) đè lên presence của kết nối mới; agent thấy thì đăng lại `online:true` ngay, để server vẫn tìm thấy agent.
- `MQTT: connected` chỉ chứng minh chiều agent → broker. Chiều server → broker kiểm tra bằng Test Sign PDF (bước 3).

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
