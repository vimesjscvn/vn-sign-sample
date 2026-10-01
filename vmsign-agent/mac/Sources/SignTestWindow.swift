import AppKit
import SwiftUI

/// Window for Test Sign (menu bar). Runs `SignTestRunner` as soon as it opens and shows each
/// step as it finishes. A fresh window each time, so reopening it always runs a new test - unless
/// the last run is still going, in which case its window comes back instead.
class SignTestWindowController {
    private let config: AppConfig
    private var window: NSWindow?
    private var model: SignTestModel?

    init(config: AppConfig) {
        self.config = config
    }

    func show() {
        // A run cannot be stopped, so a new one would go alongside it: a second login to the
        // token - a wrong PIN then costs two of its few retries - and two threads loading and
        // finalising the same PKCS#11 module at once. Step 4 can take two minutes, plenty of time
        // to lose the window behind another and pick Test Sign again. The Windows agent is safe
        // because its Test Sign window is modal.
        if let window = window, model?.running == true {
            window.makeKeyAndOrderFront(nil)
            NSApp.activate(ignoringOtherApps: true)
            return
        }
        window?.close()

        let testModel = SignTestModel(config: config)
        model = testModel
        let view = SignTestView(model: testModel)
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
    // Owned by SignTestWindowController, which needs to see whether a run is still going.
    @ObservedObject var model: SignTestModel

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
