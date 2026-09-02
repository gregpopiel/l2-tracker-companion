#!/usr/bin/env bash
# Run the WPF app from a WSL checkout. Plain `dotnet run` inside WSL invokes the
# Linux host or a broken UNC launch; this delegates to Windows PowerShell + dotnet.
set -euo pipefail

ROOT="$(cd "$(dirname "$0")/.." && pwd)"
WIN_ROOT="$(wslpath -w "$ROOT")"

exec powershell.exe -NoProfile -Command \
  "Set-Location -LiteralPath '$WIN_ROOT'; dotnet run --project L2TrackerCompanion"
