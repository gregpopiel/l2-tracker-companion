#!/usr/bin/env bash
# Publishes a GitHub Release from the assets built by publish.sh.
# Reads the version from the .csproj (same source publish.sh uses), so the
# two scripts always agree on which version they're operating on.
# Run publish.sh first – this script only uploads what's already in releases/.
# Attaches exactly: L2Tracker-Setup.exe, L2Tracker-Portable.zip,
# L2Tracker-<ver>-full.nupkg, releases.win.json (see publish.sh's header for
# why RELEASES/assets.win.json are skipped – the updater doesn't read them).
# The notes are the "## v<Version>" section of CHANGELOG.md, built by towncrier
# in the version-bump pull request (docs/release.md, "Release notes"). The
# release is created as a DRAFT: publishing it on GitHub is what announces it
# on Discord. DRY_RUN=1 prints the notes and the command instead.
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
    echo "Missing $asset – run scripts/publish.sh first." >&2
    exit 1
  fi
done

NOTES="$(awk -v h="## $TAG" '$0 == h { f = 1; next } f && /^## / { exit } f' "$ROOT/CHANGELOG.md" | sed '/./,$!d')"
FIRST_LINE="${NOTES%%$'\n'*}"
# The template's last line: a section with nothing else was built from no files.
FOOTER="Something not right? Tell us in #bug-reports."
if [[ -z "$NOTES" ]]; then
  echo "No \"## $TAG\" section in CHANGELOG.md – build it with towncrier first (docs/release.md)." >&2
  exit 1
fi
if [[ "$FIRST_LINE" == \*\** || "$FIRST_LINE" == -* || "$FIRST_LINE" == "$FOOTER" ]]; then
  echo "The $TAG section has no summary: add a changelog.d/+<slug>.summary.md before building it." >&2
  exit 1
fi

NOTES_FILE="$(mktemp)"
trap 'rm -f "$NOTES_FILE"' EXIT
printf '%s\n' "$NOTES" > "$NOTES_FILE"

CMD=(gh release create "$TAG"
  --repo "$REPO"
  --title "L2Tracker Companion $TAG"
  --notes-file "$NOTES_FILE"
  --draft
  "${ASSETS[@]}")
if [[ -n "${DRY_RUN:-}" ]]; then
  printf '%s\n\n' "$NOTES"
  printf '%q ' "${CMD[@]}"; echo
  exit 0
fi
"${CMD[@]}"
echo "Draft created. Read it on GitHub, then publish it there (Edit -> Publish release)."
