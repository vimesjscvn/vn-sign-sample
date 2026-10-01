import AppKit
import SwiftUI

/// Window for Test Sign (menu bar). Runs `SignTestRunner` as soon as it opens and shows each
/// step as it finishes. A fresh window each time, so reopening it always runs a new test.
class SignTestWindowController {
    private let config: AppConfig
    private var window: NSWindow?

    init(config: AppConfig) {
        self.config = config
    }

    func show() {
        window?.close()

        let view = SignTestView(model: SignTestModel(config: config))
        let win = NSWindow(contentViewController: NSHostingController(rootView: view))
        win.title = "USB Token Agent — Test Sign PDF"
        win.setContentSize(NSSize(width: 640, height: 440))
        win.styleMask = [.titled, .closable, .resizable]
        win.center()
        win.isReleasedWhenClosed = false
        win.makeKeyAndOrderFront(nil)
        NSApp.activate(ignoringOtherApps: true)

        window = win
    }
}

final class SignTestModel: ObservableObject {
    @Published var log = ""
    @Published var running = false
    @Published var signedPdf: URL?

    private let config: AppConfig

    init(config: AppConfig) {
        self.config = config
    }

    /// Call on the main thread. The runner blocks, so it goes to a background queue.
    func run() {
        guard !running else { return }
        running = true
        signedPdf = nil
        log = ""

        let runner = SignTestRunner(config: config) { [weak self] line in
            DispatchQueue.main.async { self?.log += line + "\n" }
        }
        DispatchQueue.global(qos: .userInitiated).async { [weak self] in
            let url = runner.run()
            DispatchQueue.main.async {
                guard let self = self else { return }
                self.running = false
                self.signedPdf = url
                if let url = url {
                    NSWorkspace.shared.open(url)
                }
            }
        }
    }
}

struct SignTestView: View {
    @StateObject var model: SignTestModel

    var body: some View {
        VStack(alignment: .leading, spacing: 10) {
            Text("Signs a sample PDF through the signing server, the same way the hospital software does, "
                + "using the phone number, PIN, token and server this agent was started with.")
                .foregroundColor(.secondary)
                .fixedSize(horizontal: false, vertical: true)

            ScrollView {
                Text(model.log)
                    .font(.system(.body, design: .monospaced))
                    .textSelection(.enabled)
                    .frame(maxWidth: .infinity, alignment: .leading)
                    .padding(8)
            }
            .background(Color(nsColor: .textBackgroundColor))
            .border(Color.secondary.opacity(0.3))

            HStack {
                if model.running {
                    ProgressView().controlSize(.small)
                }
                Spacer()
                Button("Run again") { model.run() }
                    .disabled(model.running)
                Button("Open signed PDF") {
                    if let url = model.signedPdf {
                        NSWorkspace.shared.open(url)
                    }
                }
                .disabled(model.signedPdf == nil)
                Button("Close") { NSApp.keyWindow?.close() }
                    .keyboardShortcut(.cancelAction)
            }
        }
        .padding(16)
        .frame(minWidth: 560, minHeight: 380)
        .onAppear { model.run() }
    }
}
