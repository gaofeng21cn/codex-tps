# Architecture

## Goal

Display local Codex token throughput in the macOS menu bar with minute-level
freshness, low steady-state overhead, and no prompt-content processing outside
the local process.

## Data flow

```text
~/.codex/sessions/YYYY/MM/DD/*.jsonl
        -> incremental line reader
        -> stateful token_count parser
        -> replay/duplicate filter
        -> rolling event window
        -> MenuBarExtra and snapshot CLI
```

The scanner discovers files in today's and yesterday's session directories,
parses recently modified files once to establish state, then reads only appended
bytes. The UI refresh cadence is selectable while rolling windows remain fixed
at 1 minute, 5 minutes, 30 minutes, and 1 hour. The selected window is shared
by the panel and menu bar and persisted in `UserDefaults`, so changing the
segmented control updates the compact menu bar value immediately.

## Update flow

```text
github.com/.../releases/latest (HEAD redirect)
        -> validate release tag and required asset URLs
        -> user confirms Update now
        -> download DMG and published SHA-256
        -> verify checksum, expected version, and app signature
        -> stage, back up, atomically replace, and relaunch
```

The updater checks once after launch and every six hours while the app remains
running. It is independent of the session scanner: requests contain no Codex
log data, and only GitHub release metadata and assets are accessed. Automatic
checking never silently installs or terminates the app.

## Accounting invariants

1. `last_token_usage` is the request increment.
2. `total_token_usage` is cumulative state, never a direct increment when a
   `last_token_usage` value exists.
3. `total_tokens` is the throughput numerator. `cached_input_tokens` is a subset
   of input and `reasoning_output_tokens` is a subset of output.
4. Forked children can rewrite replay timestamps. The parser reads fork metadata
   and ignores inherited history until a verifiable child UUIDv7 turn begins;
   legacy UUIDv4 turns inside replay do not establish that boundary.
5. A stable event identity provides a second cross-file duplicate guard after
   fork replay filtering.
6. Collection decodes only `session_meta`, `task_started`, `turn_context`, and
   `token_count` records; message and tool-content lines remain opaque bytes.
7. No message body crosses the parser boundary.

## Product boundaries

- Tokscale remains the historical analysis/export surface; it is not invoked on
  the menu bar refresh path.
- Local usage events are operational telemetry, not billing authority. The app
  does not attribute usage to an API key or reconcile provider-side charges.
- Codex JSONL is an implementation surface. Fixture tests cover the shapes used
  here so schema drift fails visibly.
- Network access is restricted to GitHub release checks and update downloads.
  There is no analytics, login, or conversation-content upload path.
