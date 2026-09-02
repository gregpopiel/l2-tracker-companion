#!/usr/bin/env bash
# Lamp-table XP (plan step 11). Table crop 3×, all-or-none, sum-vs-dialog-XP
# gate. Compared to baselines/tesseract-lamps.tsv.
set -euo pipefail

ROOT="$(cd "$(dirname "$0")/.." && pwd)"
WIN_ROOT="$(wslpath -w "$ROOT")"
DEFAULT_IMAGES="$ROOT/../l2-tracker-frontend/experiments/ocr-poc/images"

IMAGES="${1:-$DEFAULT_IMAGES}"
if [[ ! -d "$IMAGES" ]]; then
  echo "POC images directory not found: $IMAGES" >&2
  echo "Pass the path to l2-tracker-frontend/experiments/ocr-poc/images" >&2
  exit 1
fi

WIN_IMAGES="$(wslpath -w "$IMAGES")"

if [[ $# -ge 2 ]]; then
  WIN_OUT="$(wslpath -w "$2")"
  exec powershell.exe -NoProfile -Command \
    "Set-Location -LiteralPath '$WIN_ROOT'; dotnet run --project L2TrackerCompanion.OcrDump -- --lamps '$WIN_IMAGES' '$WIN_OUT'"
fi

exec powershell.exe -NoProfile -Command \
  "Set-Location -LiteralPath '$WIN_ROOT'; dotnet run --project L2TrackerCompanion.OcrDump -- --lamps '$WIN_IMAGES'"
