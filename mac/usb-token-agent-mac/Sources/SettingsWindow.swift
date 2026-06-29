import AppKit
import SwiftUI

class SettingsWindowController {
    private var window: NSWindow?

    func show() {
        if let window = window {
            window.makeKeyAndOrderFront(nil)
            NSApp.activate(ignoringOtherApps: true)
            return
        }

        let settingsView = SettingsView()
        let hostingController = NSHostingController(rootView: settingsView)

        let win = NSWindow(contentViewController: hostingController)
        win.title = "USB Token Agent — Settings"
        win.setContentSize(NSSize(width: 480, height: 460))
        win.styleMask = [.titled, .closable]
        win.center()
        win.isReleasedWhenClosed = false
        win.makeKeyAndOrderFront(nil)
        NSApp.activate(ignoringOtherApps: true)

        self.window = win
    }
}

struct SettingsView: View {
    @State private var port: String = "9999"
    @State private var discoveryPort: String = "9998"
    @State private var idleTimeout: String = "0"

    @State private var mqttEnabled: Bool = false
    @State private var mqttHost: String = ""
    @State private var mqttPort: String = "8883"
    @State private var mqttUsername: String = ""
    @State private var mqttPassword: String = ""
    @State private var mqttAgentId: String = ""
    @State private var mqttUseTls: Bool = true
    @State private var mqttCaCert: String = ""
    @State private var mqttClientPfx: String = ""
    @State private var mqttAllowUntrusted: Bool = false

    @State private var statusMessage: String = ""

    var body: some View {
        ScrollView {
            VStack(alignment: .leading, spacing: 16) {

                // Service section
                GroupBox(label: Text("Service").fontWeight(.semibold)) {
                    VStack(spacing: 10) {
                        HStack {
                            Text("HTTP Port:").frame(width: 120, alignment: .trailing)
                            TextField("9999", text: $port).frame(width: 80)
                            Spacer()
                        }
                        HStack {
                            Text("Discovery Port:").frame(width: 120, alignment: .trailing)
                            TextField("9998", text: $discoveryPort).frame(width: 80)
                            Spacer()
                        }
                        HStack {
                            Text("Idle Timeout:").frame(width: 120, alignment: .trailing)
                            TextField("0", text: $idleTimeout).frame(width: 80)
                            Text("minutes (0 = off)").foregroundColor(.secondary)
                            Spacer()
                        }
                    }
                    .padding(.top, 4)
                }

                // MQTT section
                GroupBox(label: Text("MQTT (optional)").fontWeight(.semibold)) {
                    VStack(alignment: .leading, spacing: 10) {
                        Toggle("Enable MQTT", isOn: $mqttEnabled)

                        if mqttEnabled {
                            HStack {
                                Text("Broker Host:").frame(width: 120, alignment: .trailing)
                                TextField("mqtt.example.com", text: $mqttHost)
                            }
                            HStack {
                                Text("Broker Port:").frame(width: 120, alignment: .trailing)
                                TextField("8883", text: $mqttPort).frame(width: 80)
                                Spacer()
                            }
                            HStack {
                                Text("Username:").frame(width: 120, alignment: .trailing)
                                TextField("", text: $mqttUsername)
                            }
                            HStack {
                                Text("Password:").frame(width: 120, alignment: .trailing)
                                SecureField("", text: $mqttPassword)
                            }
                            HStack {
                                Text("Agent ID:").frame(width: 120, alignment: .trailing)
                                TextField("(auto)", text: $mqttAgentId)
                            }

                            Divider()

                            Toggle("Use TLS", isOn: $mqttUseTls)

                            if mqttUseTls {
                                HStack {
                                    Text("CA Cert:").frame(width: 120, alignment: .trailing)
                                    TextField("", text: $mqttCaCert)
                                    Button("📁") { browseCaCert() }
                                }
                                HStack {
                                    Text("Client PFX:").frame(width: 120, alignment: .trailing)
                                    TextField("", text: $mqttClientPfx)
                                    Button("📁") { browseClientPfx() }
                                }
                                Toggle("Allow Untrusted (⚠ insecure)", isOn: $mqttAllowUntrusted)
                            }
                        }
                    }
                    .padding(.top, 4)
                }

                // Status
                if !statusMessage.isEmpty {
                    Text(statusMessage)
                        .foregroundColor(statusMessage.contains("✅") ? .green : .red)
                        .font(.caption)
                }

                // Buttons
                HStack {
                    Spacer()
                    Button("Cancel") {
                        NSApp.keyWindow?.close()
                    }
                    Button("Save & Restart") {
                        save()
                    }
                    .buttonStyle(.borderedProminent)
                }
            }
            .padding(20)
        }
        .frame(width: 460, height: 440)
        .onAppear { loadSettings() }
    }

    // MARK: - Load / Save

    private func loadSettings() {
        guard let json = loadConfigJson() else { return }
        port = "\(json["Port"] as? Int ?? 9999)"
        discoveryPort = "\(json["DiscoveryPort"] as? Int ?? 9998)"
        idleTimeout = "\(json["IdleTimeoutMinutes"] as? Int ?? 0)"

        if let mqtt = json["Mqtt"] as? [String: Any] {
            let host = mqtt["BrokerHost"] as? String ?? ""
            mqttEnabled = !host.isEmpty
            mqttHost = host
            mqttPort = "\(mqtt["BrokerPort"] as? Int ?? 8883)"
            mqttUsername = mqtt["Username"] as? String ?? ""
            mqttPassword = mqtt["Password"] as? String ?? ""
            mqttAgentId = mqtt["AgentId"] as? String ?? ""
            mqttUseTls = mqtt["UseTls"] as? Bool ?? true
            mqttCaCert = mqtt["CaCertPath"] as? String ?? ""
            mqttClientPfx = mqtt["ClientPfxPath"] as? String ?? ""
            mqttAllowUntrusted = mqtt["AllowUntrusted"] as? Bool ?? false
        }
    }

    private func save() {
        let json: [String: Any] = [
            "Port": Int(port) ?? 9999,
            "DiscoveryPort": Int(discoveryPort) ?? 9998,
            "IdleTimeoutMinutes": Int(idleTimeout) ?? 0,
            "Token": [
                "_comment": "PIN must be set via env var TOKEN__PIN.",
                "Pkcs11Module": "pkcs11/libbit4xpki.dylib",
            ] as [String: Any],
            "Mqtt": [
                "BrokerHost": mqttEnabled ? mqttHost : "",
                "BrokerPort": Int(mqttPort) ?? 8883,
                "Username": mqttUsername,
                "Password": mqttPassword,
                "AgentId": mqttAgentId,
                "UseTls": mqttUseTls,
                "CaCertPath": mqttCaCert,
                "ClientPfxPath": mqttClientPfx,
                "ClientPfxPassword": "",
                "AllowUntrusted": mqttAllowUntrusted,
            ] as [String: Any],
        ]

        guard let path = configFilePath() else {
            statusMessage = "❌ Cannot find appsettings.json"
            return
        }

        do {
            let data = try JSONSerialization.data(withJSONObject: json, options: [.prettyPrinted, .sortedKeys])
            try data.write(to: URL(fileURLWithPath: path))
            statusMessage = "✅ Saved. Restarting..."

            // Restart after short delay
            DispatchQueue.main.asyncAfter(deadline: .now() + 0.5) {
                restartApp()
            }
        } catch {
            statusMessage = "❌ Save failed: \(error.localizedDescription)"
        }
    }

    // MARK: - Helpers

    private func loadConfigJson() -> [String: Any]? {
        guard let path = configFilePath(),
              let data = FileManager.default.contents(atPath: path),
              let json = try? JSONSerialization.jsonObject(with: data) as? [String: Any]
        else { return nil }
        return json
    }

    private func configFilePath() -> String? {
        // User-writable config location (preferred for saving)
        let appSupport = FileManager.default.urls(for: .applicationSupportDirectory, in: .userDomainMask).first
        let userConfigDir = appSupport?.appendingPathComponent("UsbTokenAgent").path ?? ""
        let userConfig = userConfigDir + "/appsettings.json"

        // If user config exists, use it
        if FileManager.default.fileExists(atPath: userConfig) {
            return userConfig
        }

        // Otherwise read from app bundle (but can't write here)
        let bundleConfig = Bundle.main.bundlePath + "/Contents/Resources/appsettings.json"
        if FileManager.default.fileExists(atPath: bundleConfig) {
            // Copy to user location for writing
            try? FileManager.default.createDirectory(atPath: userConfigDir, withIntermediateDirectories: true)
            try? FileManager.default.copyItem(atPath: bundleConfig, toPath: userConfig)
            return userConfig
        }

        // Fallback
        return nil
    }

    private func browseCaCert() {
        if let path = browseFile(title: "Select CA Certificate", types: ["pem", "crt", "cer"]) {
            mqttCaCert = path
        }
    }

    private func browseClientPfx() {
        if let path = browseFile(title: "Select Client PFX", types: ["pfx", "p12"]) {
            mqttClientPfx = path
        }
    }

    private func browseFile(title: String, types: [String]) -> String? {
        let panel = NSOpenPanel()
        panel.title = title
        panel.allowedContentTypes = []
        panel.canChooseFiles = true
        panel.canChooseDirectories = false
        panel.allowsMultipleSelection = false
        let result = panel.runModal()
        return result == .OK ? panel.url?.path : nil
    }

    private func restartApp() {
        if let path = Bundle.main.executablePath {
            let task = Process()
            task.executableURL = URL(fileURLWithPath: "/usr/bin/open")
            task.arguments = [Bundle.main.bundlePath]
            try? task.run()
        }
        NSApp.terminate(nil)
    }
}
