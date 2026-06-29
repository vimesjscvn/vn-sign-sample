import Foundation

struct AppConfig {
    var port: Int = 9999
    var discoveryPort: Int = 9998
    var idleTimeoutMinutes: Int = 0
    var pkcs11Module: String = "pkcs11/libbit4xpki.dylib"
    var tokenPin: String? = nil

    // MQTT (optional)
    var mqttBrokerHost: String? = nil
    var mqttBrokerPort: Int = 8883
    var mqttUsername: String? = nil
    var mqttPassword: String? = nil
    var mqttAgentId: String? = nil
    var mqttUseTls: Bool = true

    static func load() -> AppConfig {
        var config = AppConfig()

        // Load from appsettings.json next to the executable or in Resources
        if let json = loadJson() {
            config.port = json["Port"] as? Int ?? 9999
            config.discoveryPort = json["DiscoveryPort"] as? Int ?? 9998
            config.idleTimeoutMinutes = json["IdleTimeoutMinutes"] as? Int ?? 0

            if let token = json["Token"] as? [String: Any] {
                if let mod = token["Pkcs11Module"] as? String, !mod.isEmpty {
                    config.pkcs11Module = mod
                }
                if let pin = token["Pin"] as? String, !pin.isEmpty {
                    config.tokenPin = pin
                }
            }

            if let mqtt = json["Mqtt"] as? [String: Any] {
                if let host = mqtt["BrokerHost"] as? String, !host.isEmpty {
                    config.mqttBrokerHost = host
                }
                config.mqttBrokerPort = mqtt["BrokerPort"] as? Int ?? 8883
                config.mqttUsername = mqtt["Username"] as? String
                config.mqttPassword = mqtt["Password"] as? String
                config.mqttAgentId = mqtt["AgentId"] as? String
                config.mqttUseTls = mqtt["UseTls"] as? Bool ?? true
            }
        }

        // Env var overrides
        if let envPin = ProcessInfo.processInfo.environment["TOKEN__PIN"], !envPin.isEmpty {
            config.tokenPin = envPin
        }
        if let envPort = ProcessInfo.processInfo.environment["PORT"], let p = Int(envPort) {
            config.port = p
        }

        return config
    }

    private static func loadJson() -> [String: Any]? {
        let appSupport = FileManager.default.urls(for: .applicationSupportDirectory, in: .userDomainMask).first
        let userConfig = appSupport?.appendingPathComponent("UsbTokenAgent/appsettings.json").path ?? ""

        let candidates = [
            userConfig, // User config (writable) — takes priority
            Bundle.main.bundlePath + "/Contents/Resources/appsettings.json",
            Bundle.main.executablePath.map { URL(fileURLWithPath: $0).deletingLastPathComponent().path + "/appsettings.json" } ?? "",
            FileManager.default.currentDirectoryPath + "/appsettings.json",
        ]

        for path in candidates {
            if let data = FileManager.default.contents(atPath: path),
               let json = try? JSONSerialization.jsonObject(with: data) as? [String: Any] {
                return json
            }
        }
        return nil
    }
}
