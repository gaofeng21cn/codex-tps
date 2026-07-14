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
- [x] Universal DMG and one-command release install are published and verified.
- [x] The menu bar follows the persisted panel window without extra label text.
- [x] Automatic release checks and a user-confirmed update path are implemented.
- [ ] `v0.2.0` is published and verified through a real remote update install.

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
- `v0.1.0` published a checksum-verified universal DMG. The documented remote
  one-command installer installed, launched, and passed signature, architecture,
  version, and no-network-socket readback.
- The `v0.2.0` candidate passed 12 tests, strict Swift formatting, ShellCheck,
  Actionlint, universal DMG verification, and installed-app signature readback.
- A real 50 MB subagent log containing 10,047 replayed token events and mixed
  UUIDv4/UUIDv7 turns reproduced the inflated 30-minute window. After the state
  machine fix, a fresh scan returned smooth `1m/5m/30m/1h` rates and 12-15
  requests per minute instead of about 196.
- Computer Use verified the selected window updates immediately, survives an
  app restart, renders without overlap, and reports the latest release without
  using the rate-limited GitHub API.
