import CodexTPSCore
import Foundation
import Security

enum AmbientOpsConnectionState: Equatable {
  case disabled
  case discovering
  case ready(name: String, endpoint: URL)
  case pairing(name: String, endpoint: URL, verificationCode: String, approvalURL: URL)
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
    case .pairing(_, _, let verificationCode, _):
      "等待批准 · 配对码 \(verificationCode)"
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
      .live(_, let endpoint, _), .pairing(_, let endpoint, _, _):
      endpoint
    case .disabled, .discovering, .failed:
      nil
    }
  }

  var isLive: Bool {
    if case .live = self { return true }
    return false
  }

  var pairingApprovalURL: URL? {
    guard case .pairing(_, _, _, let approvalURL) = self else { return nil }
    return approvalURL
  }
}

enum AmbientOpsPetChoice: String, CaseIterable, Identifiable {
  case localCodex = "local-codex"
  case none

  var id: Self { self }

  init(savedValue: String?) {
    self = savedValue == Self.none.rawValue ? .none : .localCodex
  }

  var label: String {
    switch self {
    case .localCodex:
      "本机 Codex 宠物"
    case .none:
      "不显示"
    }
  }
}

struct AmbientOpsKeychain {
  static let service = "cn.gaofeng.ambient-ops.agent-push"
  static let deviceKeyService = "cn.gaofeng.codex-tps.device-key"

  static func token(account: String = NSUserName()) -> String? {
    guard let data = data(service: service, account: account) else { return nil }
    guard
      let token = String(data: data, encoding: .utf8)?
        .trimmingCharacters(in: .whitespacesAndNewlines),
      !token.isEmpty
    else { return nil }
    return token
  }

  static func deviceKey(account: String = NSUserName()) throws -> AmbientOpsDeviceKey {
    if let saved = data(service: deviceKeyService, account: account) {
      do {
        return try AmbientOpsDeviceKey(rawRepresentation: saved)
      } catch {
        throw AmbientOpsKeychainError.invalidDeviceKey
      }
    }

    let created = AmbientOpsDeviceKey()
    let add: [CFString: Any] = [
      kSecClass: kSecClassGenericPassword,
      kSecAttrService: deviceKeyService,
      kSecAttrAccount: account,
      kSecAttrAccessible: kSecAttrAccessibleAfterFirstUnlockThisDeviceOnly,
      kSecValueData: created.rawRepresentation,
    ]
    let status = SecItemAdd(add as CFDictionary, nil)
    if status == errSecSuccess {
      return created
    }
    if status == errSecDuplicateItem,
      let saved = data(service: deviceKeyService, account: account),
      let existing = try? AmbientOpsDeviceKey(rawRepresentation: saved)
    {
      return existing
    }
    throw AmbientOpsKeychainError.unexpectedStatus(status)
  }

  private static func data(service: String, account: String) -> Data? {
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
      let data = result as? Data
    else { return nil }
    return data
  }
}

enum AmbientOpsKeychainError: LocalizedError {
  case invalidDeviceKey
  case unexpectedStatus(OSStatus)

  var errorDescription: String? {
    switch self {
    case .invalidDeviceKey:
      return "Keychain 中的设备配对密钥无效"
    case .unexpectedStatus(let status):
      return "无法将设备配对密钥存入 Keychain（\(status)）"
    }
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
