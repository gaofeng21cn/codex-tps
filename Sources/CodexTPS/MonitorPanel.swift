import AppKit
import CodexTPSCore
import SwiftUI

struct MonitorPanel: View {
  @EnvironmentObject private var store: MonitorStore
  @EnvironmentObject private var updateManager: UpdateManager

  private var metrics: WindowMetrics {
    store.selectedWindow.metrics(from: store.snapshot)
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

      HStack(spacing: 4) {
        HeaderIconButton(
          systemName: "arrow.clockwise",
          help: "立即刷新",
          isDisabled: store.isRefreshing
        ) {
          Task { await store.refresh() }
        }

        HeaderIconButton(
          systemName: "arrow.down.circle",
          help: "检查更新",
          isDisabled: updateManager.isBusy
        ) {
          Task { await updateManager.checkForUpdates() }
        }

        HeaderIconButton(
          systemName: "folder",
          help: "打开 Codex 会话目录",
          action: store.openSessionsDirectory
        )
      }
    }
    .padding(16)
  }

  private var throughput: some View {
    VStack(alignment: .leading, spacing: 15) {
      Picker(
        "统计窗口",
        selection: Binding(
          get: { store.selectedWindow },
          set: { window in store.setMetricWindow(window) }
        )
      ) {
        ForEach(MetricWindow.allCases) { window in
          Text(window.rawValue).tag(window)
        }
      }
      .pickerStyle(.segmented)
      .labelsHidden()

      HStack(alignment: .firstTextBaseline, spacing: 6) {
        Text(RateFormatter.detailed(metrics.tokensPerSecond))
          .font(.system(size: 32, weight: .semibold, design: .rounded))
          .monospacedDigit()
          .lineLimit(1)
          .minimumScaleFactor(0.72)
          .layoutPriority(1)
        Text("token/s")
          .font(.caption.weight(.medium))
          .foregroundStyle(.secondary)

        Spacer(minLength: 12)

        VStack(alignment: .trailing, spacing: 2) {
          Text(metrics.requestsPerMinute.formatted(.number.precision(.fractionLength(1))))
            .font(.headline.weight(.semibold))
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
    VStack(spacing: 10) {
      updateStatus

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

  @ViewBuilder
  private var updateStatus: some View {
    switch updateManager.state {
    case .idle:
      EmptyView()
    case .checking:
      updateStatusLabel("正在检查更新", systemImage: nil)
    case .upToDate:
      updateStatusLabel("已是最新版本", systemImage: "checkmark.circle")
    case .available(let release):
      HStack(spacing: 8) {
        Label("发现新版本 \(release.tagName)", systemImage: "arrow.down.circle.fill")
          .lineLimit(1)
        Spacer()
        Button("立即更新") {
          updateManager.installAvailableUpdate()
        }
        .controlSize(.small)
      }
      .font(.caption)
    case .installing:
      updateStatusLabel("正在安装，应用将重新启动", systemImage: nil)
    case .failed(let message):
      Label(message, systemImage: "exclamationmark.triangle")
        .font(.caption)
        .foregroundStyle(.red)
        .frame(maxWidth: .infinity, alignment: .leading)
    }
  }

  private func updateStatusLabel(_ text: String, systemImage: String?) -> some View {
    HStack(spacing: 7) {
      if let systemImage {
        Image(systemName: systemImage)
      } else {
        ProgressView()
          .controlSize(.small)
      }
      Text(text)
    }
    .font(.caption)
    .foregroundStyle(.secondary)
    .frame(maxWidth: .infinity, alignment: .leading)
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
          .frame(width: 5, height: 5)
        Text(title)
          .foregroundStyle(.secondary)
      }
      .font(.caption)

      Text(RateFormatter.compact(value))
        .font(.subheadline.weight(.semibold))
        .monospacedDigit()
        .lineLimit(1)
        .minimumScaleFactor(0.75)
      Text("token/s")
        .font(.caption2)
        .foregroundStyle(.tertiary)
    }
    .frame(maxWidth: .infinity, alignment: .leading)
    .accessibilityElement(children: .combine)
  }
}

private struct HeaderIconButton: View {
  let systemName: String
  let help: String
  var isDisabled = false
  let action: () -> Void

  var body: some View {
    Button(action: action) {
      Image(systemName: systemName)
        .font(.system(size: 14, weight: .medium))
        .frame(width: 24, height: 24)
        .contentShape(Rectangle())
    }
    .buttonStyle(.borderless)
    .foregroundStyle(.secondary)
    .disabled(isDisabled)
    .help(help)
  }
}
