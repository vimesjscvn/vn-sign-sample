import AppKit

// ── UsbTokenAgent (macOS Native) ─────────────────────────────────────────────
// Menu bar app that exposes USB token signing through PKCS#11.
// HTTP API on localhost:9999, UDP discovery on 9998.
// ─────────────────────────────────────────────────────────────────────────────

let config = AppConfig.load()

// MUST set activation policy BEFORE creating any windows or UI
// .accessory = no Dock icon, no app menu
NSApplication.shared.setActivationPolicy(.accessory)

let delegate = AppDelegate(config: config)
NSApplication.shared.delegate = delegate
NSApplication.shared.run()
