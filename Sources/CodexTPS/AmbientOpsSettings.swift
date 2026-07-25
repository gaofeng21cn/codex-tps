import CodexTPSCore
import Foundation
import Security

enum AmbientOpsConnectionState: Equatable {
  case disabled
  case discovering
  case ready(name: String, endpoint: URL)
  case pushing(name: String, endpoint: URL)
  case live(name: String, endpoint: URL, pushedAt: Date)
  case failed(message: String)

  var label: String {
    switch self {
    case .disabled:
      "未启用"
    case .discovering:
      "正在自动发现"
    case .ready(let name, _):
      "已发现 \(name)"
    case .pushing(let name, _):
      "正在推送到 \(name)"
    case .live(let name, _, _):
      "\(name) · 已连接"
    case .failed(let message):
      message
    }
  }

  var endpoint: URL? {
    switch self {
    case .ready(_, let endpoint), .pushing(_, let endpoint),
      .live(_, let endpoint, _):
      endpoint
    case .disabled, .discovering, .failed:
      nil
    }
  }

  var isLive: Bool {
    if case .live = self { return true }
    return false
  }
}

enum AmbientOpsPetChoice: String, CaseIterable, Identifiable {
  case ledgerOwl = "ledger-owl"
  case none

  var id: Self { self }

  var label: String {
    switch self {
    case .ledgerOwl:
      "Ledger Owl"
    case .none:
      "不显示"
    }
  }

  var definition: AmbientOpsPetDefinition? {
    switch self {
    case .ledgerOwl:
      try? AmbientOpsPetDefinition(
        id: rawValue,
        displayName: "Ledger Owl",
        spriteVersionNumber: 1,
        assetHash: "783854af87d6ee8639843ca7812917e062345b0095d43f9be5ea2374a41ada6c"
      )
    case .none:
      nil
    }
  }
}

struct AmbientOpsKeychain {
  static let service = "cn.gaofeng.ambient-ops.agent-push"

  static func token(account: String = NSUserName()) -> String? {
    let query: [CFString: Any] = [
      kSecClass: kSecClassGenericPassword,
      kSecAttrService: service,
      kSecAttrAccount: account,
      kSecReturnData: true,
      kSecMatchLimit: kSecMatchLimitOne,
    ]
    var result: CFTypeRef?
    guard
      SecItemCopyMatching(query as CFDictionary, &result) == errSecSuccess,
      let data = result as? Data,
      let token = String(data: data, encoding: .utf8)?
        .trimmingCharacters(in: .whitespacesAndNewlines),
      !token.isEmpty
    else { return nil }
    return token
  }
}

extension AmbientOpsMachineIdentity {
  static func defaultLocalMachineID() -> String {
    let hostName = Host.current().localizedName ?? ProcessInfo.processInfo.hostName
    return
      hostName
      .split(separator: ".")
      .first
      .map(String.init)?
      .lowercased()
      .replacingOccurrences(
        of: #"[^a-z0-9._-]"#, with: "-", options: .regularExpression)
      ?? "mac"
  }

  static func localMachine(machineID: String) throws -> AmbientOpsMachineIdentity {
    return try AmbientOpsMachineIdentity(
      machineID: machineID,
      machineName: Host.current().localizedName ?? machineID,
      platform: "macOS"
    )
  }
}
