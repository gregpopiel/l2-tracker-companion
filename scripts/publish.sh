#!/usr/bin/env bash
# Velopack release build (plan step 22, revised for auto-update).
# Output: releases/Setup.exe + .nupkg + delta files (gitignored). Requires the
# `vpk` global dotnet tool on this machine: dotnet tool install -g vpk
set -euo pipefail

ROOT="$(cd "$(dirname "$0")/.." && pwd)"
WIN_ROOT="$(wslpath -w "$ROOT")"
PUBLISH_OUT_WIN="${WIN_ROOT}\\publish-output"
RELEASES_OUT_WIN="${WIN_ROOT}\\releases"

VERSION="$(grep -oP '(?<=<Version>)[^<]+' "$ROOT/L2TrackerCompanion/L2TrackerCompanion.csproj")"

exec powershell.exe -NoProfile -Command \
  "Set-Location -LiteralPath '$WIN_ROOT'; \
   dotnet publish L2TrackerCompanion\\L2TrackerCompanion.csproj -c Release -r win-x64 --self-contained -p:DebugType=none -o '$PUBLISH_OUT_WIN'; \
   vpk pack -u L2TrackerCompanion -v $VERSION -p '$PUBLISH_OUT_WIN' -e L2TrackerCompanion.exe -o '$RELEASES_OUT_WIN'"
