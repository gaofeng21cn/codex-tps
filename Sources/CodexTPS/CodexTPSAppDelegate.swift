import AppKit
import SwiftUI

@MainActor
final class CodexTPSAppDelegate: NSObject, NSApplicationDelegate {
  private var previewStore: MonitorStore?
  private var previewWindow: NSWindow?

  func applicationDidFinishLaunching(_ notification: Notification) {
    guard ProcessInfo.processInfo.arguments.contains("--preview-window") else { return }

    let store = MonitorStore()
    store.start()

    let panel = MonitorPanel().environmentObject(store)
    let window = NSWindow(
      contentRect: NSRect(x: 0, y: 0, width: 380, height: 430),
      styleMask: [.titled, .closable],
      backing: .buffered,
      defer: false
    )
    window.title = "Codex TPS"
    window.contentView = NSHostingView(rootView: panel)
    window.center()
    window.makeKeyAndOrderFront(nil)

    previewStore = store
    previewWindow = window
    NSApplication.shared.activate(ignoringOtherApps: true)
  }
}
