#!/usr/bin/env bash
# Self-contained win-x64 single-file publish (plan step 22).
# Output: artifacts/win-x64/L2TrackerCompanion.exe (gitignored).
set -euo pipefail

ROOT="$(cd "$(dirname "$0")/.." && pwd)"
WIN_ROOT="$(wslpath -w "$ROOT")"
OUT_WIN="${WIN_ROOT}\\artifacts\\win-x64"

exec powershell.exe -NoProfile -Command \
  "Set-Location -LiteralPath '$WIN_ROOT'; dotnet publish L2TrackerCompanion\\L2TrackerCompanion.csproj -c Release -r win-x64 --self-contained -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:DebugType=none -o '$OUT_WIN'"
