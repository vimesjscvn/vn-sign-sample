import AppKit

class AppDelegate: NSObject, NSApplicationDelegate {
    let config: AppConfig
    private var statusItem: NSStatusItem!
    private var certsMenuItem: NSMenuItem!
    private var mqttMenuItem: NSMenuItem?  // nil when MQTT is not configured
    private var mqttConnected: Bool?       // nil until the first connect attempt settles
    private var mqttDetail = ""
    private var httpServer: HttpServer!
    private var udpDiscovery: UdpDiscovery!
    private var mqttResponder: MqttSigningResponder?
    private var idleTimer: Timer?
    private var lastActivity = Date()
    private var settingsController = SettingsWindowController()
    private lazy var signTestController = SignTestWindowController(config: config)

    private var mqttConfigured: Bool {
        return !(config.mqttBrokerHost ?? "").isEmpty
    }

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
        setStatusImage(linkDown: false)

        let menu = NSMenu()

        let statusItem = NSMenuItem(title: "✅ Running on port \(config.port)", action: nil, keyEquivalent: "")
        statusItem.isEnabled = false
        menu.addItem(statusItem)

        if mqttConfigured {
            let mqttItem = NSMenuItem(title: "MQTT: connecting...", action: nil, keyEquivalent: "")
            mqttItem.isEnabled = false
            menu.addItem(mqttItem)
            mqttMenuItem = mqttItem
            refreshMqttStatus()
        }

        menu.addItem(.separator())

        certsMenuItem = NSMenuItem(title: "📜 Checking certificates...", action: nil, keyEquivalent: "")
        certsMenuItem.isEnabled = false
        menu.addItem(certsMenuItem)

        let refreshItem = NSMenuItem(title: "↻ Refresh", action: #selector(refreshCerts), keyEquivalent: "r")
        refreshItem.target = self
        menu.addItem(refreshItem)

        menu.addItem(.separator())

        let signTestItem = NSMenuItem(title: "🧪 Test Sign PDF...", action: #selector(openSignTest), keyEquivalent: "t")
        signTestItem.target = self
        menu.addItem(signTestItem)

        let settingsItem = NSMenuItem(title: "⚙ Settings...", action: #selector(openSettings), keyEquivalent: ",")
        settingsItem.target = self
        menu.addItem(settingsItem)

        menu.addItem(.separator())

        let quitItem = NSMenuItem(title: "✕ Quit USB Agent", action: #selector(quit), keyEquivalent: "q")
        quitItem.target = self
        menu.addItem(quitItem)

        self.statusItem.menu = menu
    }

    private func setStatusImage(linkDown: Bool) {
        guard let button = statusItem.button else { return }
        let symbol = linkDown ? "lock.slash" : "lock.fill"
        let label = linkDown ? "USB Token Agent - MQTT not connected" : "USB Token Agent"
        button.image = NSImage(systemSymbolName: symbol, accessibilityDescription: label)
        button.image?.size = NSSize(width: 18, height: 18)
        button.image?.isTemplate = true // Adapts to dark/light menu bar
    }

    // MARK: - MQTT Status

    /// Called by the MQTT responder, on the main queue, when the broker link comes up, fails to
    /// come up or drops. Only changes arrive: a broker that stays down is not re-reported on
    /// every retry.
    func mqttConnectionChanged(connected: Bool, detail: String) {
        mqttConnected = connected
        mqttDetail = detail
        refreshMqttStatus()
    }

    private func refreshMqttStatus() {
        guard let item = mqttMenuItem else { return }
        let text: String
        let summary: String
        if mqttConnected == true {
            text = mqttDetail
            summary = "connected"
        } else if mqttConnected == false {
            text = "not connected - \(mqttDetail)"
            summary = "disconnected"
        } else {
            text = "connecting..."
            summary = "connecting..."
        }

        // The reasons MqttClient composes stay under 200 characters even with a long host name,
        // hint included; an unexpected system message could stretch the menu across the screen,
        // so it is cut and the full text stays in the item's tooltip.
        if text.count > 200 {
            item.title = "MQTT: \(text.prefix(197))..."
            item.toolTip = text
        } else {
            item.title = "MQTT: \(text)"
            item.toolTip = nil
        }

        // A dead link shows in the menu bar itself, before anyone opens the menu.
        setStatusImage(linkDown: mqttConnected == false)
        statusItem.button?.toolTip = "USB Token Agent - MQTT \(summary)"
    }

    // MARK: - Services

    private func startServices() {
        httpServer = HttpServer(port: config.port, config: config, delegate: self)
        httpServer.start()

        udpDiscovery = UdpDiscovery(port: config.discoveryPort, httpPort: config.port, config: config)
        udpDiscovery.start()

        // MQTT (if configured)
        if mqttConfigured {
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

    @objc func openSignTest() {
        signTestController.show()
    }

    @objc func quit() {
        NSApp.terminate(nil)
    }
}
