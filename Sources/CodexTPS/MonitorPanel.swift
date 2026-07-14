import AppKit
import CodexTPSCore
import SwiftUI

struct MonitorPanel: View {
  @EnvironmentObject private var store: MonitorStore
  @State private var selectedWindow = MetricWindow.oneMinute

  private var metrics: WindowMetrics {
    selectedWindow.metrics(from: store.snapshot)
  }

  var body: some View {
    VStack(spacing: 0) {
      header
      Divider()
      throughput
      Divider()
      footer
    }
    .frame(width: 380)
    .fixedSize(horizontal: false, vertical: true)
  }

  private var header: some View {
    HStack(spacing: 12) {
      Image(nsImage: NSApplication.shared.applicationIconImage)
        .resizable()
        .interpolation(.high)
        .frame(width: 32, height: 32)
        .accessibilityHidden(true)

      VStack(alignment: .leading, spacing: 4) {
        Text("Codex TPS")
          .font(.headline)

        HStack(spacing: 6) {
          Circle()
            .fill(statusColor)
            .frame(width: 7, height: 7)
          Text(statusText)
          Text("·")
          Text(store.snapshot.generatedAt, style: .time)
        }
        .font(.caption)
        .foregroundStyle(.secondary)
      }

      Spacer()

      Button {
        Task { await store.refresh() }
      } label: {
        Image(systemName: "arrow.clockwise")
      }
      .buttonStyle(.borderless)
      .disabled(store.isRefreshing)
      .help("立即刷新")

      Button(action: store.openSessionsDirectory) {
        Image(systemName: "folder")
      }
      .buttonStyle(.borderless)
      .help("打开 Codex 会话目录")
    }
    .padding(16)
  }

  private var throughput: some View {
    VStack(alignment: .leading, spacing: 16) {
      Picker("统计窗口", selection: $selectedWindow) {
        ForEach(MetricWindow.allCases) { window in
          Text(window.rawValue).tag(window)
        }
      }
      .pickerStyle(.segmented)
      .labelsHidden()

      HStack(alignment: .firstTextBaseline) {
        Text(RateFormatter.detailed(metrics.tokensPerSecond))
          .font(.system(size: 34, weight: .semibold, design: .rounded))
          .monospacedDigit()
        Text("token/s")
          .font(.callout.weight(.medium))
          .foregroundStyle(.secondary)

        Spacer()

        VStack(alignment: .trailing, spacing: 2) {
          Text(metrics.requestsPerMinute.formatted(.number.precision(.fractionLength(1))))
            .font(.title3.weight(.semibold))
            .monospacedDigit()
          Text("请求/分钟")
            .font(.caption)
            .foregroundStyle(.secondary)
        }
      }

      HStack(spacing: 0) {
        RateColumn(title: "输入", value: metrics.inputTokensPerSecond, color: .blue)
        RateColumn(title: "缓存", value: metrics.cachedInputTokensPerSecond, color: .teal)
        RateColumn(title: "输出", value: metrics.outputTokensPerSecond, color: .orange)
        RateColumn(title: "推理", value: metrics.reasoningTokensPerSecond, color: .purple)
      }

      HStack(spacing: 16) {
        Label("\(store.snapshot.activeSessions) 个活跃会话", systemImage: "rectangle.stack")
        Spacer()
        Text("缓存占比 \(metrics.cacheRatio.formatted(.percent.precision(.fractionLength(0))))")
      }
      .font(.caption)
      .foregroundStyle(.secondary)
    }
    .padding(16)
  }

  private var footer: some View {
    VStack(spacing: 8) {
      HStack {
        Picker(
          "自动刷新",
          selection: Binding(
            get: { store.refreshCadence },
            set: { cadence in store.setRefreshCadence(cadence) }
          )
        ) {
          ForEach(RefreshCadence.allCases) { cadence in
            Text(cadence.label).tag(cadence)
          }
        }
        .pickerStyle(.menu)
        .controlSize(.small)
        .fixedSize()

        Spacer()

        Toggle(
          "登录时启动",
          isOn: Binding(
            get: { store.launchAtLoginEnabled },
            set: { enabled in store.setLaunchAtLogin(enabled) }
          )
        )
        .toggleStyle(.switch)
        .controlSize(.small)

        Spacer()

        Button(action: store.quit) {
          Image(systemName: "power")
        }
        .buttonStyle(.borderless)
        .help("退出 Codex TPS")
      }

      if let settingsError = store.settingsError {
        Text(settingsError)
          .font(.caption)
          .foregroundStyle(.red)
          .frame(maxWidth: .infinity, alignment: .leading)
      }
    }
    .padding(16)
  }

  private var statusText: String {
    if store.isRefreshing { return "读取中" }
    switch store.snapshot.status {
    case .ready:
      return store.snapshot.malformedRelevantLines == 0 ? "就绪" : "部分记录无法解析"
    case .sessionsDirectoryMissing:
      return "未找到会话目录"
    case .readFailed:
      return "读取失败"
    }
  }

  private var statusColor: Color {
    if store.isRefreshing { return .blue }
    switch store.snapshot.status {
    case .ready:
      return store.snapshot.malformedRelevantLines == 0 ? .green : .orange
    case .sessionsDirectoryMissing:
      return .orange
    case .readFailed:
      return .red
    }
  }
}

private struct RateColumn: View {
  let title: String
  let value: Double
  let color: Color

  var body: some View {
    VStack(alignment: .leading, spacing: 5) {
      HStack(spacing: 5) {
        Circle()
          .fill(color)
          .frame(width: 6, height: 6)
        Text(title)
          .foregroundStyle(.secondary)
      }
      .font(.caption)

      Text(RateFormatter.compact(value))
        .font(.callout.weight(.semibold))
        .monospacedDigit()
      Text("token/s")
        .font(.caption2)
        .foregroundStyle(.tertiary)
    }
    .frame(maxWidth: .infinity, alignment: .leading)
  }
}
