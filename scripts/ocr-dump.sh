#!/usr/bin/env bash
# Run the Windows.Media.Ocr word dump from a WSL checkout.
set -euo pipefail

ROOT="$(cd "$(dirname "$0")/.." && pwd)"
WIN_ROOT="$(wslpath -w "$ROOT")"

if [[ $# -lt 1 ]]; then
  echo "Usage: scripts/ocr-dump.sh <image.png> [output.txt]" >&2
  exit 1
fi

WIN_IMAGE="$(wslpath -w "$1")"

if [[ $# -ge 2 ]]; then
  WIN_OUT="$(wslpath -w "$2")"
  exec powershell.exe -NoProfile -Command \
    "Set-Location -LiteralPath '$WIN_ROOT'; dotnet run --project L2TrackerCompanion.OcrDump -- '$WIN_IMAGE' '$WIN_OUT'"
fi

exec powershell.exe -NoProfile -Command \
  "Set-Location -LiteralPath '$WIN_ROOT'; dotnet run --project L2TrackerCompanion.OcrDump -- '$WIN_IMAGE'"
