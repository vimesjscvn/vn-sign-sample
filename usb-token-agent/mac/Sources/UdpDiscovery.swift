import Foundation

class UdpDiscovery {
    let port: Int
    let httpPort: Int
    let config: AppConfig
    private var socket: Int32 = -1

    init(port: Int, httpPort: Int, config: AppConfig) {
        self.port = port
        self.httpPort = httpPort
        self.config = config
    }

    func start() {
        DispatchQueue.global(qos: .utility).async { [weak self] in
            self?.runLoop()
        }
    }

    private func runLoop() {
        socket = Darwin.socket(AF_INET, SOCK_DGRAM, 0)
        guard socket >= 0 else { print("[USB Agent] UDP socket() failed"); return }

        var opt: Int32 = 1
        setsockopt(socket, SOL_SOCKET, SO_REUSEADDR, &opt, socklen_t(MemoryLayout<Int32>.size))
        setsockopt(socket, SOL_SOCKET, SO_BROADCAST, &opt, socklen_t(MemoryLayout<Int32>.size))

        var addr = sockaddr_in()
        addr.sin_family = sa_family_t(AF_INET)
        addr.sin_port = UInt16(port).bigEndian
        addr.sin_addr.s_addr = INADDR_ANY

        let bindResult = withUnsafePointer(to: &addr) { ptr in
            ptr.withMemoryRebound(to: sockaddr.self, capacity: 1) { sockPtr in
                bind(socket, sockPtr, socklen_t(MemoryLayout<sockaddr_in>.size))
            }
        }
        guard bindResult == 0 else {
            print("[USB Agent] UDP bind failed on port \(port): \(String(cString: strerror(errno)))")
            return
        }

        var buffer = [UInt8](repeating: 0, count: 4096)
        var clientAddr = sockaddr_in()
        var clientAddrLen = socklen_t(MemoryLayout<sockaddr_in>.size)

        while true {
            let n = withUnsafeMutablePointer(to: &clientAddr) { ptr in
                ptr.withMemoryRebound(to: sockaddr.self, capacity: 1) { sockPtr in
                    recvfrom(socket, &buffer, buffer.count, 0, sockPtr, &clientAddrLen)
                }
            }
            guard n > 0 else { break }

            let msg = String(bytes: buffer.prefix(n), encoding: .utf8)?.trimmingCharacters(in: .whitespacesAndNewlines) ?? ""
            guard msg.hasPrefix("VIMES-USB-DISCOVER") else { continue }

            let reply = buildDiscoveryReply()
            guard let replyData = try? JSONSerialization.data(withJSONObject: reply) else { continue }

            replyData.withUnsafeBytes { ptr in
                withUnsafeMutablePointer(to: &clientAddr) { addrPtr in
                    addrPtr.withMemoryRebound(to: sockaddr.self, capacity: 1) { sockPtr in
                        sendto(socket, ptr.baseAddress, replyData.count, 0, sockPtr, clientAddrLen)
                    }
                }
            }
        }
    }

    private func buildDiscoveryReply() -> [String: Any] {
        var certs: [[String: String]] = []
        if let certList = try? Pkcs11.listCerts(modulePath: config.pkcs11Module) {
            certs = certList.map { ["Serial": $0.serial, "Subject": $0.subjectDN, "Algorithm": $0.algorithm] }
        }
        return [
            "Service": "vimes-usb-agent",
            "Host": ProcessInfo.processInfo.hostName,
            "HttpPort": httpPort,
            "Certs": certs,
        ] as [String: Any]
    }
}
