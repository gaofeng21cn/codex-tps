import Foundation
import XCTest

@testable import CodexTPSCore

final class AmbientOpsPushTests: XCTestCase {
  func testMapsOnlyAggregateSnapshotFields() throws {
    let snapshot = usageSnapshot(status: .ready)
    let identity = try AmbientOpsMachineIdentity(
      machineID: "primary-mac",
      machineName: "Primary Mac",
      platform: "macOS"
    )

    let payload = AmbientOpsAgentSnapshot(usage: snapshot, identity: identity)
    let data = try JSONEncoder().encode(payload)
    let object = try XCTUnwrap(JSONSerialization.jsonObject(with: data) as? [String: Any])

    XCTAssertEqual(
      Set(object.keys),
      [
        "schemaVersion", "machineName", "platform", "generatedAt", "status",
        "oneMinute", "fiveMinutes", "activeSessions",
      ])
    XCTAssertEqual(payload.status, "live")
    XCTAssertEqual(payload.oneMinute.tps, 10)
    XCTAssertEqual(payload.oneMinute.inputTokens, 480)
    XCTAssertEqual(payload.oneMinute.cachedInputTokens, 300)
    XCTAssertEqual(payload.oneMinute.outputTokens, 120)
    XCTAssertEqual(payload.oneMinute.reasoningOutputTokens, 60)
    XCTAssertEqual(payload.oneMinute.requests, 2)
  }

  func testCollectionFailureRetainsLastSuccessfulValues() throws {
    let identity = try AmbientOpsMachineIdentity(
      machineID: "primary-mac",
      machineName: "Primary Mac",
      platform: "macOS"
    )
    let live = AmbientOpsAgentSnapshot(
      usage: usageSnapshot(status: .ready), identity: identity)
    let failed = AmbientOpsAgentSnapshot(
      usage: usageSnapshot(status: .readFailed), identity: identity, fallback: live)

    XCTAssertEqual(failed.status, "error")
    XCTAssertEqual(failed.error, "Codex usage collection failed")
    XCTAssertEqual(failed.oneMinute, live.oneMinute)
    XCTAssertEqual(failed.fiveMinutes, live.fiveMinutes)
    XCTAssertEqual(failed.activeSessions, live.activeSessions)
  }

  func testBuildsAuthenticatedRequestWithoutIdentityLeaks() throws {
    let identity = try AmbientOpsMachineIdentity(
      machineID: "primary-mac",
      machineName: "Primary Mac",
      platform: "macOS"
    )
    let configuration = try AmbientOpsPushRequest(
      endpoint: XCTUnwrap(URL(string: "https://ops.example.test/base")),
      token: "test-token",
      identity: identity
    )
    let request = try configuration.urlRequest(
      snapshot: AmbientOpsAgentSnapshot(
        usage: usageSnapshot(status: .ready), identity: identity))

    XCTAssertEqual(
      request.url?.absoluteString,
      "https://ops.example.test/base/api/v1/agents/primary-mac/snapshot")
    XCTAssertEqual(request.httpMethod, "POST")
    XCTAssertEqual(request.value(forHTTPHeaderField: "Authorization"), "Bearer test-token")
    let body = try XCTUnwrap(request.httpBody)
    let text = try XCTUnwrap(String(data: body, encoding: .utf8))
    XCTAssertFalse(text.contains("session"))
    XCTAssertFalse(text.contains("/Users/"))
    XCTAssertFalse(text.contains("prompt"))
    XCTAssertFalse(text.contains("response"))
  }

  func testRejectsUnsafeMachineIDAndEndpoint() throws {
    XCTAssertThrowsError(
      try AmbientOpsMachineIdentity(
        machineID: "../primary",
        machineName: "Primary",
        platform: "macOS"
      ))
    let identity = try AmbientOpsMachineIdentity(
      machineID: "primary",
      machineName: "Primary",
      platform: "macOS"
    )
    XCTAssertThrowsError(
      try AmbientOpsPushRequest(
        endpoint: XCTUnwrap(URL(string: "file:///tmp/ambient")),
        token: "test-token",
        identity: identity
      ))
  }

  func testClientRequiresAcceptedResponse() async throws {
    let identity = try AmbientOpsMachineIdentity(
      machineID: "primary",
      machineName: "Primary",
      platform: "macOS"
    )
    let request = try AmbientOpsPushRequest(
      endpoint: XCTUnwrap(URL(string: "https://ops.example.test")),
      token: "test-token",
      identity: identity
    )
    let snapshot = AmbientOpsAgentSnapshot(
      usage: usageSnapshot(status: .ready), identity: identity)
    let accepted = AmbientOpsPushClient(request: request) { request in
      HTTPURLResponse(
        url: request.url!,
        statusCode: 202,
        httpVersion: nil,
        headerFields: nil
      )!
    }
    try await accepted.push(snapshot)

    let rejected = AmbientOpsPushClient(request: request) { request in
      HTTPURLResponse(
        url: request.url!,
        statusCode: 401,
        httpVersion: nil,
        headerFields: nil
      )!
    }
    do {
      try await rejected.push(snapshot)
      XCTFail("Expected a non-202 response to fail")
    } catch let error as AmbientOpsPushError {
      XCTAssertEqual(error, .server(401))
    }
  }

  private func usageSnapshot(status: CollectionStatus) -> UsageSnapshot {
    UsageSnapshot(
      generatedAt: Date(timeIntervalSince1970: 1_000),
      oneMinute: WindowMetrics(
        windowSeconds: 60,
        requestCount: 2,
        requestsPerMinute: 2,
        tokensPerSecond: 10,
        inputTokensPerSecond: 8,
        cachedInputTokensPerSecond: 5,
        outputTokensPerSecond: 2,
        reasoningTokensPerSecond: 1,
        cacheRatio: 0.625,
        totalTokens: 600
      ),
      fiveMinutes: WindowMetrics(
        windowSeconds: 300,
        requestCount: 7,
        requestsPerMinute: 1.4,
        tokensPerSecond: 6,
        inputTokensPerSecond: 4.8,
        cachedInputTokensPerSecond: 3,
        outputTokensPerSecond: 1.2,
        reasoningTokensPerSecond: 0.4,
        cacheRatio: 0.625,
        totalTokens: 1_800
      ),
      thirtyMinutes: .empty(windowSeconds: 1_800),
      oneHour: .empty(windowSeconds: 3_600),
      activeSessions: 3,
      malformedRelevantLines: 0,
      status: status
    )
  }
}
