import Foundation

// ── MQTT Signing Responder ───────────────────────────────────────────────────
// Connects to MQTT broker and responds to sign requests.
// Uses raw TCP/TLS sockets with MQTT 3.1.1 protocol (minimal implementation).
//
// Topics:
//   usbagent/{agentId}/status    — retained presence + Last-Will
//   usbagent/{agentId}/sign/req  — SDK → Agent (subscribe)
//   usbagent/{agentId}/sign/res  — Agent → SDK (publish)
// ─────────────────────────────────────────────────────────────────────────────

class MqttSigningResponder {
    let host: String
    let port: Int
    let username: String
    let password: String
    let useTls: Bool
    let agentId: String
    let httpPort: Int
    let config: AppConfig
    weak var delegate: AppDelegate?

    private var connection: NWConnectionWrapper?
    private var running = false
    private var reconnectDelay: TimeInterval = 5

    private var statusTopic: String { "usbagent/\(agentId)/status" }
    private var signReqTopic: String { "usbagent/\(agentId)/sign/req" }
    private var signResTopic: String { "usbagent/\(agentId)/sign/res" }

    init(config: AppConfig, delegate: AppDelegate?) {
        self.host = config.mqttBrokerHost ?? ""
        self.port = config.mqttBrokerPort
        self.username = config.mqttUsername ?? "usb-agent"
        self.password = config.mqttPassword ?? ""
        self.useTls = config.mqttUseTls
        self.agentId = (config.mqttAgentId?.isEmpty == false) ? config.mqttAgentId! : ProcessInfo.processInfo.hostName
        self.httpPort = config.port
        self.config = config
        self.delegate = delegate
    }

    func start() {
        guard !host.isEmpty else { return }
        running = true
        print("[MQTT] Connecting to \(host):\(port) as '\(agentId)'")
        DispatchQueue.global(qos: .default).async { [weak self] in
            self?.connectLoop()
        }
    }

    func stop() {
        running = false
        connection?.disconnect()
    }

    // MARK: - Connection Loop

    private func connectLoop() {
        while running {
            do {
                let conn = try NWConnectionWrapper(host: host, port: port, useTls: useTls)
                self.connection = conn

                // Send CONNECT
                try sendConnect(conn)

                // Wait for CONNACK
                let connack = try conn.readPacket()
                guard connack.count >= 4, connack[0] == 0x20, connack[3] == 0x00 else {
                    print("[MQTT] CONNACK refused: \(connack.map { String(format: "%02X", $0) }.joined())")
                    Thread.sleep(forTimeInterval: reconnectDelay)
                    continue
                }
                print("[MQTT] Connected to \(host):\(port)")

                // Publish presence
                publishPresence(conn, online: true)

                // Subscribe to sign/req
                try subscribe(conn, topic: signReqTopic)

                // Read loop
                while running {
                    if let packet = try? conn.readPacket(timeout: 30) {
                        if packet.isEmpty {
                            // Timeout — send PINGREQ
                            try conn.send(data: [0xC0, 0x00])
                            continue
                        }
                        handlePacket(conn, packet: packet)
                    } else {
                        break // Connection lost
                    }
                }
            } catch {
                print("[MQTT] Connection error: \(error.localizedDescription)")
            }

            if running {
                print("[MQTT] Reconnecting in \(Int(reconnectDelay))s...")
                Thread.sleep(forTimeInterval: reconnectDelay)
            }
        }
    }

    // MARK: - MQTT Protocol

    private func sendConnect(_ conn: NWConnectionWrapper) throws {
        // Build CONNECT packet (MQTT 3.1.1)
        let clientId = "usbagent-\(agentId)-\(UUID().uuidString.prefix(8))"
        let willPayload = buildPresenceJson(online: false)

        var variableHeader: [UInt8] = []
        // Protocol name
        variableHeader += encodeString("MQTT")
        // Protocol level (4 = 3.1.1)
        variableHeader.append(4)
        // Connect flags: username + password + will retain + will QoS 1 + will flag + clean session
        variableHeader.append(0b11101110)
        // Keep alive (60 seconds)
        variableHeader += [0x00, 0x3C]

        var payload: [UInt8] = []
        payload += encodeString(clientId)
        payload += encodeString(statusTopic) // Will topic
        payload += encodeBytes(Array(willPayload.utf8)) // Will message
        payload += encodeString(username)
        payload += encodeString(password)

        let remainingLength = variableHeader.count + payload.count
        var packet: [UInt8] = [0x10] // CONNECT type
        packet += encodeRemainingLength(remainingLength)
        packet += variableHeader
        packet += payload

        try conn.send(data: packet)
    }

    private func subscribe(_ conn: NWConnectionWrapper, topic: String) throws {
        var packet: [UInt8] = []
        // Fixed header
        packet.append(0x82) // SUBSCRIBE
        let variableHeader: [UInt8] = [0x00, 0x01] // Packet ID = 1
        let payload = encodeString(topic) + [0x01] // QoS 1
        packet += encodeRemainingLength(variableHeader.count + payload.count)
        packet += variableHeader
        packet += payload
        try conn.send(data: packet)
        print("[MQTT] Subscribed to \(topic)")
    }

    private func publish(_ conn: NWConnectionWrapper, topic: String, message: String, retain: Bool = false, qos: UInt8 = 0) {
        var firstByte: UInt8 = 0x30 // PUBLISH
        if retain { firstByte |= 0x01 }
        if qos > 0 { firstByte |= (qos << 1) }

        var variableHeader = encodeString(topic)
        if qos > 0 {
            variableHeader += [0x00, 0x02] // Packet ID
        }
        let payload = Array(message.utf8)

        var packet: [UInt8] = [firstByte]
        packet += encodeRemainingLength(variableHeader.count + payload.count)
        packet += variableHeader
        packet += payload

        try? conn.send(data: packet)
    }

    private func publishPresence(_ conn: NWConnectionWrapper, online: Bool) {
        let json = buildPresenceJson(online: online)
        publish(conn, topic: statusTopic, message: json, retain: true, qos: 1)
    }

    // MARK: - Packet Handling

    private func handlePacket(_ conn: NWConnectionWrapper, packet: [UInt8]) {
        let type = packet[0] & 0xF0
        switch type {
        case 0x30: // PUBLISH
            handlePublish(conn, packet: packet)
        case 0xD0: // PINGRESP
            break
        case 0x90: // SUBACK
            break
        default:
            break
        }
    }

    private func handlePublish(_ conn: NWConnectionWrapper, packet: [UInt8]) {
        // Parse PUBLISH packet
        guard packet.count > 4 else { return }

        var offset = 1
        // Decode remaining length
        var multiplier = 1
        var remainingLength = 0
        while offset < packet.count {
            let byte = packet[offset]
            offset += 1
            remainingLength += Int(byte & 0x7F) * multiplier
            multiplier *= 128
            if byte & 0x80 == 0 { break }
        }

        // Topic length
        guard offset + 2 <= packet.count else { return }
        let topicLen = Int(packet[offset]) << 8 | Int(packet[offset + 1])
        offset += 2
        guard offset + topicLen <= packet.count else { return }
        let topic = String(bytes: packet[offset..<(offset + topicLen)], encoding: .utf8) ?? ""
        offset += topicLen

        // QoS
        let qos = (packet[0] >> 1) & 0x03
        if qos > 0 { offset += 2 } // Skip packet ID

        // Payload
        let payload = Array(packet[offset...])
        let message = String(bytes: payload, encoding: .utf8) ?? ""

        if topic == signReqTopic {
            handleSignRequest(conn, message: message)
        }
    }

    private func handleSignRequest(_ conn: NWConnectionWrapper, message: String) {
        guard let data = message.data(using: .utf8),
              let json = try? JSONSerialization.jsonObject(with: data) as? [String: Any] else {
            print("[MQTT] Sign request not parseable")
            return
        }

        let correlationId = json["correlationId"] as? String ?? ""
        let hashBase64 = json["hashBase64"] as? String ?? ""
        let serial = json["serial"] as? String ?? ""
        let pin = json["pin"] as? String ?? config.tokenPin ?? ""

        delegate?.recordActivity()

        let response: [String: Any]
        do {
            guard !hashBase64.isEmpty else { throw Pkcs11Error.general("hashBase64 is required") }
            guard !serial.isEmpty else { throw Pkcs11Error.general("serial is required") }
            guard let digest = Data(base64Encoded: hashBase64), digest.count == 32 else {
                throw Pkcs11Error.general("hashBase64 must be a 32-byte SHA-256 digest")
            }
            guard !pin.isEmpty else { throw Pkcs11Error.general("PIN is required") }
            guard let (certData, _) = try Pkcs11.findCert(serial: serial, userName: nil, modulePath: config.pkcs11Module) else {
                throw Pkcs11Error.general("Certificate not found on PKCS#11 token")
            }

            let result = try Pkcs11.signDigest(certData: certData, digest: digest, pin: pin, modulePath: config.pkcs11Module)
            response = [
                "correlationId": correlationId,
                "success": true,
                "signatureBase64": result.signature.base64EncodedString(),
                "certificateBase64": result.certRawData.base64EncodedString(),
                "algorithm": result.algorithm,
                "error": NSNull(),
            ]
        } catch {
            response = [
                "correlationId": correlationId,
                "success": false,
                "signatureBase64": NSNull(),
                "certificateBase64": NSNull(),
                "algorithm": NSNull(),
                "error": error.localizedDescription,
            ]
        }

        if let responseData = try? JSONSerialization.data(withJSONObject: response),
           let responseStr = String(data: responseData, encoding: .utf8) {
            publish(conn, topic: signResTopic, message: responseStr, qos: 1)
            let success = response["success"] as? Bool ?? false
            print("[MQTT] Sign response sent (correlationId=\(correlationId), success=\(success))")
        }
    }

    // MARK: - Helpers

    private func buildPresenceJson(online: Bool) -> String {
        var certs: [[String: String]] = []
        if online, let certList = try? Pkcs11.listCerts(modulePath: config.pkcs11Module) {
            certs = certList.map { [
                "serial": $0.serial,
                "subject": $0.subjectDN,
                "algorithm": $0.algorithm,
                "certificate": $0.certificate,
            ] }
        }

        let payload: [String: Any] = [
            "service": "vimes-usb-agent",
            "agentId": agentId,
            "host": ProcessInfo.processInfo.hostName,
            "httpPort": httpPort,
            "online": online,
            "certs": certs,
            "ts": ISO8601DateFormatter().string(from: Date()),
        ]
        guard let data = try? JSONSerialization.data(withJSONObject: payload),
              let str = String(data: data, encoding: .utf8) else { return "{}" }
        return str
    }

    private func encodeString(_ s: String) -> [UInt8] {
        let bytes = Array(s.utf8)
        return [UInt8(bytes.count >> 8), UInt8(bytes.count & 0xFF)] + bytes
    }

    private func encodeBytes(_ bytes: [UInt8]) -> [UInt8] {
        return [UInt8(bytes.count >> 8), UInt8(bytes.count & 0xFF)] + bytes
    }

    private func encodeRemainingLength(_ length: Int) -> [UInt8] {
        var result: [UInt8] = []
        var len = length
        repeat {
            var byte = UInt8(len % 128)
            len /= 128
            if len > 0 { byte |= 0x80 }
            result.append(byte)
        } while len > 0
        return result
    }
}

// MARK: - TCP/TLS Connection Wrapper

class NWConnectionWrapper {
    private var inputStream: InputStream?
    private var outputStream: OutputStream?

    init(host: String, port: Int, useTls: Bool) throws {
        var readStream: Unmanaged<CFReadStream>?
        var writeStream: Unmanaged<CFWriteStream>?
        CFStreamCreatePairWithSocketToHost(nil, host as CFString, UInt32(port), &readStream, &writeStream)

        guard let input = readStream?.takeRetainedValue() as InputStream?,
              let output = writeStream?.takeRetainedValue() as OutputStream? else {
            throw Pkcs11Error.general("Failed to create socket streams")
        }

        if useTls {
            input.setProperty(StreamSocketSecurityLevel.tlSv1, forKey: .socketSecurityLevelKey)
            output.setProperty(StreamSocketSecurityLevel.tlSv1, forKey: .socketSecurityLevelKey)
            // Use system trust store (Let's Encrypt is trusted)
            let sslSettings: [String: Any] = [
                kCFStreamSSLValidatesCertificateChain as String: true,
                kCFStreamSSLPeerName as String: host,
            ]
            input.setProperty(sslSettings, forKey: kCFStreamPropertySSLSettings as Stream.PropertyKey)
            output.setProperty(sslSettings, forKey: kCFStreamPropertySSLSettings as Stream.PropertyKey)
        }

        input.open()
        output.open()

        // Wait for connection
        let deadline = Date().addingTimeInterval(10)
        while input.streamStatus == .opening && Date() < deadline {
            Thread.sleep(forTimeInterval: 0.1)
        }
        guard input.streamStatus == .open else {
            throw Pkcs11Error.general("Connection timed out to \(host):\(port)")
        }

        self.inputStream = input
        self.outputStream = output
    }

    func send(data: [UInt8]) throws {
        guard let output = outputStream else { throw Pkcs11Error.general("Not connected") }
        data.withUnsafeBufferPointer { ptr in
            _ = output.write(ptr.baseAddress!, maxLength: data.count)
        }
    }

    func readPacket(timeout: TimeInterval = 60) throws -> [UInt8] {
        guard let input = inputStream else { throw Pkcs11Error.general("Not connected") }

        let deadline = Date().addingTimeInterval(timeout)
        while !input.hasBytesAvailable && Date() < deadline {
            Thread.sleep(forTimeInterval: 0.05)
        }
        if !input.hasBytesAvailable { return [] } // Timeout

        var buffer = [UInt8](repeating: 0, count: 65536)
        let bytesRead = input.read(&buffer, maxLength: buffer.count)
        guard bytesRead > 0 else { throw Pkcs11Error.general("Connection closed") }
        return Array(buffer.prefix(bytesRead))
    }

    func disconnect() {
        inputStream?.close()
        outputStream?.close()
        inputStream = nil
        outputStream = nil
    }
}
