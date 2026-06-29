import AppKit

class AppDelegate: NSObject, NSApplicationDelegate {
    let config: AppConfig
    private var statusItem: NSStatusItem!
    private var certsMenuItem: NSMenuItem!
    private var httpServer: HttpServer!
    private var udpDiscovery: UdpDiscovery!
    private var mqttResponder: MqttSigningResponder?
    private var idleTimer: Timer?
    private var lastActivity = Date()
    private var settingsController = SettingsWindowController()

    init(config: AppConfig) {
        self.config = config
        super.init()
    }

    func applicationDidFinishLaunching(_ notification: Notification) {
        // Ensure accessory mode (no Dock icon)
        NSApp.setActivationPolicy(.accessory)
        setupStatusBar()
        startServices()
        startIdleTimer()
        refreshCerts()
        print("[USB Agent] HTTP  http://localhost:\(config.port)/")
        print("[USB Agent] UDP   discovery port \(config.discoveryPort)")
    }

    func applicationShouldTerminateAfterLastWindowClosed(_ sender: NSApplication) -> Bool {
        return false
    }

    func recordActivity() {
        lastActivity = Date()
    }

    // MARK: - Status Bar

    private func setupStatusBar() {
        statusItem = NSStatusBar.system.statusItem(withLength: NSStatusItem.squareLength)

        if let button = statusItem.button {
            button.image = NSImage(systemSymbolName: "lock.fill", accessibilityDescription: "USB Token Agent")
            button.image?.size = NSSize(width: 18, height: 18)
            button.image?.isTemplate = true // Adapts to dark/light menu bar
        }

        let menu = NSMenu()

        let statusItem = NSMenuItem(title: "✅ Running on port \(config.port)", action: nil, keyEquivalent: "")
        statusItem.isEnabled = false
        menu.addItem(statusItem)

        menu.addItem(.separator())

        certsMenuItem = NSMenuItem(title: "📜 Checking certificates...", action: nil, keyEquivalent: "")
        certsMenuItem.isEnabled = false
        menu.addItem(certsMenuItem)

        let refreshItem = NSMenuItem(title: "↻ Refresh", action: #selector(refreshCerts), keyEquivalent: "r")
        refreshItem.target = self
        menu.addItem(refreshItem)

        menu.addItem(.separator())

        let settingsItem = NSMenuItem(title: "⚙ Settings...", action: #selector(openSettings), keyEquivalent: ",")
        settingsItem.target = self
        menu.addItem(settingsItem)

        menu.addItem(.separator())

        let quitItem = NSMenuItem(title: "✕ Quit USB Agent", action: #selector(quit), keyEquivalent: "q")
        quitItem.target = self
        menu.addItem(quitItem)

        self.statusItem.menu = menu
    }

    // MARK: - Services

    private func startServices() {
        httpServer = HttpServer(port: config.port, config: config, delegate: self)
        httpServer.start()

        udpDiscovery = UdpDiscovery(port: config.discoveryPort, httpPort: config.port, config: config)
        udpDiscovery.start()

        // MQTT (if configured)
        if config.mqttBrokerHost != nil && !config.mqttBrokerHost!.isEmpty {
            mqttResponder = MqttSigningResponder(config: config, delegate: self)
            mqttResponder?.start()
            print("[USB Agent] MQTT  \(config.mqttBrokerHost!):\(config.mqttBrokerPort)")
        }
    }

    private func startIdleTimer() {
        guard config.idleTimeoutMinutes > 0 else { return }
        idleTimer = Timer.scheduledTimer(withTimeInterval: 60, repeats: true) { [weak self] _ in
            guard let self = self else { return }
            let idle = Date().timeIntervalSince(self.lastActivity)
            if idle >= Double(self.config.idleTimeoutMinutes) * 60 {
                print("[USB Agent] Idle for \(self.config.idleTimeoutMinutes) minutes. Exiting.")
                NSApp.terminate(nil)
            }
        }
    }

    // MARK: - Actions

    @objc func refreshCerts() {
        DispatchQueue.global().async { [weak self] in
            guard let self = self else { return }
            do {
                let certs = try Pkcs11.listCerts(modulePath: self.config.pkcs11Module)
                DispatchQueue.main.async {
                    if certs.isEmpty {
                        self.certsMenuItem.title = "⚠ No certificates found"
                    } else {
                        self.certsMenuItem.title = "📜 \(certs.count) cert(s) on token"
                    }
                }
            } catch {
                DispatchQueue.main.async {
                    let msg = String(error.localizedDescription.prefix(50))
                    self.certsMenuItem.title = "⚠ \(msg)"
                }
            }
        }
    }

    @objc func openSettings() {
        settingsController.show()
    }

    @objc func quit() {
        NSApp.terminate(nil)
    }
}
