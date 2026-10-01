import Foundation
import Security

/// Test Sign: proves, one link at a time, that a document can be signed with this token the way
/// the hospital software signs it. Same four steps as the Windows agent's SignTest.cs:
///
///   1. Token   - sign a random digest locally and verify it against the certificate.
///   2. Server  - log in to the signing API (mid=USB).
///   3. MQTT    - credentials/list: the server finds this agent over MQTT and checks phone+PIN.
///   4. Sign    - sign/multi with a generated PDF; the server sends the hash back to this agent
///                over MQTT, embeds the signature and returns the signed file.
///
/// Each step stops the run on failure and says which link broke. Step 1 runs first on purpose:
/// a wrong PIN costs one of the token's few retries before it locks, and the server would spend
/// another one in step 4. Stopping at step 1 spends one.
///
/// Blocking, like the rest of this agent's I/O: call `run()` off the main thread.
final class SignTestRunner {
    static let signaturePath = "/api/v1/Signature"
    private static let merchant = "USB"
    private static let timeout: TimeInterval = 120

    // PKCS#11 return values (not in the bridge header).
    private static let ckrPinIncorrect: UInt = 0xA0
    private static let ckrPinLocked: UInt = 0xA4

    private let config: AppConfig
    private let report: (String) -> Void

    /// - Parameter log: receives each line of the report; called on the calling thread.
    init(config: AppConfig, log: @escaping (String) -> Void) {
        self.config = config
        self.report = log
    }

    /// Returns the signed PDF, or nil when a step failed.
    func run() -> URL? {
        do {
            let info = try testToken()

            let apiUrl = SignTestRunner.normalizeApiUrl(config.signApiBaseUrl)
            report("")
            report("2/4  Signing server  \(apiUrl ?? "(not set)")")
            guard let api = apiUrl else {
                throw SignTestError("no valid API URL in Settings",
                    hint: "Open Settings and fill in Signing Server > API URL, e.g. http://10.0.0.5:8081")
            }
            guard let broker = config.mqttBrokerHost, !broker.isEmpty else {
                throw SignTestError("MQTT is not configured in this agent",
                    hint: "The server reaches USB tokens only over MQTT. Fill in the MQTT section of Settings.")
            }
            let phone = (config.endUserPhoneNumber ?? "").trimmingCharacters(in: .whitespacesAndNewlines)
            guard !phone.isEmpty else {
                throw SignTestError("no phone number in Settings",
                    hint: "The server finds this agent by its phone number. Fill in Phone Number in Settings.")
            }

            let identity: [String: Any] = [
                "user_name": phone,
                "password": config.tokenPin ?? "",
                "ip": SignTestRunner.localIp(),
                "mid": SignTestRunner.merchant,
            ]
            _ = try post(api, "/login", identity)
            report("     OK   login accepted")

            report("")
            report("3/4  Server -> MQTT broker -> this agent")
            let credentialId = try findCredential(api, identity, serial: info.serial)

            report("")
            report("4/4  Sign a test PDF")
            return try signPdf(api, identity, credentialId: credentialId, phone: phone, info: info)
        } catch let error as SignTestError {
            report("     FAIL \(error.message)")
            if let hint = error.hint {
                report("     ->   \(hint)")
            }
            return nil
        } catch {
            report("     FAIL \(error.localizedDescription)")
            return nil
        }
    }

    // MARK: - Step 1

    private func testToken() throws -> CertInfo {
        report("1/4  USB token")
        guard let pin = config.tokenPin, !pin.isEmpty else {
            throw SignTestError("no PIN configured",
                hint: "This agent reads the PIN from the TOKEN__PIN environment variable, and the server "
                    + "always sends one with a USB sign request.")
        }

        let certs: [(Data, CertInfo)]
        do {
            certs = try Pkcs11.listRawCerts(modulePath: config.pkcs11Module)
        } catch {
            throw SignTestError("cannot read the token: \(error.localizedDescription)",
                hint: "Plug in the token and check its PKCS#11 driver (\(config.pkcs11Module)).")
        }
        // The token also carries its CA chain; those certificates have no key to sign with.
        guard let (certData, info) = certs.first(where: { !SignTestRunner.isCertificateAuthority($0.0) }) else {
            throw SignTestError("no signing certificate on the token",
                hint: "Plug in the token and check its PKCS#11 driver (\(config.pkcs11Module)).")
        }
        report("     Certificate: \(info.subjectDN)  serial \(info.serial)")

        var digest = Data(count: 32)
        let randomStatus = digest.withUnsafeMutableBytes { buffer in
            SecRandomCopyBytes(kSecRandomDefault, 32, buffer.baseAddress!)
        }
        guard randomStatus == errSecSuccess else {
            throw SignTestError("could not generate a random test digest", hint: nil)
        }

        let started = Date()
        let result: SignResult
        do {
            result = try Pkcs11.signDigest(certData: certData, digest: digest, pin: pin, modulePath: config.pkcs11Module)
        } catch Pkcs11Error.loginFailed(let rv) where rv == SignTestRunner.ckrPinIncorrect {
            throw SignTestError("the token rejected the PIN",
                hint: "The token locks after a few wrong PINs. Correct TOKEN__PIN before trying again.")
        } catch Pkcs11Error.loginFailed(let rv) where rv == SignTestRunner.ckrPinLocked {
            throw SignTestError("the token PIN is locked",
                hint: "Unlock it with the token vendor's tool (PUK), or ask the CA.")
        } catch {
            throw SignTestError("signing on the token failed: \(error.localizedDescription)",
                hint: "Check the token is plugged in and its driver is installed.")
        }

        guard SignTestRunner.verify(certData: certData, digest: digest, result: result) else {
            throw SignTestError("the token signed, but the signature does not match the certificate",
                hint: "The token holds a different key from this certificate.")
        }
        let elapsed = String(format: "%.1f", Date().timeIntervalSince(started))
        report("     OK   signed and verified a test digest (\(result.algorithm), \(elapsed)s)")
        return info
    }

    private static func verify(certData: Data, digest: Data, result: SignResult) -> Bool {
        guard let cert = SecCertificateCreateWithData(nil, certData as CFData),
              let key = SecCertificateCopyKey(cert) else { return false }
        // Pkcs11.signDigest returns RSA PKCS#1 v1.5 over DigestInfo(SHA-256), or DER ECDSA.
        let algorithm: SecKeyAlgorithm = result.algorithm == "ECDSA"
            ? .ecdsaSignatureDigestX962SHA256
            : .rsaSignatureDigestPKCS1v15SHA256
        var error: Unmanaged<CFError>?
        return SecKeyVerifySignature(key, algorithm, digest as CFData, result.signature as CFData, &error)
    }

    // MARK: - Steps 3 and 4

    private func findCredential(_ api: String, _ identity: [String: Any], serial: String) throws -> String {
        let json = try post(api, "/credentials/list", identity)
        let creds = json["result"] as? [[String: Any]] ?? []
        report("     OK   the server found this agent: \(creds.count) certificate(s)")

        let match = creds.first { cred in
            let credSerial = cred["serial_number"] as? String ?? ""
            return credSerial.caseInsensitiveCompare(serial) == .orderedSame
        }
        guard let mine = match else {
            throw SignTestError("the server's list does not contain certificate \(serial)",
                hint: "Another agent may be publishing the same phone number, or this agent has not been "
                    + "restarted since the token was changed. Restart the agent and try again.")
        }
        return mine["credential_id"] as? String ?? serial
    }

    private func signPdf(_ api: String, _ identity: [String: Any], credentialId: String, phone: String,
                         info: CertInfo) throws -> URL {
        let now = Date()
        let testId = "VMSIGN-TEST-\(SignTestRunner.format(now, "yyyyMMddHHmmss"))-"
            + UUID().uuidString.prefix(6).lowercased()
        let computer = Host.current().localizedName ?? ProcessInfo.processInfo.hostName
        let pdf = SamplePdf.build(title: "VMSignAgent - Test signature", lines: [
            "This page was generated by VMSignAgent to test USB token signing.",
            "Computer:    \(computer)",
            "Phone:       \(phone)",
            "Certificate: \(info.subjectDN)",
            "Serial:      \(info.serial)",
            "Created:     \(SignTestRunner.format(now, "yyyy-MM-dd HH:mm:ss"))",
            "Test ID:     \(testId)",
            "",
            "The signature box below was added by the signing server.",
        ])

        var request = identity
        request["trans_id"] = testId
        request["credential_id"] = credentialId
        request["computer_name"] = computer
        request["mac"] = ""
        request["os"] = "macOS"
        request["data_type"] = 1 // BASE64: the signed file comes back in raw_data
        request["local_sign"] = false
        // No signature_id: it names a signature record in the HIS, and with one the server goes on
        // to mark that record signed - which fails for a test, and fails the whole file with it.
        let file: [String: Any] = [
            "page_sign": 1,
            "file_name": "\(testId).pdf",
            "signature_name": "vmsign_test",
            "store_uid": testId,
            "store_data": false,
            "display_name_mode": 2,
            // Drawn by the server with its own Unicode font, so the diacritics stay.
            "name_signer": info.subjectDN,
            "title_signer": "VMSignAgent test",
            "is_show_signature_time": true,
            // The server takes point_x/point_y as the CENTRE of the box: this is [72,420]-[312,510].
            "point_x": 192,
            "point_y": 465,
            "width": 240,
            "height": 90,
            "pdf_data": pdf.base64EncodedString(),
        ]
        request["file_datas"] = [file]

        let started = Date()
        let json = try post(api, "/sign/multi", request)

        guard let result = (json["result"] as? [[String: Any]])?.first else {
            throw SignTestError("the server answered without a file result", hint: nil)
        }
        // SignatureStatus.Done = 2. Also accept the name, for a server that serialises enums by name.
        let statusNumber = result["status"] as? Int
        let statusText = result["status"] as? String ?? ""
        let message = result["message"] as? String
        let done = statusNumber == 2 || statusText == "2"
            || statusText.caseInsensitiveCompare("Done") == .orderedSame
        guard done else {
            throw SignTestError("the server did not sign the file: \(message ?? "")",
                hint: SignTestRunner.hint(for: message))
        }
        guard let raw = result["raw_data"] as? String, !raw.isEmpty,
              let signed = Data(base64Encoded: raw) else {
            throw SignTestError("the server reported success but returned no file", hint: nil)
        }

        // Application Support rather than Documents: a non-sandboxed app writing to ~/Documents
        // triggers a privacy prompt, and this file is only kept to be opened right away.
        let folder = FileManager.default.urls(for: .applicationSupportDirectory, in: .userDomainMask)[0]
            .appendingPathComponent("VMSignAgent/test-signed", isDirectory: true)
        try FileManager.default.createDirectory(at: folder, withIntermediateDirectories: true)
        let url = folder.appendingPathComponent("test-signed-\(SignTestRunner.format(now, "yyyyMMdd-HHmmss")).pdf")
        try signed.write(to: url)

        let elapsed = String(format: "%.1f", Date().timeIntervalSince(started))
        report("     OK   signed by the server in \(elapsed)s")
        report("     Saved: \(url.path)")
        report("")
        report("All steps passed. USB signing works end to end on this Mac.")
        return url
    }

    // MARK: - HTTP

    private func post(_ api: String, _ action: String, _ body: [String: Any]) throws -> [String: Any] {
        let address = api + SignTestRunner.signaturePath + action
        guard let url = URL(string: address) else {
            throw SignTestError("not a valid URL: \(address)", hint: nil)
        }
        var request = URLRequest(url: url, cachePolicy: .reloadIgnoringLocalCacheData,
                                 timeoutInterval: SignTestRunner.timeout)
        request.httpMethod = "POST"
        request.setValue("application/json", forHTTPHeaderField: "Content-Type")
        request.httpBody = try JSONSerialization.data(withJSONObject: body)

        let reply = HttpReply()
        let finished = DispatchSemaphore(value: 0)
        URLSession.shared.dataTask(with: request) { data, response, error in
            reply.data = data
            reply.response = response
            reply.error = error
            finished.signal()
        }.resume()
        finished.wait()

        if let error = reply.error {
            let code = (error as? URLError)?.code
            if code == .timedOut {
                throw SignTestError("no answer from \(address) within \(Int(SignTestRunner.timeout))s",
                    hint: "The server may be waiting on the MQTT broker or on this agent. Check the agent's MQTT connection.")
            }
            // App Transport Security refuses plain http through URLSession unless the app's
            // Info.plist allows it, as the release builds' Info.plist does. Reported as "cannot
            // reach" below, this would send people to check a URL that is fine.
            if code == .appTransportSecurityRequiresSecureConnection {
                throw SignTestError("macOS refused plain http to \(address) (App Transport Security)",
                    hint: "This copy of the agent has no Info.plist entry allowing http (NSAllowsArbitraryLoads) - "
                        + "a binary run straight from .build has none. Install the release .pkg, or use an https:// API URL.")
            }
            throw SignTestError("cannot reach \(address): \(error.localizedDescription)",
                hint: "Check Settings > Signing Server > API URL and that this Mac can reach the server.")
        }

        let status = (reply.response as? HTTPURLResponse)?.statusCode ?? 0
        guard let data = reply.data,
              let json = (try? JSONSerialization.jsonObject(with: data)) as? [String: Any] else {
            throw SignTestError("\(address) answered HTTP \(status), but not with the signing API's JSON",
                hint: "Check Settings > Signing Server > API URL points at the signing API.")
        }

        if (json["success"] as? Bool) != true {
            var message = json["message"] as? String ?? ""
            // sign/multi: the envelope only counts the failed files; each file says why.
            let fileMessage = (json["result"] as? [[String: Any]])?
                .compactMap { $0["message"] as? String }
                .first(where: { !$0.isEmpty })
            if let fileMessage = fileMessage, fileMessage != message {
                message = message.isEmpty ? fileMessage : "\(message) \(fileMessage)"
            }
            throw SignTestError(message.isEmpty ? "the server refused the request (HTTP \(status))" : "server: \(message)",
                hint: SignTestRunner.hint(for: message))
        }
        return json
    }

    /// Turns the server's USB error messages into the thing to go and fix.
    private static func hint(for message: String?) -> String? {
        guard let message = message, !message.isEmpty else { return nil }
        func has(_ text: String) -> Bool { message.range(of: text, options: .caseInsensitive) != nil }

        if has("Error while connecting with host") || has("Connection refused") {
            return "The SERVER cannot connect to its MQTT broker. On the server, check the broker is running and "
                + "USB_MQTT_BROKER_HOST/PORT in .env (then recreate the API container)."
        }
        if has("BadUserNameOrPassword") || has("NotAuthorized") {
            return "The broker refused the SERVER's MQTT username/password (USB_MQTT_USERNAME/PASSWORD on the server)."
        }
        if has("nào đang online") {
            return "The server reached the broker but sees no agent at all. This agent and the server must use the "
                + "same broker host and port."
        }
        if has("cho số điện thoại này") {
            return "The server sees agents, but none with this phone number. The phone number in Settings must be "
                + "the one typed when signing; restart the agent after changing it."
        }
        if has("PIN USB Token không hợp lệ") {
            return "This agent rejected the phone/PIN. If Settings or TOKEN__PIN changed, restart the agent."
        }
        if has("Không tìm thấy thông tin CTS") {
            return "The server's USB merchant is not in MQTT mode (USB_MQTT_BROKER_HOST is empty on the server)."
        }
        if has("Timeout") || has("timed out") {
            return "The server waited for this agent and gave up. Check the agent's MQTT connection and that the "
                + "token is plugged in."
        }
        return nil
    }

    // MARK: - Helpers

    /// Accepts the bare server ("http://host:2606") or the URL the hospital software is configured
    /// with ("http://host:2606/api/v1/Signature"), and returns the bare server.
    static func normalizeApiUrl(_ value: String?) -> String? {
        var url = (value ?? "").trimmingCharacters(in: .whitespacesAndNewlines)
        while url.hasSuffix("/") { url.removeLast() }
        if url.lowercased().hasSuffix(signaturePath.lowercased()) {
            url = String(url.dropLast(signaturePath.count))
        }
        guard !url.isEmpty,
              let parsed = URL(string: url),
              let scheme = parsed.scheme?.lowercased(),
              scheme == "http" || scheme == "https",
              parsed.host != nil else { return nil }
        return url
    }

    /// True for a certificate whose Basic Constraints extension says cA=TRUE.
    static func isCertificateAuthority(_ der: Data) -> Bool {
        let bytes = [UInt8](der)
        let oid: [UInt8] = [0x06, 0x03, 0x55, 0x1D, 0x13] // id-ce-basicConstraints
        guard bytes.count > oid.count else { return false }
        for i in 0...(bytes.count - oid.count) where Array(bytes[i..<(i + oid.count)]) == oid {
            var p = i + oid.count
            if p + 2 < bytes.count, bytes[p] == 0x01, bytes[p + 1] == 0x01 { p += 3 } // critical flag
            guard p + 1 < bytes.count, bytes[p] == 0x04 else { return false }        // extnValue OCTET STRING
            p += 2                                                                    // short length: tiny extension
            guard p + 1 < bytes.count, bytes[p] == 0x30 else { return false }        // BasicConstraints SEQUENCE
            let length = Int(bytes[p + 1])
            p += 2
            return length >= 3 && p + 2 < bytes.count
                && bytes[p] == 0x01 && bytes[p + 1] == 0x01 && bytes[p + 2] != 0x00 // cA BOOLEAN TRUE
        }
        return false
    }

    private static func localIp() -> String {
        Host.current().addresses.first(where: { $0.contains(".") && !$0.hasPrefix("127.") }) ?? "127.0.0.1"
    }

    private static func format(_ date: Date, _ pattern: String) -> String {
        let formatter = DateFormatter()
        formatter.locale = Locale(identifier: "en_US_POSIX")
        formatter.dateFormat = pattern
        return formatter.string(from: date)
    }
}

struct SignTestError: Error {
    let message: String
    let hint: String?

    init(_ message: String, hint: String?) {
        self.message = message
        self.hint = hint
    }
}

/// Carries a URLSession reply back to the thread blocked on the semaphore in `post`.
private final class HttpReply: @unchecked Sendable {
    var data: Data?
    var response: URLResponse?
    var error: Error?
}
