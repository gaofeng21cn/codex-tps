import SwiftUI

@main
@MainActor
struct CodexTPSApp: App {
  @NSApplicationDelegateAdaptor(CodexTPSAppDelegate.self) private var appDelegate
  @StateObject private var store: MonitorStore

  init() {
    let store = MonitorStore()
    _store = StateObject(wrappedValue: store)
    store.start()
  }

  var body: some Scene {
    MenuBarExtra {
      MonitorPanel()
        .environmentObject(store)
    } label: {
      HStack(spacing: 4) {
        Image(systemName: "waveform.path.ecg")
        Text(store.menuBarTitle)
          .monospacedDigit()
      }
      .accessibilityLabel("Codex token throughput, \(store.menuBarTitle)")
    }
    .menuBarExtraStyle(.window)
  }
}
