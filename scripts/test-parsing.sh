#!/usr/bin/env bash
# Run the WinRT-free parser tests (plan step 7). Delegates to Windows dotnet
# the same way the other scripts do — WSL has no SDK here.
set -euo pipefail

ROOT="$(cd "$(dirname "$0")/.." && pwd)"
WIN_ROOT="$(wslpath -w "$ROOT")"

exec powershell.exe -NoProfile -Command \
  "Set-Location -LiteralPath '$WIN_ROOT'; dotnet test L2TrackerCompanion.sln"
