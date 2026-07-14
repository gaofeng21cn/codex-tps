#!/bin/bash
set -euo pipefail

REPOSITORY="gaofeng21cn/codex-tps"
INSTALL_DIR="${CODEX_TPS_INSTALL_DIR:-/Applications}"
DMG_URL="${CODEX_TPS_DMG_URL:-https://github.com/$REPOSITORY/releases/latest/download/Codex-TPS.dmg}"
CHECKSUM_URL="${CODEX_TPS_CHECKSUM_URL:-https://github.com/$REPOSITORY/releases/latest/download/Codex-TPS.dmg.sha256}"
TEMP_DIR="$(mktemp -d "${TMPDIR:-/tmp}/codex-tps-install.XXXXXX")"
DMG_PATH="$TEMP_DIR/Codex-TPS.dmg"
CHECKSUM_PATH="$TEMP_DIR/Codex-TPS.dmg.sha256"
MOUNT_POINT=""

cleanup() {
  if [[ -n "$MOUNT_POINT" ]]; then
    hdiutil detach "$MOUNT_POINT" -quiet >/dev/null 2>&1 || true
  fi
  rm -rf "$TEMP_DIR"
}
trap cleanup EXIT INT TERM

download() {
  curl --fail --location --silent --show-error --retry 3 "$1" --output "$2"
}

echo "Downloading the latest Codex TPS release..."
download "$DMG_URL" "$DMG_PATH"
download "$CHECKSUM_URL" "$CHECKSUM_PATH"

EXPECTED_HASH="$(awk 'NR == 1 { print $1 }' "$CHECKSUM_PATH" | tr '[:upper:]' '[:lower:]')"
ACTUAL_HASH="$(shasum -a 256 "$DMG_PATH" | awk '{ print $1 }')"
if [[ ! "$EXPECTED_HASH" =~ ^[0-9a-f]{64}$ ]] || [[ "$ACTUAL_HASH" != "$EXPECTED_HASH" ]]; then
  echo "Codex TPS DMG checksum verification failed." >&2
  exit 1
fi

ATTACH_OUTPUT="$(hdiutil attach "$DMG_PATH" -readonly -nobrowse)"
MOUNT_POINT="$(printf '%s\n' "$ATTACH_OUTPUT" | awk -F '\t' '$NF ~ /^\/Volumes\// { print $NF; exit }')"
SOURCE_APP="$MOUNT_POINT/Codex TPS.app"
DEST_APP="$INSTALL_DIR/Codex TPS.app"

if [[ -z "$MOUNT_POINT" ]] || [[ ! -d "$SOURCE_APP" ]]; then
  echo "Codex TPS.app was not found in the mounted DMG." >&2
  exit 1
fi

codesign --verify --deep --strict "$SOURCE_APP"
mkdir -p "$INSTALL_DIR"
if [[ ! -w "$INSTALL_DIR" ]]; then
  echo "No write permission for $INSTALL_DIR." >&2
  echo "Use CODEX_TPS_INSTALL_DIR=\"$HOME/Applications\" to install for this user." >&2
  exit 1
fi

pkill -x CodexTPS 2>/dev/null || true
rm -rf "$DEST_APP"
ditto "$SOURCE_APP" "$DEST_APP"
codesign --verify --deep --strict "$DEST_APP"

VERSION="$(plutil -extract CFBundleShortVersionString raw "$DEST_APP/Contents/Info.plist")"
if [[ "${CODEX_TPS_NO_LAUNCH:-0}" != "1" ]]; then
  open "$DEST_APP"
fi

echo "Installed Codex TPS $VERSION at $DEST_APP"
echo "This community build is ad-hoc signed and is not notarized by Apple."
