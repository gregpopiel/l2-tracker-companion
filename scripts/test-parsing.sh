#!/usr/bin/env bash
# Run every test project of the solution (parsing, session, API). Delegates to
# Windows dotnet the same way the other scripts do – WSL has no SDK here.
set -euo pipefail

ROOT="$(cd "$(dirname "$0")/.." && pwd)"
WIN_ROOT="$(wslpath -w "$ROOT")"

exec powershell.exe -NoProfile -Command \
  "Set-Location -LiteralPath '$WIN_ROOT'; dotnet test L2TrackerCompanion.sln"
