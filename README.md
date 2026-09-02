# L2 Tracker Companion

Windows desktop companion for the [Lineage 2 Farm Tracker](https://l2tracker.cc). Watches a running L2 client, reads the in-game Play Report panel via OCR, and submits farm sessions through the existing backend API.

This repository is the desktop app only. The web UI and API live in [`l2-tracker-frontend`](https://github.com/gregpopiel/l2-tracker-frontend) and [`l2-tracker-backend`](https://github.com/gregpopiel/l2-tracker-backend) and are deployed independently.

## Stack

| Piece | Choice |
| :--- | :--- |
| Runtime | .NET 8 (`net8.0-windows10.0.19041.0`) |
| UI | WPF |
| OCR | `Windows.Media.Ocr` (built into Windows) |
| Local storage | SQLite (`%LOCALAPPDATA%\L2TrackerCompanion\session.db`) |

## Requirements

- Windows 10 or later
- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0) (for development; the published app is self-contained)

## Local setup

### Windows (PowerShell / CMD)

```bash
dotnet build
dotnet run --project L2TrackerCompanion
```

### WSL (repo lives under `/home/...`, game runs on Windows)

Do **not** use Linux `dotnet run` — WPF and `Windows.Graphics.Capture` need the Windows host. From the repo root:

```bash
chmod +x scripts/run.sh   # once
./scripts/run.sh
```

That shells out to Windows `dotnet` with the project on `\\wsl.localhost\...`. Build-only from WSL works the same way:

```bash
powershell.exe -NoProfile -Command \
  "Set-Location -LiteralPath '$(wslpath -w "$PWD")'; dotnet build L2TrackerCompanion/L2TrackerCompanion.csproj"
```

**Capture output:** `capture.png` is written to `%LOCALAPPDATA%\L2TrackerCompanion\capture.png` (e.g. `C:\Users\<you>\AppData\Local\L2TrackerCompanion\capture.png`), not beside the build output — so the path stays the same whether you launch from WSL, PowerShell, or a published `.exe`.

**OCR word dump (plan step 5):** `OCR last capture` / `OCR a PNG...` in the window, or headless:

```bash
./scripts/ocr-dump.sh /path/to/screenshot.png
```

Writes `%LOCALAPPDATA%\L2TrackerCompanion\ocr-words.txt` (words + bounding boxes, no parsing). From Windows:

```bash
dotnet run --project L2TrackerCompanion.OcrDump -- path\to\screenshot.png
```

**OCR batch dump (plan step 6):** all top-level PNGs in the frontend POC set (`experiments/ocr-poc/images/`, skips `processed/`):

```bash
./scripts/ocr-batch.sh
```

Writes `%LOCALAPPDATA%\L2TrackerCompanion\ocr-poc-dumps\` — one `.txt` per image plus `_summary.tsv`. Stay on `Windows.Media.Ocr` for now; see `PLAN.md` "OCR engine / libraries" before adding Tesseract or PaddleOCR.

**Parsers (plan step 7):** WinRT-free `net8.0` library (`L2TrackerCompanion.Parsing`) — digit look-alike fold, magnitude-group sum, play-time line, `/1000`. No OCR types. From WSL:

```bash
chmod +x scripts/test-parsing.sh   # once
./scripts/test-parsing.sh
```

From Windows: `dotnet test L2TrackerCompanion.Parsing.Tests`.

**Dialog crop (plan step 8):** locate "Report" (fallback topmost "Characters"), crop with equal 550px left/right margins, second OCR pass. Over the 41-set:

```bash
chmod +x scripts/ocr-crop.sh   # once
./scripts/ocr-crop.sh
```

Writes `%LOCALAPPDATA%\L2TrackerCompanion\ocr-poc-crops\` — one crop PNG + `.txt` per image plus `_summary.tsv`. **Windows required.**

**Farm fields (plan step 9):** XP + Adena from the dialog crop. Token bands around the `adena` unit word, XP splice, Adena fallback crop.

```bash
./scripts/ocr-farm.sh
```

Writes `%LOCALAPPDATA%\L2TrackerCompanion\ocr-poc-farm\` and compares to `baselines/tesseract-farm.tsv`. **Windows required.**

**Play time (plan step 10):** dual-read of the duration line (tokens + micro-crop). Contradiction or hours>23 / minutes>59 refuses the read.

```bash
chmod +x scripts/ocr-playtime.sh   # once
./scripts/ocr-playtime.sh
```

Writes `%LOCALAPPDATA%\L2TrackerCompanion\ocr-poc-playtime\` and compares to `baselines/tesseract-playtime.tsv`. **Windows required.**

**Lamp table XP (plan step 11):** 3× table crop from row-name anchors + row pitch, re-locate rows, read the four XP cells. All-or-none. Sum must not exceed dialog XP. A collapsed Magic Lamp panel is `lampPanelClosed`, not a failed read.

```bash
chmod +x scripts/ocr-lamps.sh   # once
./scripts/ocr-lamps.sh
```

Writes `%LOCALAPPDATA%\L2TrackerCompanion\ocr-poc-lamps\` and compares to `baselines/tesseract-lamps.tsv`. **Windows required.**

**Location hint (plan step 12):** minimap zone header, off the same full-image pass that locates the dialog. Width/position gates; at least two words. Dialog-only crops return nothing rather than a single-word guess.

```bash
chmod +x scripts/ocr-location.sh   # once
./scripts/ocr-location.sh
```

Writes `%LOCALAPPDATA%\L2TrackerCompanion\ocr-poc-location\` and compares to `baselines/tesseract-location.tsv`. **Windows required.**

**One-shot parse (plan step 13):** one PNG → XP, Adena, play time, four lamp XP figures, `lampXpRead` / `lampPanelClosed`, location hint. **Capture once** in the WPF window captures then parses; **Parse a PNG...** does the same without the game.

```bash
chmod +x scripts/ocr-parse.sh   # once
./scripts/ocr-parse.sh /path/to/screenshot.png
```

Writes debug crops under `%LOCALAPPDATA%\L2TrackerCompanion\ocr-poc-parse\`. **Windows required.**

**Session store (plan step 14):** each successful parse (WPF or `ocr-parse.sh`) appends a snapshot to `%LOCALAPPDATA%\L2TrackerCompanion\session.db` unless monotonicity rejects it. The WPF window lists those rows; **New session** (or `./scripts/ocr-parse.sh --new-session`) wipes the file.

**Polling (plan step 15):** **Start tracking** captures → OCR → accept-or-discard every 10s until **Stop tracking**. A tick whose XP / Adena / play time dropped versus the last accepted snapshot is discarded (OCR misread). Lamp XP is monotonic only when both ticks had `lampXpRead`; a closed Magic Lamp panel is not a misread. A tick that finishes after Stop does not append.

**Live status (plan step 16):** a traffic light on the latest parse (not only accepted snapshots). Red = unread farm field or a lamp table that is in frame but unreadable; orange = Magic Lamp panel closed; green = farm + lamps read. Missing minimap hint does not change the colour. Updates every poll tick.

**Auth (plan step 17):** paste the website JWT (`localStorage` key `l2_jwt_token`). It is stored DPAPI-encrypted (`%LOCALAPPDATA%\L2TrackerCompanion\auth.bin`, current-user scope) only after `GET /api/characters` succeeds. A rejected token is deleted, not left on disk. Default API base is `https://l2tracker.cc` (editable in the window).

```bash
chmod +x scripts/auth.sh   # once
./scripts/auth.sh --token '<jwt>'
./scripts/auth.sh --garbage   # must print that nothing is on disk
./scripts/auth.sh --status
```

A single window titled **L2 Tracker Companion** should open.

**Character + spot pickers (plan step 18):** after a valid token, the window lists characters from `GET /api/characters` and, on character change, spots from `GET /api/spots?characterId=`. Save stays disabled until both are chosen (the POST is step 20). `% Bonus` / `Minutes` prefill from `GET /api/settings` (`defaultBonus` / `defaultMinutes` only — not lamp values). Headless:

```bash
./scripts/auth.sh --spots
```

**Native HTTP smoke (plan step 19):** `HttpClient` GET against production with `Authorization: Bearer` and **no** `Origin` header. Confirms CORS/auth middleware does not reject a desktop client. Uses the stored JWT:

```bash
./scripts/auth.sh --http-smoke
```

Must print `HTTP 200` and `JSON: yes` for `/api/characters` and `/api/settings`.

**Save session (plan step 20):** Save POSTs last−first accepted snapshots as a `FarmLog` (`xpFarmed` / `adena` / lamp XP **in thousands**, `minutes` = wall-clock, `% Bonus` = `acquiredXpSp`). Blocked until both pickers are chosen, there are two accepted snapshots, and lamp XP was read at both ends (no silent zeros). On 2xx the local session file is cleared.

```bash
chmod +x scripts/save.sh   # once
./scripts/save.sh --character-id <id> --spot-id <id> [--bonus <n>]
./scripts/save.sh --smoke   # posts a 1-minute 1000k-XP probe using the first character/spot
```

Uses the stored JWT. Switching character in the window reloads spots.

## Publish (later)

```bash
dotnet publish -r win-x64 --self-contained -p:PublishSingleFile=true
```

## Implementation plan

Step-by-step work is tracked in the parent workspace [`PLAN.md`](../PLAN.md) (sibling folder, not part of this repo).
