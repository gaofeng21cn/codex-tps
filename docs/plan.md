# Delivery Plan

## Acceptance items

- [x] Repository, CodeGraph, build, and durable contracts are initialized.
- [x] Stateful parser handles current Codex usage events and fork replay.
- [x] Rolling 1-minute, 5-minute, 30-minute, and 1-hour metrics are covered by tests.
- [x] Native menu bar UI displays live local metrics without a Dock icon.
- [x] Launch-at-login control, refresh, session-folder access, and quit work.
- [x] Release app is packaged, ad-hoc signed, installed, and launched.
- [x] Snapshot CLI returns nonzero live data when Codex is active.
- [x] GitHub repository is created and the verified commit is pushed/read back.

## Verification record

- 2026-07-14: `swift test` passed 8 tests; `swift-format lint` passed.
- Real one-hour scan completed in about 1.3 seconds with about 19 MB peak RSS,
  `status=ready`, and zero malformed relevant lines.
- A real 4,239-record legacy subagent replay was suppressed; a rewritten-
  timestamp replay regression fixture now covers the behavior.
- Computer Use verified all four windows, manual refresh, selectable cadence,
  cadence persistence, accessibility labels, and non-overlapping layout.
- `/Applications/Codex TPS.app` passed plist and ad-hoc signature checks,
  launched as an `LSUIElement`, and had no network sockets.
- `gaofeng21cn/codex-tps` was created as a public repository; local and remote
  `main` SHAs matched after the initial push.
