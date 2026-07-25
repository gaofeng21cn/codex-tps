@preconcurrency import Foundation

struct AmbientOpsService: Equatable {
  let instanceID: String
  let name: String
  let endpoint: URL
}

@MainActor
final class AmbientOpsDiscovery: NSObject, NetServiceBrowserDelegate, NetServiceDelegate {
  var onServiceResolved: ((AmbientOpsService) -> Void)?
  var onStatusChanged: ((String) -> Void)?

  private let browser = NetServiceBrowser()
  private var services: [NetService] = []
  private var resolving = false
  private(set) var isRunning = false
  private var preferredInstanceID: String?

  override init() {
    super.init()
    browser.delegate = self
  }

  func start(preferredInstanceID: String?) {
    self.preferredInstanceID = preferredInstanceID
    guard !isRunning else { return }
    isRunning = true
    onStatusChanged?("正在查找局域网服务")
    browser.searchForServices(ofType: "_ambient-ops._tcp.", inDomain: "local.")
  }

  func stop() {
    guard isRunning else { return }
    browser.stop()
    for service in services {
      service.stop()
    }
    services.removeAll()
    resolving = false
    isRunning = false
  }

  nonisolated func netServiceBrowserWillSearch(_ browser: NetServiceBrowser) {}

  nonisolated func netServiceBrowser(
    _ browser: NetServiceBrowser,
    didFind service: NetService,
    moreComing: Bool
  ) {
    let box = NetServiceBox(service)
    Task { @MainActor [weak self, box] in
      guard let self else { return }
      services.append(box.value)
      resolveNext()
    }
  }

  nonisolated func netServiceBrowser(
    _ browser: NetServiceBrowser,
    didRemove service: NetService,
    moreComing: Bool
  ) {
    let box = NetServiceBox(service)
    Task { @MainActor [weak self, box] in
      self?.services.removeAll { $0 == box.value }
    }
  }

  nonisolated func netServiceBrowser(
    _ browser: NetServiceBrowser,
    didNotSearch errorDict: [String: NSNumber]
  ) {
    Task { @MainActor [weak self] in
      self?.resolving = false
      self?.isRunning = false
      self?.onStatusChanged?("自动发现不可用")
    }
  }

  nonisolated func netServiceDidResolveAddress(_ sender: NetService) {
    let box = NetServiceBox(sender)
    Task { @MainActor [weak self, box] in
      self?.handleResolved(box.value)
    }
  }

  nonisolated func netService(
    _ sender: NetService,
    didNotResolve errorDict: [String: NSNumber]
  ) {
    let box = NetServiceBox(sender)
    Task { @MainActor [weak self, box] in
      guard let self else { return }
      resolving = false
      services.removeAll { $0 == box.value }
      resolveNext()
    }
  }

  private func resolveNext() {
    guard !resolving, let service = services.first else { return }
    resolving = true
    service.delegate = self
    service.resolve(withTimeout: 5)
  }

  private func handleResolved(_ service: NetService) {
    resolving = false
    services.removeAll { $0 == service }
    defer { resolveNext() }

    guard
      let hostName = service.hostName?.trimmingCharacters(in: CharacterSet(charactersIn: ".")),
      service.port > 0
    else { return }

    let txt = Self.txtDictionary(service.txtRecordData())
    guard txt["protocol"] == "1" else { return }
    let instanceID = txt["id"] ?? service.name.lowercased()
    if let preferredInstanceID, preferredInstanceID != instanceID {
      services.append(service)
      return
    }
    var components = URLComponents()
    components.scheme = "http"
    components.host = hostName
    components.port = service.port
    guard let endpoint = components.url else { return }

    onServiceResolved?(
      AmbientOpsService(
        instanceID: instanceID,
        name: txt["name"] ?? service.name,
        endpoint: endpoint
      ))
  }

  static func txtDictionary(_ data: Data?) -> [String: String] {
    guard let data else { return [:] }
    return NetService.dictionary(fromTXTRecord: data).reduce(into: [:]) {
      result, entry in
      guard let value = String(data: entry.value, encoding: .utf8) else { return }
      result[entry.key] = value
    }
  }

  static func normalizedPath(_ value: String?) -> String {
    guard let value, value.hasPrefix("/"), value.count <= 160 else {
      return "/display/overview"
    }
    return value
  }
}

private final class NetServiceBox: @unchecked Sendable {
  let value: NetService

  init(_ value: NetService) {
    self.value = value
  }
}
