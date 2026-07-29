import Foundation

#if os(macOS)
import Darwin
#endif

/// Samples host-wide CPU utilization without reading or transmitting process or
/// conversation data. The first sample establishes a baseline; later samples
/// report the utilization between two reads.
public struct HostTelemetrySampler: Sendable {
  private var previousTotalTicks: UInt64?
  private var previousIdleTicks: UInt64?

  public init() {}

  public mutating func sampleCPUPercent() -> Double? {
    #if os(macOS)
    var load = host_cpu_load_info_data_t()
    var count = mach_msg_type_number_t(
      MemoryLayout<host_cpu_load_info_data_t>.size / MemoryLayout<integer_t>.size)
    let result = withUnsafeMutablePointer(to: &load) { pointer in
      pointer.withMemoryRebound(to: integer_t.self, capacity: Int(count)) { rebound in
        host_statistics(mach_host_self(), HOST_CPU_LOAD_INFO, rebound, &count)
      }
    }
    guard result == KERN_SUCCESS else { return nil }

    let user = UInt64(load.cpu_ticks.0)
    let system = UInt64(load.cpu_ticks.1)
    let nice = UInt64(load.cpu_ticks.2)
    let idle = UInt64(load.cpu_ticks.3)
    let total = user &+ system &+ nice &+ idle

    defer {
      previousTotalTicks = total
      previousIdleTicks = idle
    }
    guard let previousTotalTicks, let previousIdleTicks else { return nil }
    let totalDelta = total &- previousTotalTicks
    let idleDelta = idle &- previousIdleTicks
    guard totalDelta > 0, idleDelta <= totalDelta else { return nil }
    let busy = Double(totalDelta - idleDelta) / Double(totalDelta) * 100
    return min(100, max(0, busy))
    #else
    return nil
    #endif
  }
}
