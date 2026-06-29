import Foundation

// ── Minimal HTTP Server using GCDAsyncSocket-style raw sockets ───────────────
// Uses Python-style socket server for simplicity. For production consider NIO.
// ─────────────────────────────────────────────────────────────────────────────

class HttpServer {
    let port: Int
    let config: AppConfig
    weak var delegate: AppDelegate?
    private var listenSocket: Int32 = -1
    private var running = false

    init(port: Int, config: AppConfig, delegate: AppDelegate) {
        self.port = port
        self.config = config
        self.delegate = delegate
    }

    func start() {
        DispatchQueue.global(qos: .default).async { [weak self] in
            self?.runLoop()
        }
    }

    private func runLoop() {
        listenSocket = socket(AF_INET, SOCK_STREAM, 0)
        guard listenSocket >= 0 else { print("[USB Agent] socket() failed"); return }

        var opt: Int32 = 1
        setsockopt(listenSocket, SOL_SOCKET, SO_REUSEADDR, &opt, socklen_t(MemoryLayout<Int32>.size))

        var addr = sockaddr_in()
        addr.sin_family = sa_family_t(AF_INET)
        addr.sin_port = UInt16(port).bigEndian
        addr.sin_addr.s_addr = inet_addr("127.0.0.1")

        let bindResult = withUnsafePointer(to: &addr) { ptr in
            ptr.withMemoryRebound(to: sockaddr.self, capacity: 1) { sockPtr in
                bind(listenSocket, sockPtr, socklen_t(MemoryLayout<sockaddr_in>.size))
            }
        }
        guard bindResult == 0 else {
            print("[USB Agent] bind() failed on port \(port): \(String(cString: strerror(errno)))")
            return
        }

        listen(listenSocket, 10)
        running = true

        while running {
            let client = accept(listenSocket, nil, nil)
            guard client >= 0 else { break }
            DispatchQueue.global(qos: .userInitiated).async { [weak self] in
                self?.handleClient(client)
            }
        }
    }

    private func handleClient(_ client: Int32) {
        defer { close(client) }

        // Read request (up to 64KB)
        var buffer = [UInt8](repeating: 0, count: 65536)
        let bytesRead = read(client, &buffer, buffer.count)
        guard bytesRead > 0 else { return }

        let requestStr = String(bytes: buffer.prefix(bytesRead), encoding: .utf8) ?? ""
        let (method, path, body) = parseHttpRequest(requestStr)

        guard method == "POST" else {
            sendResponse(client, status: 404, json: ["Success": false, "Error": "Not found"])
            return
        }

        delegate?.recordActivity()

        let result: [String: Any]
        switch path.lowercased().trimmingCharacters(in: CharacterSet(charactersIn: "/")) {
        case "certs":
            result = handleCerts()
        case "login":
            result = handleLogin(body: body)
        case "signhash":
            result = handleSignHash(body: body)
        default:
            result = ["Success": false, "Error": "Not found"]
        }

        sendResponse(client, status: 200, json: result)
    }

    // MARK: - Handlers

    private func handleCerts() -> [String: Any] {
        do {
            let certs = try Pkcs11.listCerts(modulePath: config.pkcs11Module)
            let certsArray = certs.map { cert -> [String: Any] in
                ["Serial": cert.serial, "SubjectDN": cert.subjectDN, "IssuerDN": cert.issuerDN,
                 "ValidFrom": cert.validFrom, "ValidTo": cert.validTo, "Thumbprint": cert.thumbprint,
                 "Certificate": cert.certificate, "Algorithm": cert.algorithm, "Success": true]
            }
            return ["Success": true, "Certs": certsArray]
        } catch {
            return ["Success": false, "Error": error.localizedDescription]
        }
    }

    private func handleLogin(body: String) -> [String: Any] {
        guard let params = parseJsonBody(body) else {
            return ["Success": false, "Error": "Invalid JSON"]
        }
        let serial = params["Serial"] as? String ?? params["serial"] as? String
        let userName = params["UserName"] as? String ?? params["userName"] as? String

        do {
            guard let (_, certInfo) = try Pkcs11.findCert(serial: serial, userName: userName, modulePath: config.pkcs11Module) else {
                return ["Success": false, "Error": "Certificate not found on PKCS#11 token"]
            }
            return ["Success": true, "Serial": certInfo.serial, "SubjectDN": certInfo.subjectDN,
                    "IssuerDN": certInfo.issuerDN, "ValidFrom": certInfo.validFrom, "ValidTo": certInfo.validTo,
                    "Thumbprint": certInfo.thumbprint, "Certificate": certInfo.certificate, "Algorithm": certInfo.algorithm]
        } catch {
            return ["Success": false, "Error": error.localizedDescription]
        }
    }

    private func handleSignHash(body: String) -> [String: Any] {
        guard let params = parseJsonBody(body) else {
            return ["Success": false, "Error": "Invalid JSON"]
        }

        guard let hashBase64 = params["HashBase64"] as? String ?? params["hashBase64"] as? String,
              !hashBase64.isEmpty else {
            return ["Success": false, "Error": "hashBase64 is required"]
        }
        guard let serial = params["Serial"] as? String ?? params["serial"] as? String,
              !serial.isEmpty else {
            return ["Success": false, "Error": "serial is required"]
        }
        guard let digest = Data(base64Encoded: hashBase64) else {
            return ["Success": false, "Error": "hashBase64 is not valid base64"]
        }
        guard digest.count == 32 else {
            return ["Success": false, "Error": "hashBase64 must be a 32-byte SHA-256 digest"]
        }

        let pin = (params["Pin"] as? String ?? params["pin"] as? String ?? config.tokenPin) ?? ""
        guard !pin.isEmpty else {
            return ["Success": false, "Error": "PIN is required for PKCS#11 signing"]
        }

        do {
            guard let (certData, _) = try Pkcs11.findCert(serial: serial, userName: nil, modulePath: config.pkcs11Module) else {
                return ["Success": false, "Error": "Certificate not found on PKCS#11 token"]
            }
            let result = try Pkcs11.signDigest(certData: certData, digest: digest, pin: pin, modulePath: config.pkcs11Module)
            return ["Success": true,
                    "SignatureBase64": result.signature.base64EncodedString(),
                    "CertificateBase64": result.certRawData.base64EncodedString(),
                    "Algorithm": result.algorithm]
        } catch {
            return ["Success": false, "Error": "sign failed: \(error.localizedDescription)"]
        }
    }

    // MARK: - Helpers

    private func parseHttpRequest(_ request: String) -> (String, String, String) {
        let lines = request.components(separatedBy: "\r\n")
        guard let firstLine = lines.first else { return ("", "", "") }
        let parts = firstLine.split(separator: " ", maxSplits: 2)
        let method = parts.count > 0 ? String(parts[0]) : ""
        let path = parts.count > 1 ? String(parts[1]) : ""

        // Body is after empty line
        if let emptyIdx = lines.firstIndex(of: "") {
            let body = lines.suffix(from: lines.index(after: emptyIdx)).joined(separator: "\r\n")
            return (method, path, body)
        }
        return (method, path, "")
    }

    private func parseJsonBody(_ body: String) -> [String: Any]? {
        guard let data = body.data(using: .utf8),
              let json = try? JSONSerialization.jsonObject(with: data) as? [String: Any] else { return nil }
        return json
    }

    private func sendResponse(_ client: Int32, status: Int, json: [String: Any]) {
        guard let data = try? JSONSerialization.data(withJSONObject: json),
              let jsonStr = String(data: data, encoding: .utf8) else { return }

        let response = "HTTP/1.1 \(status) OK\r\nContent-Type: application/json\r\nContent-Length: \(data.count)\r\nConnection: close\r\n\r\n\(jsonStr)"
        _ = response.withCString { ptr in
            write(client, ptr, strlen(ptr))
        }
    }
}
