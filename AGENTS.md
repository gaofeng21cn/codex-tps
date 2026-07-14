# Codex TPS Repository Guide

This repository owns a local-only macOS menu bar monitor for Codex token
throughput. It reads Codex session JSONL files and never uploads conversation
content.

## Runtime Contract

- Live input is `$CODEX_HOME/sessions` when `CODEX_HOME` is set, otherwise
  `~/.codex/sessions`.
- Count only `event_msg` entries whose payload type is `token_count`.
- Treat `last_token_usage` as the request increment. Use
  `total_token_usage` only for replay and duplicate detection.
- `total_tokens` is authoritative for throughput. Cached input and reasoning
  output are subsets used for breakdowns and must not be added again.
- Forked and subagent logs can rewrite parent history timestamps during replay.
  Preserve the fork state machine and cross-file deduplication tests.
- Do not persist, log, transmit, or render prompt or response bodies.

## Development

- Build: `swift build`
- Test: `swift test`
- Snapshot: `swift run codex-tps-snapshot --json`
- Package: `./scripts/build-app.sh`
- Universal DMG: `./scripts/build-dmg.sh`
- Install: `./scripts/install.sh`
- Install latest release: `./scripts/install-release.sh`

Runtime and packaging claims require a real installed-app readback in addition
to unit tests.

<!-- CODEGRAPH_START -->
## CodeGraph

- This repository uses the local `.codegraph/` index; it must remain Git ignored.
- Prefer CodeGraph for symbol, caller, impact, and flow queries. Use `rg` for
  literal text searches.
- Run `codegraph init .` or `codegraph sync .` when the index is missing or stale.
<!-- CODEGRAPH_END -->
