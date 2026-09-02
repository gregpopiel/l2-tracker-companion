#!/usr/bin/env bash
# One-shot Play Report parse (plan step 13). PNG → XP / Adena / play time /
# lamp XP / location hint, printed as the WPF window would show them.
set -euo pipefail

if [[ $# -lt 1 ]]; then
  echo "Usage: $0 <screenshot.png>" >&2
  exit 1
fi

ROOT="$(cd "$(dirname "$0")/.." && pwd)"
WIN_ROOT="$(wslpath -w "$ROOT")"
PNG="$1"
if [[ ! -f "$PNG" ]]; then
  echo "PNG not found: $PNG" >&2
  exit 1
fi

WIN_PNG="$(wslpath -w "$PNG")"

exec powershell.exe -NoProfile -Command \
  "Set-Location -LiteralPath '$WIN_ROOT'; dotnet run --project L2TrackerCompanion.OcrDump -- --parse '$WIN_PNG'"
