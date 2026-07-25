import Foundation

public struct AmbientOpsMachineIdentity: Equatable, Sendable {
  public let machineID: String
  public let machineName: String
  public let platform: String

  public init(machineID: String, machineName: String, platform: String) throws {
    guard
      machineID.range(of: #"^[A-Za-z0-9._-]{1,80}$"#, options: .regularExpression)
        != nil
    else {
      throw AmbientOpsPushError.invalidMachineID
    }
    self.machineID = machineID
    self.machineName = String(machineName.prefix(80))
    self.platform = String(platform.prefix(32))
  }
}

public struct AmbientOpsWindowSnapshot: Codable, Equatable, Sendable {
  public let tps: Double
  public let inputTokens: Int64
  public let outputTokens: Int64
  public let cachedInputTokens: Int64
  public let reasoningOutputTokens: Int64
  public let requests: Int

  init(metrics: WindowMetrics) {
    tps = metrics.tokensPerSecond
    inputTokens = Self.total(metrics.inputTokensPerSecond, seconds: metrics.windowSeconds)
    outputTokens = Self.total(metrics.outputTokensPerSecond, seconds: metrics.windowSeconds)
    cachedInputTokens = Self.total(
      metrics.cachedInputTokensPerSecond, seconds: metrics.windowSeconds)
    reasoningOutputTokens = Self.total(
      metrics.reasoningTokensPerSecond, seconds: metrics.windowSeconds)
    requests = metrics.requestCount
  }

  private static func total(_ rate: Double, seconds: Int) -> Int64 {
    Int64((rate * Double(seconds)).rounded())
  }
}

public struct AmbientOpsAgentSnapshot: Codable, Equatable, Sendable {
  public let schemaVersion: Int
  public let machineName: String
  public let platform: String
  public let generatedAt: Date
  public let status: String
  public let error: String?
  public let oneMinute: AmbientOpsWindowSnapshot
  public let fiveMinutes: AmbientOpsWindowSnapshot
  public let activeSessions: Int

  public init(
    usage: UsageSnapshot,
    identity: AmbientOpsMachineIdentity,
    fallback: AmbientOpsAgentSnapshot? = nil
  ) {
    schemaVersion = 1
    machineName = identity.machineName
    platform = identity.platform
    generatedAt = usage.generatedAt
    activeSessions = usage.status == .ready ? usage.activeSessions : fallback?.activeSessions ?? 0

    if usage.status == .ready {
      status = "live"
      error = nil
      oneMinute = AmbientOpsWindowSnapshot(metrics: usage.oneMinute)
      fiveMinutes = AmbientOpsWindowSnapshot(metrics: usage.fiveMinutes)
    } else {
      status = "error"
      error = Self.errorMessage(for: usage.status)
      oneMinute = fallback?.oneMinute ?? AmbientOpsWindowSnapshot(metrics: usage.oneMinute)
      fiveMinutes = fallback?.fiveMinutes ?? AmbientOpsWindowSnapshot(metrics: usage.fiveMinutes)
    }
  }

  private static func errorMessage(for status: CollectionStatus) -> String {
    switch status {
    case .ready:
      return ""
    case .sessionsDirectoryMissing:
      return "Codex sessions directory is unavailable"
    case .readFailed:
      return "Codex usage collection failed"
    }
  }
}

public struct AmbientOpsPushRequest: Sendable {
  public let endpoint: URL
  public let token: String
  public let identity: AmbientOpsMachineIdentity

  public init(endpoint: URL, token: String, identity: AmbientOpsMachineIdentity) throws {
    guard ["http", "https"].contains(endpoint.scheme?.lowercased() ?? ""),
      endpoint.host != nil
    else {
      throw AmbientOpsPushError.invalidEndpoint
    }
    let trimmedToken = token.trimmingCharacters(in: .whitespacesAndNewlines)
    guard !trimmedToken.isEmpty else {
      throw AmbientOpsPushError.missingToken
    }
    self.endpoint = endpoint
    self.token = trimmedToken
    self.identity = identity
  }

  public func urlRequest(snapshot: AmbientOpsAgentSnapshot) throws -> URLRequest {
    let url =
      endpoint
      .appendingPathComponent("api")
      .appendingPathComponent("v1")
      .appendingPathComponent("agents")
      .appendingPathComponent(identity.machineID)
      .appendingPathComponent("snapshot")
    var request = URLRequest(url: url, cachePolicy: .reloadIgnoringLocalCacheData)
    request.httpMethod = "POST"
    request.timeoutInterval = 10
    request.setValue("Bearer \(token)", forHTTPHeaderField: "Authorization")
    request.setValue("application/json", forHTTPHeaderField: "Content-Type")
    request.httpBody = try Self.encoder().encode(snapshot)
    return request
  }

  private static func encoder() -> JSONEncoder {
    let encoder = JSONEncoder()
    encoder.dateEncodingStrategy = .custom { date, encoder in
      var container = encoder.singleValueContainer()
      try container.encode(
        date.formatted(Date.ISO8601FormatStyle(includingFractionalSeconds: true)))
    }
    encoder.outputFormatting = [.sortedKeys, .withoutEscapingSlashes]
    return encoder
  }
}

public struct AmbientOpsPushClient: Sendable {
  private let request: AmbientOpsPushRequest
  private let transport: @Sendable (URLRequest) async throws -> URLResponse

  public init(request: AmbientOpsPushRequest, session: URLSession = .shared) {
    self.request = request
    transport = { request in
      let (_, response) = try await session.data(for: request)
      return response
    }
  }

  init(
    request: AmbientOpsPushRequest,
    transport: @escaping @Sendable (URLRequest) async throws -> URLResponse
  ) {
    self.request = request
    self.transport = transport
  }

  public func push(_ snapshot: AmbientOpsAgentSnapshot) async throws {
    let response = try await transport(request.urlRequest(snapshot: snapshot))
    guard let httpResponse = response as? HTTPURLResponse else {
      throw AmbientOpsPushError.invalidResponse
    }
    guard httpResponse.statusCode == 202 else {
      throw AmbientOpsPushError.server(httpResponse.statusCode)
    }
  }
}

public enum AmbientOpsPushError: LocalizedError, Equatable {
  case invalidMachineID
  case invalidEndpoint
  case missingToken
  case invalidResponse
  case server(Int)

  public var errorDescription: String? {
    switch self {
    case .invalidMachineID:
      return "Machine ID must contain 1-80 letters, numbers, dots, underscores, or hyphens"
    case .invalidEndpoint:
      return "Ambient Ops URL must be an absolute HTTP or HTTPS URL"
    case .missingToken:
      return "Ambient Ops push token is required"
    case .invalidResponse:
      return "Ambient Ops returned an invalid response"
    case .server(let statusCode):
      return "Ambient Ops returned HTTP \(statusCode)"
    }
  }
}
