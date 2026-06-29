import Foundation
import Security
import CommonCrypto
import CPkcs11

// ── PKCS#11 Swift wrapper using C bridge ─────────────────────────────────────

struct CertInfo: Encodable {
    let serial: String
    let subjectDN: String
    let issuerDN: String
    let validFrom: String
    let validTo: String
    let thumbprint: String
    let certificate: String
    let algorithm: String
    let success: Bool = true
}

struct SignResult {
    let signature: Data
    let certRawData: Data
    let algorithm: String
}

enum Pkcs11Error: LocalizedError {
    case moduleNotFound(String)
    case noToken
    case noPrivateKey
    case loginFailed(UInt)
    case signFailed(UInt)
    case general(String)

    var errorDescription: String? {
        switch self {
        case .moduleNotFound(let p): return "PKCS#11 module not found: \(p)"
        case .noToken: return "No PKCS#11 token present"
        case .noPrivateKey: return "No private key found for certificate"
        case .loginFailed(let rv): return "PKCS#11 login failed: 0x\(String(rv, radix: 16))"
        case .signFailed(let rv): return "PKCS#11 sign failed: 0x\(String(rv, radix: 16))"
        case .general(let m): return m
        }
    }
}

class Pkcs11 {
    private static let sha256DigestInfoPrefix: [UInt8] = [
        0x30, 0x31, 0x30, 0x0d, 0x06, 0x09, 0x60, 0x86, 0x48, 0x01,
        0x65, 0x03, 0x04, 0x02, 0x01, 0x05, 0x00, 0x04, 0x20
    ]

    // MARK: - Public API

    static func listCerts(modulePath: String) throws -> [CertInfo] {
        try enumerateRawCerts(modulePath: modulePath).map { $0.1 }
    }

    static func findCert(serial: String?, userName: String?, modulePath: String) throws -> (Data, CertInfo)? {
        let certs = try enumerateRawCerts(modulePath: modulePath)

        if let serial = serial, !serial.isEmpty {
            let normalized = serial.replacingOccurrences(of: " ", with: "")
                .replacingOccurrences(of: ":", with: "").uppercased()
            if let found = certs.first(where: { $0.1.serial.uppercased() == normalized }) {
                return found
            }
        }

        if let userName = userName, !userName.isEmpty {
            let lower = userName.lowercased()
            if let found = certs.first(where: {
                $0.1.subjectDN.lowercased().contains("cn=\(lower)") ||
                $0.1.subjectDN.lowercased().contains("e=\(lower)")
            }) {
                return found
            }
        }

        return certs.first
    }

    static func signDigest(certData: Data, digest: Data, pin: String, modulePath: String) throws -> SignResult {
        guard digest.count == 32 else {
            throw Pkcs11Error.general("Expected 32-byte SHA-256 digest")
        }

        let resolved = resolveModulePath(modulePath)
        guard let lib = pkcs11_load(resolved) else {
            throw Pkcs11Error.moduleNotFound(resolved)
        }
        defer { pkcs11_free(lib) }

        var rv = lib.pointee.C_Initialize(nil)
        guard rv == CKR_OK else { throw Pkcs11Error.general("C_Initialize failed") }

        let slots = getSlots(lib: lib)
        guard !slots.isEmpty else { throw Pkcs11Error.noToken }

        for slot in slots {
            var session: CK_SESSION_HANDLE = CK_INVALID_HANDLE
            rv = lib.pointee.C_OpenSession(slot, CK_FLAGS(CKF_SERIAL_SESSION), nil, nil, &session)
            guard rv == CKR_OK else { continue }
            defer { lib.pointee.C_CloseSession(session) }

            // Login
            var pinBytes = Array(pin.utf8)
            rv = lib.pointee.C_Login(session, CK_USER_TYPE(CKU_USER), &pinBytes, CK_ULONG(pinBytes.count))
            guard rv == CKR_OK || rv == CK_RV(CKR_USER_ALREADY_LOGGED_IN) else {
                throw Pkcs11Error.loginFailed(UInt(rv))
            }
            defer { lib.pointee.C_Logout(session) }

            // Find private key
            guard let keyHandle = findPrivateKey(lib: lib, session: session, certData: certData) else { continue }

            // Get key type
            let keyType = getKeyType(lib: lib, session: session, key: keyHandle)

            if keyType == CK_KEY_TYPE(CKK_EC) {
                let sig = try sign(lib: lib, session: session, key: keyHandle, mechanism: CK_MECHANISM_TYPE(CKM_ECDSA), data: digest)
                return SignResult(signature: ecdsaP1363ToDer(sig), certRawData: certData, algorithm: "ECDSA")
            } else {
                let digestInfo = Data(sha256DigestInfoPrefix) + digest
                let sig = try sign(lib: lib, session: session, key: keyHandle, mechanism: CK_MECHANISM_TYPE(CKM_RSA_PKCS), data: digestInfo)
                return SignResult(signature: sig, certRawData: certData, algorithm: "RSA")
            }
        }

        throw Pkcs11Error.noPrivateKey
    }

    // MARK: - Internal

    private static func enumerateRawCerts(modulePath: String) throws -> [(Data, CertInfo)] {
        let resolved = resolveModulePath(modulePath)
        guard let lib = pkcs11_load(resolved) else {
            throw Pkcs11Error.moduleNotFound(resolved)
        }
        defer { pkcs11_free(lib) }

        var rv = lib.pointee.C_Initialize(nil)
        guard rv == CKR_OK else { throw Pkcs11Error.general("C_Initialize failed: 0x\(String(rv, radix: 16))") }

        let slots = getSlots(lib: lib)
        var results: [(Data, CertInfo)] = []

        for slot in slots {
            var session: CK_SESSION_HANDLE = CK_INVALID_HANDLE
            rv = lib.pointee.C_OpenSession(slot, CK_FLAGS(CKF_SERIAL_SESSION), nil, nil, &session)
            guard rv == CKR_OK else { continue }
            defer { lib.pointee.C_CloseSession(session) }

            let certHandles = findObjects(lib: lib, session: session, objectClass: CK_OBJECT_CLASS(CKO_CERTIFICATE))
            for ch in certHandles {
                guard let rawData = getAttributeData(lib: lib, session: session, obj: ch, attrType: CK_ATTRIBUTE_TYPE(CKA_VALUE)),
                      !rawData.isEmpty else { continue }
                if let info = parseCertificate(rawData) {
                    results.append((rawData, info))
                }
            }
        }
        return results
    }

    private static func getSlots(lib: UnsafeMutablePointer<Pkcs11Lib>) -> [CK_SLOT_ID] {
        var count: CK_ULONG = 0
        var rv = lib.pointee.C_GetSlotList(1, nil, &count)
        guard rv == CKR_OK, count > 0 else { return [] }
        var slots = [CK_SLOT_ID](repeating: 0, count: Int(count))
        rv = lib.pointee.C_GetSlotList(1, &slots, &count)
        guard rv == CKR_OK else { return [] }
        return Array(slots.prefix(Int(count)))
    }

    private static func findObjects(lib: UnsafeMutablePointer<Pkcs11Lib>, session: CK_SESSION_HANDLE, objectClass: CK_OBJECT_CLASS) -> [CK_OBJECT_HANDLE] {
        var classVal = objectClass
        var template = CK_ATTRIBUTE(type: CK_ATTRIBUTE_TYPE(CKA_CLASS), pValue: &classVal, ulValueLen: CK_ULONG(MemoryLayout<CK_OBJECT_CLASS>.size))
        var rv = lib.pointee.C_FindObjectsInit(session, &template, 1)
        guard rv == CKR_OK else { return [] }
        defer { _ = lib.pointee.C_FindObjectsFinal(session) }

        var results: [CK_OBJECT_HANDLE] = []
        while true {
            var obj: CK_OBJECT_HANDLE = 0
            var count: CK_ULONG = 0
            rv = lib.pointee.C_FindObjects(session, &obj, 1, &count)
            guard rv == CKR_OK, count > 0 else { break }
            results.append(obj)
        }
        return results
    }

    private static func getAttributeData(lib: UnsafeMutablePointer<Pkcs11Lib>, session: CK_SESSION_HANDLE, obj: CK_OBJECT_HANDLE, attrType: CK_ATTRIBUTE_TYPE) -> Data? {
        var template = CK_ATTRIBUTE(type: attrType, pValue: nil, ulValueLen: 0)
        var rv = lib.pointee.C_GetAttributeValue(session, obj, &template, 1)
        guard rv == CKR_OK, template.ulValueLen > 0 else { return nil }

        let size = Int(template.ulValueLen)
        let buffer = UnsafeMutablePointer<UInt8>.allocate(capacity: size)
        defer { buffer.deallocate() }
        template.pValue = UnsafeMutableRawPointer(buffer)
        rv = lib.pointee.C_GetAttributeValue(session, obj, &template, 1)
        guard rv == CKR_OK else { return nil }
        return Data(bytes: buffer, count: size)
    }

    private static func findPrivateKey(lib: UnsafeMutablePointer<Pkcs11Lib>, session: CK_SESSION_HANDLE, certData: Data) -> CK_OBJECT_HANDLE? {
        // Find cert object matching raw data to get CKA_ID
        let certHandles = findObjects(lib: lib, session: session, objectClass: CK_OBJECT_CLASS(CKO_CERTIFICATE))
        var certId: Data?

        for ch in certHandles {
            guard let val = getAttributeData(lib: lib, session: session, obj: ch, attrType: CK_ATTRIBUTE_TYPE(CKA_VALUE)) else { continue }
            if val == certData {
                certId = getAttributeData(lib: lib, session: session, obj: ch, attrType: CK_ATTRIBUTE_TYPE(CKA_ID))
                break
            }
        }

        // Find private key with that ID
        if let id = certId, !id.isEmpty {
            var classVal = CK_OBJECT_CLASS(CKO_PRIVATE_KEY)
            var idBytes = Array(id)
            var template: [CK_ATTRIBUTE] = [
                CK_ATTRIBUTE(type: CK_ATTRIBUTE_TYPE(CKA_CLASS), pValue: &classVal, ulValueLen: CK_ULONG(MemoryLayout<CK_OBJECT_CLASS>.size)),
                CK_ATTRIBUTE(type: CK_ATTRIBUTE_TYPE(CKA_ID), pValue: &idBytes, ulValueLen: CK_ULONG(idBytes.count)),
            ]
            let rv = lib.pointee.C_FindObjectsInit(session, &template, CK_ULONG(template.count))
            guard rv == CKR_OK else { return nil }
            defer { _ = lib.pointee.C_FindObjectsFinal(session) }
            var obj: CK_OBJECT_HANDLE = 0
            var count: CK_ULONG = 0
            _ = lib.pointee.C_FindObjects(session, &obj, 1, &count)
            if count > 0 { return obj }
        }

        // Fallback: single private key
        let allKeys = findObjects(lib: lib, session: session, objectClass: CK_OBJECT_CLASS(CKO_PRIVATE_KEY))
        return allKeys.count == 1 ? allKeys[0] : nil
    }

    private static func getKeyType(lib: UnsafeMutablePointer<Pkcs11Lib>, session: CK_SESSION_HANDLE, key: CK_OBJECT_HANDLE) -> CK_KEY_TYPE {
        var keyType: CK_KEY_TYPE = 0
        var template = CK_ATTRIBUTE(type: CK_ATTRIBUTE_TYPE(CKA_KEY_TYPE), pValue: &keyType, ulValueLen: CK_ULONG(MemoryLayout<CK_KEY_TYPE>.size))
        _ = lib.pointee.C_GetAttributeValue(session, key, &template, 1)
        return keyType
    }

    private static func sign(lib: UnsafeMutablePointer<Pkcs11Lib>, session: CK_SESSION_HANDLE, key: CK_OBJECT_HANDLE, mechanism: CK_MECHANISM_TYPE, data: Data) throws -> Data {
        var mech = CK_MECHANISM(mechanism: mechanism, pParameter: nil, ulParameterLen: 0)
        var rv = lib.pointee.C_SignInit(session, &mech, key)
        guard rv == CKR_OK else { throw Pkcs11Error.signFailed(UInt(rv)) }

        var dataBytes = Array(data)
        var sigLen: CK_ULONG = 512
        var sigBuffer = [UInt8](repeating: 0, count: Int(sigLen))
        rv = lib.pointee.C_Sign(session, &dataBytes, CK_ULONG(dataBytes.count), &sigBuffer, &sigLen)
        guard rv == CKR_OK else { throw Pkcs11Error.signFailed(UInt(rv)) }

        return Data(sigBuffer.prefix(Int(sigLen)))
    }

    // MARK: - Helpers

    static func resolveModulePath(_ path: String) -> String {
        if path.hasPrefix("/") { return path }
        let execDir = Bundle.main.executablePath.map { URL(fileURLWithPath: $0).deletingLastPathComponent().path } ?? ""
        let candidates = [
            "\(execDir)/\(path)",
            "\(execDir)/../Resources/\(path)",
            "\(execDir)/\((path as NSString).lastPathComponent)",
            "\(Bundle.main.bundlePath)/Contents/Resources/\(path)",
            "\(FileManager.default.currentDirectoryPath)/\(path)",
        ]
        for c in candidates where FileManager.default.fileExists(atPath: c) { return c }
        return "\(execDir)/\(path)"
    }

    private static func ecdsaP1363ToDer(_ p1363: Data) -> Data {
        let half = p1363.count / 2
        let r = minimalBigInt(Array(p1363.prefix(half)))
        let s = minimalBigInt(Array(p1363.suffix(half)))
        let body = derInt(r) + derInt(s)
        return Data([0x30, UInt8(body.count)] + body)
    }

    private static func minimalBigInt(_ val: [UInt8]) -> [UInt8] {
        var v = val
        while v.count > 1 && v[0] == 0 { v.removeFirst() }
        if v[0] & 0x80 != 0 { v.insert(0, at: 0) }
        return v
    }

    private static func derInt(_ val: [UInt8]) -> [UInt8] { [0x02, UInt8(val.count)] + val }
}

// MARK: - X.509 Certificate parsing via Security framework

func parseCertificate(_ data: Data) -> CertInfo? {
    guard let cert = SecCertificateCreateWithData(nil, data as CFData) else { return nil }

    let serial = SecCertificateCopySerialNumberData(cert, nil)
        .map { ($0 as Data).map { String(format: "%02X", $0) }.joined() } ?? ""
    let subject = SecCertificateCopySubjectSummary(cert) as String? ?? ""

    var algorithm = "RSA"
    if let key = SecCertificateCopyKey(cert),
       let attrs = SecKeyCopyAttributes(key) as? [String: Any],
       let type = attrs[kSecAttrKeyType as String] as? String,
       type.contains("EC") {
        algorithm = "ECDSA"
    }

    var digest = [UInt8](repeating: 0, count: Int(CC_SHA1_DIGEST_LENGTH))
    data.withUnsafeBytes { CC_SHA1($0.baseAddress, CC_LONG(data.count), &digest) }
    let thumbprint = digest.map { String(format: "%02X", $0) }.joined()

    return CertInfo(
        serial: serial,
        subjectDN: subject,
        issuerDN: "",
        validFrom: "",
        validTo: "",
        thumbprint: thumbprint,
        certificate: data.base64EncodedString(),
        algorithm: algorithm
    )
}
