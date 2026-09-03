#!/usr/bin/env bash
# Velopack release build (plan step 22, revised for auto-update).
# Output: releases/L2Tracker-Setup.exe + L2Tracker-Portable.zip +
# L2Tracker-<ver>-full.nupkg + manifests (gitignored). Pack id is L2Tracker
# (`-u`); vpk still suffixes the installer/zip with `-win-`, so those two are
# renamed after pack. Do not change `-u` without a fresh pack — that id is
# baked into the nupkg and the install folder.
# Attach to GitHub only those three plus releases.win.json.
# Skip RELEASES and assets.win.json — updater does not read them.
# Requires the `vpk` global dotnet tool: dotnet tool install -g vpk
set -euo pipefail

ROOT="$(cd "$(dirname "$0")/.." && pwd)"
WIN_ROOT="$(wslpath -w "$ROOT")"
PUBLISH_OUT_WIN="${WIN_ROOT}\\publish-output"
RELEASES_OUT_WIN="${WIN_ROOT}\\releases"
RELEASES_DIR="$ROOT/releases"

VERSION="$(grep -oP '(?<=<Version>)[^<]+' "$ROOT/L2TrackerCompanion/L2TrackerCompanion.csproj")"

powershell.exe -NoProfile -Command \
  "Set-Location -LiteralPath '$WIN_ROOT'; \
   dotnet publish L2TrackerCompanion\\L2TrackerCompanion.csproj -c Release -r win-x64 --self-contained -p:DebugType=none -o '$PUBLISH_OUT_WIN'; \
   vpk pack -u L2Tracker --packTitle 'L2 Tracker Companion' -v $VERSION -p '$PUBLISH_OUT_WIN' -e L2TrackerCompanion.exe -o '$RELEASES_OUT_WIN'"

# Drop the channel suffix from the two user downloads (nupkg is already L2Tracker-<ver>-*.nupkg).
mv -f "$RELEASES_DIR/L2Tracker-win-Setup.exe" "$RELEASES_DIR/L2Tracker-Setup.exe"
mv -f "$RELEASES_DIR/L2Tracker-win-Portable.zip" "$RELEASES_DIR/L2Tracker-Portable.zip"
