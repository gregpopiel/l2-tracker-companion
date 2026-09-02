#!/usr/bin/env bash
# Validate a website JWT (plan step 17). On success the token is stored
# DPAPI-encrypted; a garbage token is not left on disk.
set -euo pipefail

ROOT="$(cd "$(dirname "$0")/.." && pwd)"
WIN_ROOT="$(wslpath -w "$ROOT")"

if [[ $# -lt 1 ]]; then
  echo "Usage: $0 --token <jwt>" >&2
  echo "       $0 --garbage" >&2
  echo "       $0 --status" >&2
  exit 1
fi

case "$1" in
  --garbage)
    exec powershell.exe -NoProfile -Command \
      "Set-Location -LiteralPath '$WIN_ROOT'; dotnet run --project L2TrackerCompanion.OcrDump -- --auth-garbage"
    ;;
  --status)
    exec powershell.exe -NoProfile -Command \
      "Set-Location -LiteralPath '$WIN_ROOT'; dotnet run --project L2TrackerCompanion.OcrDump -- --auth-status"
    ;;
  --token)
    if [[ $# -lt 2 ]]; then
      echo "Usage: $0 --token <jwt>" >&2
      exit 1
    fi
    TOKEN="$2"
    exec powershell.exe -NoProfile -Command \
      "Set-Location -LiteralPath '$WIN_ROOT'; dotnet run --project L2TrackerCompanion.OcrDump -- --auth '$TOKEN'"
    ;;
  *)
    echo "Usage: $0 --token <jwt> | --garbage | --status" >&2
    exit 1
    ;;
esac
