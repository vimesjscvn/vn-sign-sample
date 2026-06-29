# Vimes MQTT Broker — USB Token Signing Transport

MQTT broker dùng để kết nối **USB Token Agent** (máy người dùng) với **Web/App SDK** (server ký số) qua mạng internet, thay thế giao thức HTTP chỉ hoạt động cùng mạng LAN.

## Kiến trúc

```
┌──────────────────┐         ┌─────────────────────┐         ┌──────────────────┐
│  Web/App SDK     │         │   MQTT Broker        │         │ USB Token Agent  │
│  (Server ký số)  │◄───────►│   (Public Internet)  │◄───────►│ (Máy người dùng) │
│                  │  TLS    │   Port 8883          │  TLS    │                  │
│  UsbMqttClient   │         │   mqtt.intellisoftjsc│         │ MqttSigningResp  │
└──────────────────┘         │        .cloud        │         └──────────────────┘
                             └─────────────────────┘
```

## Topic Structure

| Topic | Hướng | Mục đích |
|-------|-------|----------|
| `usbagent/{agentId}/status` | Agent → Broker (retained) | Trạng thái online + danh sách chứng thư số |
| `usbagent/{agentId}/sign/req` | SDK → Agent | Yêu cầu ký (hash SHA-256) |
| `usbagent/{agentId}/sign/res` | Agent → SDK | Kết quả ký (chữ ký số) |
| `usbagent/+/status` | SDK subscribe (wildcard) | Khám phá tất cả agent online |

## Luồng hoạt động

1. **Agent khởi động** → kết nối broker, publish retained presence (danh sách cert trên token)
2. **SDK discover** → subscribe `usbagent/+/status`, thu thập agent nào đang online
3. **SDK gửi yêu cầu ký** → publish `usbagent/{agentId}/sign/req` với hash + serial cert
4. **Agent ký** → nhận request, ký bằng PKCS#11, publish response tới `usbagent/{agentId}/sign/res`
5. **Agent offline** → Last-Will message tự động publish `online: false`

## Tài khoản MQTT

| User | Password | Vai trò |
|------|----------|---------|
| `sdk-server` | `Sdk@Sign2024` | SDK/Web app — discover agents + gửi yêu cầu ký |
| `usb-agent` | `UsbAgent@Sign2024` | Tất cả USB Token Agent (dùng chung) |

## Production Broker

| Thông tin | Giá trị |
|-----------|---------|
| Host | `mqtt.intellisoftjsc.cloud` |
| Port | `8883` (TLS) |
| TLS | Let's Encrypt (tự động gia hạn) |
| VM | Google Cloud `e2-small`, zone `asia-southeast1-a` |
| IP | `35.240.169.42` |

---

## Tích hợp USB Token Agent

### Cấu hình `appsettings.json`

```json
{
  "Mqtt": {
    "BrokerHost": "mqtt.intellisoftjsc.cloud",
    "BrokerPort": 8883,
    "Username": "usb-agent",
    "Password": "UsbAgent@Sign2024",
    "AgentId": "",
    "UseTls": true,
    "CaCertPath": "",
    "ClientPfxPath": "",
    "ClientPfxPassword": "",
    "AllowUntrusted": false
  }
}
```

### Giải thích

| Field | Mô tả |
|-------|--------|
| `BrokerHost` | Địa chỉ MQTT broker. Để trống = tắt MQTT (dùng HTTP localhost) |
| `BrokerPort` | Port kết nối. `8883` = TLS, `1883` = không mã hóa (chỉ dev) |
| `Username` | Tài khoản MQTT |
| `Password` | Mật khẩu MQTT |
| `AgentId` | ID duy nhất của agent. Để trống = tự sinh từ hostname máy |
| `UseTls` | Bật TLS. **Luôn `true` ở production** |
| `CaCertPath` | Đường dẫn CA cert (không cần nếu dùng Let's Encrypt — OS trust store đã có) |
| `AllowUntrusted` | **CHỈ DÙNG DEV** — bỏ qua kiểm tra TLS cert |

### Chạy local (dev) với Docker

```bash
cd mqtt/
docker compose up -d

# Agent config cho local:
# BrokerHost: localhost
# BrokerPort: 1883
# UseTls: false
# Username: usb-agent
# Password: UsbAgent@Sign2024
```

---

## Tích hợp Web/App SDK

### Cấu hình `appsettings.json`

Thêm section `UsbSetting` vào `AppSettings`:

```json
{
  "AppSettings": {
    "UsbSetting": {
      "UsbMqttBrokerHost": "mqtt.intellisoftjsc.cloud",
      "UsbMqttBrokerPort": 8883,
      "UsbMqttUsername": "sdk-server",
      "UsbMqttPassword": "Sdk@Sign2024",
      "UsbMqttUseTls": true,
      "UsbMqttCaCertPath": "",
      "UsbMqttClientPfxPath": "",
      "UsbMqttClientPfxPassword": "",
      "UsbMqttAllowUntrusted": false
    }
  }
}
```

### Chuyển đổi HTTP ↔ MQTT

SDK tự động chọn transport:

- `UsbMqttBrokerHost` **có giá trị** → dùng MQTT (cross-subnet, internet)
- `UsbMqttBrokerHost` **trống** → dùng HTTP trực tiếp (cùng LAN, `http://127.0.0.1:9999`)

### Code flow trong SDK

```csharp
// SDK tự động detect:
private bool UseMqtt => !string.IsNullOrWhiteSpace(_appSettings.UsbSetting?.UsbMqttBrokerHost);

// Discover agents:
var agents = CreateMqttClient().Discover(waitMs: 2000);

// Sign:
var signature = CreateMqttClient().SignHash(agentId, base64Hash, serial, pin);
```

### Chạy local (dev) với Docker

```bash
cd mqtt/
docker compose up -d

# SDK config cho local:
# UsbMqttBrokerHost: localhost
# UsbMqttBrokerPort: 1883
# UsbMqttUseTls: false
# UsbMqttUsername: sdk-server
# UsbMqttPassword: Sdk@Sign2024
```

---

## Message Contracts

### Presence (status)

```json
{
  "service": "vimes-usb-agent",
  "agentId": "my-pc-hostname",
  "host": "my-pc-hostname",
  "httpPort": 9999,
  "online": true,
  "certs": [
    {
      "serial": "ABC123DEF456",
      "subject": "CN=Nguyen Van A, O=Company",
      "algorithm": "RSA",
      "certificate": "MIIBxTCCAW..."
    }
  ],
  "ts": "2026-06-29T10:30:00+07:00"
}
```

### Sign Request

```json
{
  "correlationId": "a1b2c3d4e5f6",
  "hashBase64": "dGVzdC1oYXNoLXNoYTI1Ni0zMi1ieXRlcw==",
  "serial": "ABC123DEF456",
  "pin": "1234"
}
```

### Sign Response

```json
{
  "correlationId": "a1b2c3d4e5f6",
  "success": true,
  "signatureBase64": "MEUCIQD...",
  "certificateBase64": "MIIBxTCCAW...",
  "algorithm": "RSA",
  "error": null
}
```

---

## Troubleshooting

| Vấn đề | Nguyên nhân | Giải pháp |
|---------|-------------|-----------|
| Agent không kết nối được | Firewall chặn port 8883 | Mở outbound TCP 8883 |
| `CONNACK refused` | Sai username/password | Kiểm tra credentials |
| SDK không thấy agent | Agent chưa online hoặc chưa publish presence | Kiểm tra agent log |
| Sign timeout (30s) | Agent offline hoặc USB token chưa cắm | Kiểm tra agent + token |
| TLS handshake failed | Cert hết hạn hoặc sai CA | Kiểm tra cert broker |

---

## Bảo mật

- **TLS 1.2+**: Mã hóa toàn bộ traffic
- **Username/Password**: Xác thực client
- **ACL**: `sdk-server` chỉ đọc status + sign; `usb-agent` chỉ write status + sign/res
- **PIN**: Bảo vệ private key trên USB token (không lưu trên broker)
- **Let's Encrypt**: Cert tự động gia hạn, OS trust store verify
