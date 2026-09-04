#!/usr/bin/env bash
# Publishes a GitHub Release from the assets built by publish.sh.
# Reads the version from the .csproj (same source publish.sh uses), so the
# two scripts always agree on which version they're operating on.
# Run publish.sh first — this script only uploads what's already in releases/.
# Attaches exactly: L2Tracker-Setup.exe, L2Tracker-Portable.zip,
# L2Tracker-<ver>-full.nupkg, releases.win.json (see publish.sh's header for
# why RELEASES/assets.win.json are skipped — the updater doesn't read them).
# Requires: gh (authenticated against gregpopiel/l2-tracker-companion).
set -euo pipefail

ROOT="$(cd "$(dirname "$0")/.." && pwd)"
RELEASES_DIR="$ROOT/releases"
REPO="gregpopiel/l2-tracker-companion"

VERSION="$(grep -oP '(?<=<Version>)[^<]+' "$ROOT/L2TrackerCompanion/L2TrackerCompanion.csproj")"
TAG="v$VERSION"

ASSETS=(
  "$RELEASES_DIR/L2Tracker-Setup.exe"
  "$RELEASES_DIR/L2Tracker-Portable.zip"
  "$RELEASES_DIR/L2Tracker-$VERSION-full.nupkg"
  "$RELEASES_DIR/releases.win.json"
)

for asset in "${ASSETS[@]}"; do
  if [[ ! -f "$asset" ]]; then
    echo "Missing $asset — run scripts/publish.sh first." >&2
    exit 1
  fi
done

gh release create "$TAG" \
  --repo "$REPO" \
  --title "L2 Tracker Companion $TAG" \
  --generate-notes \
  "${ASSETS[@]}"
