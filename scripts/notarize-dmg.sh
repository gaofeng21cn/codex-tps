#!/bin/zsh
set -euo pipefail

ROOT_DIR="${0:A:h:h}"
DMG_PATH="${1:-$ROOT_DIR/dist/Codex-TPS.dmg}"
CHECKSUM_PATH="${2:-$DMG_PATH.sha256}"
NOTARY_PROFILE="${CODEX_TPS_NOTARY_PROFILE:-}"
NOTARY_KEYCHAIN="${CODEX_TPS_NOTARY_KEYCHAIN:-}"

if [[ ! -f "$DMG_PATH" ]]; then
  echo "DMG not found: $DMG_PATH" >&2
  exit 1
fi
if [[ -z "$NOTARY_PROFILE" ]]; then
  echo "CODEX_TPS_NOTARY_PROFILE is required." >&2
  exit 1
fi

SUBMIT_PLIST="$(mktemp "${TMPDIR:-/tmp}/codex-tps-notary-submit.XXXXXX.plist")"
RESULT_PLIST="$(mktemp "${TMPDIR:-/tmp}/codex-tps-notary-result.XXXXXX.plist")"
cleanup() {
  rm -f "$SUBMIT_PLIST" "$RESULT_PLIST"
}
trap cleanup EXIT INT TERM

NOTARY_ARGS=(--keychain-profile "$NOTARY_PROFILE")
if [[ -n "$NOTARY_KEYCHAIN" ]]; then
  NOTARY_ARGS+=(--keychain "$NOTARY_KEYCHAIN")
fi

xcrun notarytool submit "$DMG_PATH" \
  "${NOTARY_ARGS[@]}" \
  --output-format plist >"$SUBMIT_PLIST"

SUBMISSION_ID="$(plutil -extract id raw -o - "$SUBMIT_PLIST")"
echo "Notarization submitted: $SUBMISSION_ID"

set +e
xcrun notarytool wait "$SUBMISSION_ID" \
  "${NOTARY_ARGS[@]}" \
  --timeout 30m \
  --output-format plist >"$RESULT_PLIST"
WAIT_STATUS=$?
set -e

if [[ "$WAIT_STATUS" -ne 0 ]]; then
  xcrun notarytool info "$SUBMISSION_ID" \
    "${NOTARY_ARGS[@]}" \
    --output-format plist >"$RESULT_PLIST" || true
fi

STATUS="$(plutil -extract status raw -o - "$RESULT_PLIST")"
if [[ "$STATUS" != "Accepted" ]]; then
  echo "Notarization $SUBMISSION_ID is not accepted; current status: $STATUS." >&2
  plutil -p "$RESULT_PLIST" >&2
  exit 1
fi

xcrun stapler staple "$DMG_PATH"
xcrun stapler validate "$DMG_PATH"

(
  cd "${DMG_PATH:h}"
  shasum -a 256 "${DMG_PATH:t}" >"${CHECKSUM_PATH:t}"
)

echo "Notarization accepted: $SUBMISSION_ID"
echo "$DMG_PATH"
echo "$CHECKSUM_PATH"
