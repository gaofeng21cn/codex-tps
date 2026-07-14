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
DEST_APP="$INSTALL_DIR/Codex TPS.app"
STAGED_APP="$INSTALL_DIR/.Codex TPS.app.update.$$"
BACKUP_APP="$INSTALL_DIR/.Codex TPS.app.backup.$$"
REPLACEMENT_STARTED=0
HAD_EXISTING_APP=0

cleanup() {
  local exit_status=$?

  if [[ "$exit_status" -ne 0 && "$REPLACEMENT_STARTED" -eq 1 ]]; then
    if [[ -d "$BACKUP_APP" ]]; then
      rm -rf "$DEST_APP"
      mv "$BACKUP_APP" "$DEST_APP" || true
    elif [[ "$HAD_EXISTING_APP" -eq 0 ]]; then
      rm -rf "$DEST_APP"
    fi

    if [[ "$HAD_EXISTING_APP" -eq 1 && -d "$DEST_APP" && "${CODEX_TPS_NO_LAUNCH:-0}" != "1" ]]; then
      open "$DEST_APP" >/dev/null 2>&1 || true
    fi
  fi

  if [[ -n "$MOUNT_POINT" ]]; then
    hdiutil detach "$MOUNT_POINT" -quiet >/dev/null 2>&1 || true
  fi
  rm -rf "$STAGED_APP"
  if [[ "$exit_status" -eq 0 ]]; then
    rm -rf "$BACKUP_APP"
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

if [[ -z "$MOUNT_POINT" ]] || [[ ! -d "$SOURCE_APP" ]]; then
  echo "Codex TPS.app was not found in the mounted DMG." >&2
  exit 1
fi

codesign --verify --deep --strict "$SOURCE_APP"
VERSION="$(plutil -extract CFBundleShortVersionString raw "$SOURCE_APP/Contents/Info.plist")"
if [[ -n "${CODEX_TPS_EXPECTED_VERSION:-}" && "$VERSION" != "$CODEX_TPS_EXPECTED_VERSION" ]]; then
  echo "Expected Codex TPS $CODEX_TPS_EXPECTED_VERSION, but the DMG contains $VERSION." >&2
  exit 1
fi

mkdir -p "$INSTALL_DIR"
if [[ ! -w "$INSTALL_DIR" ]]; then
  echo "No write permission for $INSTALL_DIR." >&2
  echo "Use CODEX_TPS_INSTALL_DIR=\"$HOME/Applications\" to install for this user." >&2
  exit 1
fi

rm -rf "$STAGED_APP" "$BACKUP_APP"
ditto "$SOURCE_APP" "$STAGED_APP"
codesign --verify --deep --strict "$STAGED_APP"

pkill -x CodexTPS 2>/dev/null || true
for ((attempt = 0; attempt < 50; attempt++)); do
  if ! pgrep -x CodexTPS >/dev/null 2>&1; then
    break
  fi
  sleep 0.1
done
if pgrep -x CodexTPS >/dev/null 2>&1; then
  echo "Codex TPS could not be stopped for the update." >&2
  exit 1
fi

if [[ -d "$DEST_APP" ]]; then
  HAD_EXISTING_APP=1
fi
REPLACEMENT_STARTED=1
if [[ "$HAD_EXISTING_APP" -eq 1 ]]; then
  mv "$DEST_APP" "$BACKUP_APP"
fi
mv "$STAGED_APP" "$DEST_APP"
codesign --verify --deep --strict "$DEST_APP"

if [[ "${CODEX_TPS_NO_LAUNCH:-0}" != "1" ]]; then
  open "$DEST_APP"
fi

REPLACEMENT_STARTED=0
rm -rf "$BACKUP_APP"
if [[ -n "${CODEX_TPS_UPDATE_LOG:-}" ]]; then
  rm -f "$CODEX_TPS_UPDATE_LOG" || true
fi

echo "Installed Codex TPS $VERSION at $DEST_APP"
echo "This community build is ad-hoc signed and is not notarized by Apple."
