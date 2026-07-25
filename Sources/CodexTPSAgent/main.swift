import CodexTPSCore
import Foundation
import Security

@main
struct CodexTPSAgentCommand {
  static func main() async throws {
    let arguments = Array(CommandLine.arguments.dropFirst())
    if arguments.contains("--help") || arguments.contains("-h") {
      print(Self.usage)
      return
    }
    guard arguments.allSatisfy({ $0 == "--once" }) else {
      throw AgentError.invalidArguments
    }

    let configuration = try AgentConfiguration(environment: ProcessInfo.processInfo.environment)
    let scanner = SessionScanner()
    let request = try AmbientOpsPushRequest(
      endpoint: configuration.endpoint,
      token: configuration.token,
      identity: configuration.identity
    )
    let client = AmbientOpsPushClient(request: request)
    var lastSuccessfulSnapshot: AmbientOpsAgentSnapshot?
    var petTracker = AmbientOpsPetTracker()
    var consecutiveFailures = 0

    repeat {
      let usage = await scanner.refresh()
      let pet = configuration.petDefinition.map {
        petTracker.snapshot(definition: $0, usage: usage)
      }
      let snapshot = AmbientOpsAgentSnapshot(
        usage: usage,
        identity: configuration.identity,
        fallback: lastSuccessfulSnapshot,
        pet: pet
      )
      if usage.status == .ready {
        lastSuccessfulSnapshot = snapshot
      }

      do {
        try await client.push(snapshot)
        consecutiveFailures = 0
        print(
          "Pushed \(configuration.identity.machineID): "
            + "\(snapshot.oneMinute.tps.formatted(.number.precision(.fractionLength(1)))) TPS"
        )
      } catch {
        consecutiveFailures += 1
        FileHandle.standardError.write(
          Data("Push failed: \(error.localizedDescription)\n".utf8))
        if arguments.contains("--once") {
          throw error
        }
      }

      if arguments.contains("--once") {
        break
      }
      let multiplier = min(pow(2, Double(consecutiveFailures)), 6)
      let delay = min(configuration.intervalSeconds * multiplier, 60)
      try await Task.sleep(for: .seconds(delay))
    } while !Task.isCancelled
  }

  private static let usage = """
    Usage: codex-tps-agent [--once]

    Required environment:
      CODEX_TPS_AMBIENT_URL       Ambient Ops base URL
      CODEX_TPS_AMBIENT_TOKEN     Agent push token, or use the Keychain option

    Optional environment:
      CODEX_TPS_AMBIENT_TOKEN_KEYCHAIN_SERVICE
                                   Generic-password Keychain service name
      CODEX_TPS_KEYCHAIN_ACCOUNT  Keychain account (default: current user)
      CODEX_TPS_MACHINE_ID        Stable machine ID (default: short hostname)
      CODEX_TPS_MACHINE_NAME      Display name (default: localized hostname)
      CODEX_TPS_PLATFORM          Platform label (default: macOS)
      CODEX_TPS_PUSH_INTERVAL     Push interval in seconds (default: 10)
      CODEX_TPS_PET_ID            Pet ID (default: ledger-owl; use none to disable)
      CODEX_TPS_PET_NAME          Pet display name
      CODEX_TPS_PET_ASSET_HASH    Pet spritesheet SHA-256
      CODEX_TPS_PET_ASSET_VERSION Pet sprite protocol version (default: 1)
      CODEX_HOME                  Alternate Codex home
    """
}

private struct AgentConfiguration {
  let endpoint: URL
  let token: String
  let identity: AmbientOpsMachineIdentity
  let intervalSeconds: Double
  let petDefinition: AmbientOpsPetDefinition?

  init(environment: [String: String]) throws {
    guard let endpointValue = environment["CODEX_TPS_AMBIENT_URL"],
      let endpoint = URL(string: endpointValue)
    else {
      throw AgentError.missingEnvironment("CODEX_TPS_AMBIENT_URL")
    }
    let token =
      environment["CODEX_TPS_AMBIENT_TOKEN"]
      ?? Self.keychainToken(environment: environment)
    guard let token, !token.isEmpty else {
      throw AgentError.missingEnvironment("CODEX_TPS_AMBIENT_TOKEN")
    }

    let hostName = ProcessInfo.processInfo.hostName
    let defaultID =
      hostName
      .split(separator: ".")
      .first
      .map(String.init)?
      .lowercased()
      .replacingOccurrences(
        of: #"[^a-z0-9._-]"#, with: "-", options: .regularExpression)
      ?? "mac"
    let machineID = environment["CODEX_TPS_MACHINE_ID"] ?? defaultID
    let machineName =
      environment["CODEX_TPS_MACHINE_NAME"]
      ?? Host.current().localizedName
      ?? machineID
    identity = try AmbientOpsMachineIdentity(
      machineID: machineID,
      machineName: machineName,
      platform: environment["CODEX_TPS_PLATFORM"] ?? "macOS"
    )
    self.endpoint = endpoint
    self.token = token

    let interval = Double(environment["CODEX_TPS_PUSH_INTERVAL"] ?? "10") ?? 10
    guard interval >= 2, interval <= 300 else {
      throw AgentError.invalidInterval
    }
    intervalSeconds = interval

    let petID = environment["CODEX_TPS_PET_ID"] ?? "ledger-owl"
    if petID == "none" {
      petDefinition = nil
    } else {
      let defaultHash =
        petID == "ledger-owl"
        ? "783854af87d6ee8639843ca7812917e062345b0095d43f9be5ea2374a41ada6c"
        : ""
      petDefinition = try AmbientOpsPetDefinition(
        id: petID,
        displayName: environment["CODEX_TPS_PET_NAME"] ?? "Ledger Owl",
        spriteVersionNumber: Int(environment["CODEX_TPS_PET_ASSET_VERSION"] ?? "1") ?? 1,
        assetHash: environment["CODEX_TPS_PET_ASSET_HASH"] ?? defaultHash
      )
    }
  }

  private static func keychainToken(environment: [String: String]) -> String? {
    guard let service = environment["CODEX_TPS_AMBIENT_TOKEN_KEYCHAIN_SERVICE"],
      !service.isEmpty
    else { return nil }
    let account = environment["CODEX_TPS_KEYCHAIN_ACCOUNT"] ?? NSUserName()
    let query: [CFString: Any] = [
      kSecClass: kSecClassGenericPassword,
      kSecAttrService: service,
      kSecAttrAccount: account,
      kSecReturnData: true,
      kSecMatchLimit: kSecMatchLimitOne,
    ]
    var result: CFTypeRef?
    guard SecItemCopyMatching(query as CFDictionary, &result) == errSecSuccess,
      let data = result as? Data
    else { return nil }
    return String(data: data, encoding: .utf8)
  }
}

private enum AgentError: LocalizedError {
  case invalidArguments
  case missingEnvironment(String)
  case invalidInterval

  var errorDescription: String? {
    switch self {
    case .invalidArguments:
      return "Invalid arguments. Use --help for usage."
    case .missingEnvironment(let name):
      return "Missing required environment variable \(name)"
    case .invalidInterval:
      return "CODEX_TPS_PUSH_INTERVAL must be between 2 and 300 seconds"
    }
  }
}
