import XCTest

@testable import CodexTPSCore

final class HostTelemetryTests: XCTestCase {
  func testUtilizationUsesIdleDeltaRatherThanNiceDelta() throws {
    XCTAssertEqual(
      try XCTUnwrap(
        HostTelemetrySampler.utilizationPercent(totalDelta: 100, idleDelta: 60)),
      40,
      accuracy: 0.001
    )
  }

  func testUtilizationRejectsInvalidDeltas() {
    XCTAssertNil(HostTelemetrySampler.utilizationPercent(totalDelta: 0, idleDelta: 0))
    XCTAssertNil(HostTelemetrySampler.utilizationPercent(totalDelta: 100, idleDelta: 101))
  }

  func testUtilizationClampsToTheValidRange() throws {
    XCTAssertEqual(
      try XCTUnwrap(
        HostTelemetrySampler.utilizationPercent(totalDelta: 100, idleDelta: 0)),
      100,
      accuracy: 0.001
    )
    XCTAssertEqual(
      try XCTUnwrap(
        HostTelemetrySampler.utilizationPercent(totalDelta: 100, idleDelta: 100)),
      0,
      accuracy: 0.001
    )
  }
}
