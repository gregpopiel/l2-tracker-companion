#!/usr/bin/env bash
# POST the active session as a FarmLog (plan step 20).
set -euo pipefail

ROOT="$(cd "$(dirname "$0")/.." && pwd)"
WIN_ROOT="$(wslpath -w "$ROOT")"

if [[ $# -lt 1 ]]; then
  echo "Usage: $0 --character-id <id> --spot-id <id> [--bonus <n>]" >&2
  exit 1
fi

exec powershell.exe -NoProfile -Command \
  "Set-Location -LiteralPath '$WIN_ROOT'; dotnet run --project L2TrackerCompanion.OcrDump -- --save $*"
