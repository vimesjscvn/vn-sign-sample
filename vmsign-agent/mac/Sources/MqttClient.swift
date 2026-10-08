import Foundation
import Security

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

    // How long the broker gets to answer CONNECT: ten seconds, as the Windows agent's Test
    // Connection allows. Something that accepts the socket but never answers - a TLS setting that
    // does not match the port, or not an MQTT broker at all - would otherwise hold every attempt
    // for the read's 60s default.
    private static let connackTimeout: TimeInterval = 10
    // Inbound silence after which the link is probed with PINGREQ - see readLoop.
    private static let pingInterval: TimeInterval = 30

    // The link state last passed to the menu bar. Only the connect loop's thread touches these.
    private var reportedConnected: Bool?
    private var reportedDetail = ""

    private var statusTopic: String { "usbagent/\(agentId)/status" }
    private var signReqTopic: String { "usbagent/\(agentId)/sign/req" }
    private var signResTopic: String { "usbagent/\(agentId)/sign/res" }
    private var authReqTopic: String { "usbagent/\(agentId)/auth/req" }
    private var authResTopic: String { "usbagent/\(agentId)/auth/res" }

    init(config: AppConfig, delegate: AppDelegate?) {
        self.host = config.mqttBrokerHost ?? ""
        self.port = config.mqttBrokerPort
        self.username = config.mqttUsername ?? "vmsign-agent"
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
            // Set once the broker has accepted us, so a later failure is reported as a drop.
            var linkUp = false
            do {
                let conn = try NWConnectionWrapper(host: host, port: port, useTls: useTls)
                self.connection = conn
                defer { conn.disconnect() }

                // CONNECT, then wait for CONNACK
                try MqttSigningResponder.handshake(conn, connect: agentConnectPacket())
                print("[MQTT] Connected to \(host):\(port)")

                // Publish presence
                publishPresence(conn, online: true)

                // Subscribe to sign/req and auth/req
                try subscribe(conn, topic: signReqTopic)
                try subscribe(conn, topic: authReqTopic)
                // And to this agent's own status topic, to catch a stale Last-Will - see
                // handleStatus. QoS 0: the broker sends these without waiting for an ack.
                try subscribe(conn, topic: statusTopic, qos: 0)

                linkUp = true
                reportConnection(connected: true, detail: "connected to \(host):\(port)")

                try readLoop(conn)
            } catch {
                // stop() closes the socket under the loop; shutting down is not a failure.
                if running {
                    var reason = error.localizedDescription
                    if linkUp {
                        reason = "Connection lost: \(reason)"
                    }
                    print("[MQTT] \(reason)")
                    reportConnection(connected: false, detail: reason)
                }
            }

            if running {
                print("[MQTT] Reconnecting in \(Int(reconnectDelay))s...")
                Thread.sleep(forTimeInterval: reconnectDelay)
            }
        }
    }

    /// Handles packets until the link fails, then throws the reason. Silence is answered with
    /// PINGREQ, and silence after that means the link is gone: one that died without a FIN or
    /// RST (Wi-Fi switched off, a NAT dropping the mapping) otherwise looks idle until TCP gives
    /// up retransmitting, minutes later, and all that time the menu would say "connected".
    private func readLoop(_ conn: NWConnectionWrapper) throws {
        var pingOutstanding = false
        while running {
            let packet = try conn.readPacket(timeout: MqttSigningResponder.pingInterval)
            if packet.isEmpty {
                if pingOutstanding {
                    let silence = Int(2 * MqttSigningResponder.pingInterval)
                    throw Pkcs11Error.general("No answer from the broker for \(silence)s")
                }
                try conn.send(data: [0xC0, 0x00]) // PINGREQ
                pingOutstanding = true
                continue
            }
            pingOutstanding = false
            handlePacket(conn, packet: packet)
        }
    }

    /// Passes the link state to the menu bar, on the main queue. This is a menu bar app with no
    /// console anyone reads, so without it a broker the agent could not reach failed silently
    /// while the menu said "Running". Only changes go through: the loop retries every few
    /// seconds, and a broker that stays down would otherwise rewrite the menu on every attempt.
    private func reportConnection(connected: Bool, detail: String) {
        if connected == reportedConnected && detail == reportedDetail { return }
        reportedConnected = connected
        reportedDetail = detail
        DispatchQueue.main.async { [weak self] in
            self?.delegate?.mqttConnectionChanged(connected: connected, detail: detail)
        }
    }

    // MARK: - Test Connection

    /// One-off connect and disconnect against a broker, for Settings > Test Connection.
    /// Sends no Last-Will and publishes no presence, and uses its own client id, so it leaves a
    /// running agent and its status topic alone. Blocking, like the connect loop: call it off
    /// the main thread.
    /// - Returns: nil when the broker accepted the connection, otherwise the reason it did not.
    static func testConnection(host: String, port: Int, username: String, password: String, useTls: Bool) -> String? {
        do {
            let conn = try NWConnectionWrapper(host: host, port: port, useTls: useTls)
            defer { conn.disconnect() }
            let clientId = "usbagent-test-\(UUID().uuidString.prefix(8))"
            let connect = connectPacket(clientId: clientId, username: username, password: password, will: nil)
            try handshake(conn, connect: connect)
            // DISCONNECT, so the broker logs a clean close rather than a lost client.
            try? conn.send(data: [0xE0, 0x00])
            return nil
        } catch {
            return error.localizedDescription
        }
    }

    // MARK: - MQTT Protocol

    /// Sends CONNECT and waits for CONNACK; throws, worded for the menu bar, unless the broker
    /// accepted. Test Connection goes through here too, so it reports a failure in the same
    /// words the menu bar would.
    private static func handshake(_ conn: NWConnectionWrapper, connect: [UInt8]) throws {
        try conn.send(data: connect)
        var connack: [UInt8]
        do {
            connack = try conn.readPacket(timeout: connackTimeout)
        } catch {
            // Something answered but never sent a whole MQTT packet before hanging up - an HTTP
            // or SSH port, say. What it sent says more than the hang-up does.
            guard !conn.pending.isEmpty else { throw error }
            connack = conn.pending
        }
        if connack.isEmpty {
            // Timed out, possibly part-way through an answer that is not MQTT at all.
            connack = conn.pending
        }
        if let refusal = describeConnack(connack) {
            throw Pkcs11Error.general(refusal)
        }
    }

    /// Why the broker did not accept CONNECT, from the first bytes it sent back; nil when it did.
    private static func describeConnack(_ packet: [UInt8]) -> String? {
        if packet.isEmpty {
            return "Timed out waiting for the broker to answer (\(Int(connackTimeout))s)"
        }
        // TLS record types (alert 0x15, handshake 0x16): a TLS listener answering plain MQTT.
        if packet[0] == 0x15 || packet[0] == 0x16 {
            return "The broker answered with TLS - turn on Use TLS for this port"
        }
        guard packet.count >= 4, packet[0] == 0x20 else {
            let head = packet.prefix(8).map { String(format: "%02X", $0) }.joined(separator: " ")
            return "Unexpected answer from the broker (\(head)) - is this an MQTT port?"
        }
        // CONNACK return codes, MQTT 3.1.1 section 3.2.2.3.
        let refused = "Broker refused the connection"
        switch packet[3] {
        case 0x00: return nil
        case 0x01: return "\(refused): unacceptable protocol version (CONNACK 1)"
        case 0x02: return "\(refused): client identifier rejected (CONNACK 2)"
        case 0x03: return "\(refused): server unavailable (CONNACK 3)"
        case 0x04: return "\(refused): bad user name or password (CONNACK 4) - check Username and Password"
        // Mosquitto answers 5 rather than 4 to a wrong username or password as well.
        case 0x05: return "\(refused): not authorized (CONNACK 5) - check Username and Password"
        default: return "\(refused): return code \(packet[3])"
        }
    }

    /// The agent's own CONNECT: the offline presence goes in as the retained Last-Will.
    private func agentConnectPacket() -> [UInt8] {
        let clientId = "usbagent-\(agentId)-\(UUID().uuidString.prefix(8))"
        let will = (topic: statusTopic, payload: buildPresenceJson(online: false))
        return MqttSigningResponder.connectPacket(clientId: clientId, username: username,
                                                  password: password, will: will)
    }

    /// CONNECT (MQTT 3.1.1). `will` is the retained Last-Will the broker publishes when the link
    /// drops; Test Connection passes nil, so a test never touches the agent's status topic.
    private static func connectPacket(clientId: String, username: String, password: String,
                                      will: (topic: String, payload: String)?) -> [UInt8] {
        var variableHeader: [UInt8] = []
        // Protocol name
        variableHeader += encodeString("MQTT")
        // Protocol level (4 = 3.1.1)
        variableHeader.append(4)
        // Connect flags: username + password + clean session; with a will, also will retain +
        // will QoS 1 + will flag
        var flags: UInt8 = 0b11000010
        if will != nil {
            flags |= 0b00101100
        }
        variableHeader.append(flags)
        // Keep alive (60 seconds)
        variableHeader += [0x00, 0x3C]

        var payload: [UInt8] = []
        payload += encodeString(clientId)
        if let lastWill = will {
            payload += encodeString(lastWill.topic) // Will topic
            payload += encodeBytes(Array(lastWill.payload.utf8)) // Will message
        }
        payload += encodeString(username)
        payload += encodeString(password)

        let remainingLength = variableHeader.count + payload.count
        var packet: [UInt8] = [0x10] // CONNECT type
        packet += encodeRemainingLength(remainingLength)
        packet += variableHeader
        packet += payload
        return packet
    }

    private func subscribe(_ conn: NWConnectionWrapper, topic: String, qos: UInt8 = 1) throws {
        var packet: [UInt8] = []
        // Fixed header
        packet.append(0x82) // SUBSCRIBE
        let variableHeader: [UInt8] = [0x00, 0x01] // Packet ID = 1
        let payload = MqttSigningResponder.encodeString(topic) + [qos] // Requested QoS
        packet += MqttSigningResponder.encodeRemainingLength(variableHeader.count + payload.count)
        packet += variableHeader
        packet += payload
        try conn.send(data: packet)
        print("[MQTT] Subscribed to \(topic)")
    }

    private func publish(_ conn: NWConnectionWrapper, topic: String, message: String, retain: Bool = false, qos: UInt8 = 0) {
        var firstByte: UInt8 = 0x30 // PUBLISH
        if retain { firstByte |= 0x01 }
        if qos > 0 { firstByte |= (qos << 1) }

        var variableHeader = MqttSigningResponder.encodeString(topic)
        if qos > 0 {
            variableHeader += [0x00, 0x02] // Packet ID
        }
        let payload = Array(message.utf8)

        var packet: [UInt8] = [firstByte]
        packet += MqttSigningResponder.encodeRemainingLength(variableHeader.count + payload.count)
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
        if qos > 0 {
            // PUBACK with the packet id. The broker counts a QoS 1 message as in flight until it
            // is acknowledged, and mosquitto holds back the rest once 20 are (its
            // max_inflight_messages default): without this the agent would stop receiving sign
            // and auth requests after about twenty on one connection, while still answering
            // PINGREQ, so the menu would keep saying "connected". QoS 2 never arrives: no
            // subscription asks for more than 1.
            guard offset + 2 <= packet.count else { return }
            try? conn.send(data: [0x40, 0x02, packet[offset], packet[offset + 1]])
            offset += 2
        }

        // Payload
        let payload = Array(packet[offset...])
        let message = String(bytes: payload, encoding: .utf8) ?? ""

        if topic == signReqTopic {
            handleSignRequest(conn, message: message)
        } else if topic == authReqTopic {
            handleAuthRequest(conn, message: message)
        } else if topic == statusTopic {
            handleStatus(conn, message: message)
        }
    }

    /// Puts this agent's presence back when the broker marks it offline while it is connected.
    ///
    /// readLoop gives up on a silent link after 60s and reconnects a few seconds later, under a
    /// new client id. The broker only drops the old link when the keep-alive runs out (90s, one
    /// and a half times the 60s in CONNECT) or the old socket's FIN finally gets through, and
    /// then publishes its Last-Will - a retained Online:false - over the Online:true this
    /// connection published. The server finds agents only by a retained Online:true, so after an
    /// outage of a minute or so the agent would stay invisible until its next reconnect, with the
    /// menu saying "connected".
    private func handleStatus(_ conn: NWConnectionWrapper, message: String) {
        guard let data = message.data(using: .utf8),
              let json = try? JSONSerialization.jsonObject(with: data) as? [String: Any] else { return }
        // Only an explicit offline: this connection's own Online:true comes back here too, and an
        // empty retained message is someone clearing the topic on purpose. "Online" is how the
        // Windows agent spells it, should one share this AgentId.
        let online = json["online"] as? Bool ?? json["Online"] as? Bool
        guard online == false else { return }
        print("[MQTT] The broker marked this agent offline (a stale Last-Will); publishing presence again")
        publishPresence(conn, online: true)
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
        // No fallback to config.tokenPin here: this handler answers requests arriving over the
        // network (MQTT), so the caller must prove it knows the PIN on every request. The cached
        // PIN is only used for the loopback-bound local HTTP path, which is already trusted.
        let pin = json["pin"] as? String ?? ""

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

    private func handleAuthRequest(_ conn: NWConnectionWrapper, message: String) {
        guard let data = message.data(using: .utf8),
              let json = try? JSONSerialization.jsonObject(with: data) as? [String: Any] else {
            print("[MQTT] Auth request not parseable")
            return
        }

        let correlationId = json["correlationId"] as? String ?? ""
        let phoneNumber = json["phoneNumber"] as? String ?? ""
        let pin = json["pin"] as? String ?? ""

        let response: [String: Any]

        guard let configPhone = config.endUserPhoneNumber, !configPhone.isEmpty,
              let configPin = config.tokenPin, !configPin.isEmpty else {
            response = [
                "correlationId": correlationId,
                "success": false,
                "error": "agent phone number or PIN is not configured",
            ]
            if let responseData = try? JSONSerialization.data(withJSONObject: response),
               let responseStr = String(data: responseData, encoding: .utf8) {
                publish(conn, topic: authResTopic, message: responseStr, qos: 1)
            }
            print("[MQTT] Auth response sent (correlationId=\(correlationId), success=false)")
            return
        }

        let phoneMatches = normalizePhone(phoneNumber) == normalizePhone(configPhone)
        let pinMatches = pin == configPin

        if phoneMatches && pinMatches {
            response = [
                "correlationId": correlationId,
                "success": true,
                "error": NSNull(),
            ]
        } else {
            response = [
                "correlationId": correlationId,
                "success": false,
                "error": "invalid phone number or PIN",
            ]
        }

        if let responseData = try? JSONSerialization.data(withJSONObject: response),
           let responseStr = String(data: responseData, encoding: .utf8) {
            publish(conn, topic: authResTopic, message: responseStr, qos: 1)
            let success = response["success"] as? Bool ?? false
            print("[MQTT] Auth response sent (correlationId=\(correlationId), success=\(success))")
        }
    }

    private func normalizePhone(_ phone: String) -> String {
        return phone.trimmingCharacters(in: .whitespacesAndNewlines)
            .replacingOccurrences(of: " ", with: "")
            .replacingOccurrences(of: "-", with: "")
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
            "service": "vmsign-agent",
            "agentId": agentId,
            "host": ProcessInfo.processInfo.hostName,
            "httpPort": httpPort,
            "online": online,
            "phoneNumber": config.endUserPhoneNumber ?? "",
            "certs": certs,
            "ts": ISO8601DateFormatter().string(from: Date()),
        ]
        guard let data = try? JSONSerialization.data(withJSONObject: payload),
              let str = String(data: data, encoding: .utf8) else { return "{}" }
        return str
    }

    private static func encodeString(_ s: String) -> [UInt8] {
        let bytes = Array(s.utf8)
        return [UInt8(bytes.count >> 8), UInt8(bytes.count & 0xFF)] + bytes
    }

    private static func encodeBytes(_ bytes: [UInt8]) -> [UInt8] {
        return [UInt8(bytes.count >> 8), UInt8(bytes.count & 0xFF)] + bytes
    }

    private static func encodeRemainingLength(_ length: Int) -> [UInt8] {
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
    private let useTls: Bool
    // Set by the first read that returns bytes. A hang-up before the broker has sent anything is
    // nearly always Use TLS not matching the port, and the reason then says which way to flip it.
    private var answered = false
    /// Bytes received that readPacket has not handed out yet: the start of the next packet, or
    /// more packets that arrived together. After a failed handshake, whatever answered instead
    /// of a broker.
    private(set) var pending: [UInt8] = []

    init(host: String, port: Int, useTls: Bool) throws {
        self.useTls = useTls

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
            let status = input.streamStatus
            let cause = input.streamError ?? output.streamError
            input.close()
            output.close()
            if status == .opening {
                throw Pkcs11Error.general("Connection timed out to \(host):\(port)")
            }
            // A refused port, an unknown host and a failed TLS handshake all end here well before
            // the deadline, and each has a different fix, so none of them is called a timeout.
            let reason = NWConnectionWrapper.reason(for: cause, useTls: useTls, answered: false)
            throw Pkcs11Error.general("Cannot connect to \(host):\(port): \(reason)")
        }

        self.inputStream = input
        self.outputStream = output
    }

    func send(data: [UInt8]) throws {
        guard let output = outputStream else { throw Pkcs11Error.general("Not connected") }
        // write() may take only part of the buffer, and returns -1 once the link has failed.
        // Ignoring that let a PINGREQ into a dead link pass as sent.
        var offset = 0
        while offset < data.count {
            let written = data.withUnsafeBufferPointer { ptr in
                output.write(ptr.baseAddress! + offset, maxLength: data.count - offset)
            }
            guard written > 0 else {
                let cause = written < 0 ? output.streamError : nil
                throw Pkcs11Error.general(NWConnectionWrapper.reason(for: cause, useTls: useTls, answered: answered))
            }
            offset += written
        }
    }

    /// Returns the next MQTT packet, whole, or [] when none arrived within `timeout`.
    ///
    /// One read returns whatever the socket holds: two packets that arrived close together, or
    /// the first part of a long one. Taking that as a single packet lost a PUBLISH that came
    /// right behind a PINGRESP, and turned two sign requests arriving together into one message
    /// that did not parse, so neither was answered; a PUBLISH cut short after its topic could
    /// crash the parser. Bytes past the packet wait in `pending`.
    func readPacket(timeout: TimeInterval = 60) throws -> [UInt8] {
        guard let input = inputStream else { throw Pkcs11Error.general("Not connected") }

        let deadline = Date().addingTimeInterval(timeout)
        while true {
            if let length = NWConnectionWrapper.packetLength(pending) {
                let packet = Array(pending.prefix(length))
                pending.removeFirst(length)
                return packet
            }

            while !input.hasBytesAvailable && Date() < deadline {
                try throwIfFailed(input)
                Thread.sleep(forTimeInterval: 0.05)
            }
            if !input.hasBytesAvailable {
                try throwIfFailed(input)
                return [] // Timeout
            }

            var buffer = [UInt8](repeating: 0, count: 65536)
            let bytesRead = input.read(&buffer, maxLength: buffer.count)
            guard bytesRead > 0 else {
                // 0: the broker closed the connection. -1: the link failed.
                let cause = bytesRead < 0 ? input.streamError : nil
                throw Pkcs11Error.general(NWConnectionWrapper.reason(for: cause, useTls: useTls, answered: answered))
            }
            answered = true
            pending += buffer.prefix(bytesRead)
        }
    }

    /// Length of the MQTT packet at the start of `bytes`, fixed header included, once all of it
    /// has arrived; nil until then.
    private static func packetLength(_ bytes: [UInt8]) -> Int? {
        // Remaining Length: 7 bits a byte, least significant first, at most four bytes
        // (MQTT 3.1.1 section 2.2.3).
        var remaining = 0
        var multiplier = 1
        var index = 1
        while true {
            guard index < bytes.count else { return nil }
            let byte = bytes[index]
            remaining += Int(byte & 0x7F) * multiplier
            index += 1
            if byte & 0x80 == 0 { break }
            // A fifth length byte: this is not MQTT. Hand over what came, for the caller to report.
            if index == 5 { return bytes.count }
            multiplier *= 128
        }
        let total = index + remaining
        return bytes.count >= total ? total : nil
    }

    func disconnect() {
        inputStream?.close()
        outputStream?.close()
        inputStream = nil
        outputStream = nil
    }

    /// A stream that failed or reached its end reports no bytes available, exactly like a quiet
    /// one. This check tells them apart, so a dead link fails now instead of waiting out the
    /// timeout and passing for an idle one.
    private func throwIfFailed(_ input: InputStream) throws {
        switch input.streamStatus {
        case .error:
            throw Pkcs11Error.general(NWConnectionWrapper.reason(for: input.streamError, useTls: useTls, answered: answered))
        case .atEnd, .closed:
            throw Pkcs11Error.general(NWConnectionWrapper.reason(for: nil, useTls: useTls, answered: answered))
        default:
            break
        }
    }

    /// Words a stream failure for the menu bar; `error` nil means the broker closed the connection.
    private static func reason(for error: Error?, useTls: Bool, answered: Bool) -> String {
        var text = "Connection closed by the broker"
        var hungUp = true
        if let cause = error {
            text = describe(cause)
            hungUp = isHangUp(cause)
        }
        if !hungUp || answered {
            return text
        }
        // Nothing has come back yet: a plain listener hangs up on a TLS ClientHello, and a TLS
        // listener on a plain CONNECT.
        if useTls {
            return "\(text) - if this port does not use TLS, turn off Use TLS"
        }
        return "\(text) before answering - if this port uses TLS, turn on Use TLS"
    }

    /// CFNetwork reports socket and TLS failures as bare codes ("OSStatus error -9806"), which
    /// tell the person reading the menu bar nothing.
    private static func describe(_ error: Error) -> String {
        let nsError = error as NSError
        if nsError.domain == NSPOSIXErrorDomain {
            // From macOS 15 an app needs the Local Network permission to reach the LAN, and a
            // connect it blocks fails with EHOSTUNREACH. "No route to host" alone sends people
            // off to check routing instead of the permission.
            if nsError.code == Int(EHOSTUNREACH) {
                return "No route to host - on macOS 15 or later, also check Privacy & Security > Local Network"
            }
            // "Connection refused", "Network is unreachable", "Connection reset by peer", ...
            return String(cString: strerror(Int32(truncatingIfNeeded: nsError.code)))
        }
        if nsError.domain == NSOSStatusErrorDomain {
            if isHangUp(error) {
                return "the broker closed the TLS connection"
            }
            // Secure Transport's codes run down from errSSLProtocol (-9800).
            let status = OSStatus(truncatingIfNeeded: nsError.code)
            if status <= -9800 && status > -9900 {
                let message = (SecCopyErrorMessageString(status, nil) as String?) ?? "OSStatus \(status)"
                return "TLS error: \(message)"
            }
        }
        // kCFHostErrorHostNotFound (1) and kCFHostErrorUnknown (2): the name did not resolve.
        let cfNetworkDomain = kCFErrorDomainCFNetwork as String
        if nsError.domain == cfNetworkDomain && (nsError.code == 1 || nsError.code == 2) {
            return "cannot resolve the host name"
        }
        return nsError.localizedDescription
    }

    /// The other end hung up: ECONNRESET or EPIPE, or Secure Transport's errSSLClosedGraceful
    /// (-9805), errSSLClosedAbort (-9806) and errSSLClosedNoNotify (-9816).
    private static func isHangUp(_ error: Error) -> Bool {
        let nsError = error as NSError
        if nsError.domain == NSPOSIXErrorDomain {
            return nsError.code == Int(ECONNRESET) || nsError.code == Int(EPIPE)
        }
        if nsError.domain == NSOSStatusErrorDomain {
            return nsError.code == -9805 || nsError.code == -9806 || nsError.code == -9816
        }
        return false
    }
}
