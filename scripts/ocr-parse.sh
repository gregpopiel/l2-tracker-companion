#!/usr/bin/env bash
# One-shot Play Report parse (plan step 13). PNG → XP / Adena / play time /
# lamp XP / location hint, printed as the WPF window would show them.
# A successful parse also appends a SQLite snapshot (plan step 14).
# Pass --new-session to wipe the active session file instead.
set -euo pipefail

if [[ $# -lt 1 ]]; then
  echo "Usage: $0 <screenshot.png>" >&2
  echo "       $0 --new-session" >&2
  echo "       $0 --match-hint <screenshot.png|hint>" >&2
  exit 1
fi

ROOT="$(cd "$(dirname "$0")/.." && pwd)"
WIN_ROOT="$(wslpath -w "$ROOT")"

if [[ "$1" == "--new-session" ]]; then
  exec powershell.exe -NoProfile -Command \
    "Set-Location -LiteralPath '$WIN_ROOT'; dotnet run --project L2TrackerCompanion.OcrDump -- --new-session"
fi

if [[ "$1" == "--match-hint" ]]; then
  if [[ $# -lt 2 ]]; then
    echo "Usage: $0 --match-hint <screenshot.png|hint>" >&2
    exit 1
  fi
  TARGET="$2"
  if [[ -f "$TARGET" ]]; then
    WIN_TARGET="$(wslpath -w "$TARGET")"
    exec powershell.exe -NoProfile -Command \
      "Set-Location -LiteralPath '$WIN_ROOT'; dotnet run --project L2TrackerCompanion.OcrDump -- --match-hint '$WIN_TARGET'"
  fi
  exec powershell.exe -NoProfile -Command \
    "Set-Location -LiteralPath '$WIN_ROOT'; dotnet run --project L2TrackerCompanion.OcrDump -- --match-hint '$TARGET'"
fi

PNG="$1"
if [[ ! -f "$PNG" ]]; then
  echo "PNG not found: $PNG" >&2
  exit 1
fi

WIN_PNG="$(wslpath -w "$PNG")"

exec powershell.exe -NoProfile -Command \
  "Set-Location -LiteralPath '$WIN_ROOT'; dotnet run --project L2TrackerCompanion.OcrDump -- --parse '$WIN_PNG'"
