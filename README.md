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

**Session store (plan step 14):** each successful parse (WPF or `ocr-parse.sh`) appends a snapshot to `%LOCALAPPDATA%\L2TrackerCompanion\session.db` unless monotonicity rejects it. The WPF window lists those rows. There is no **New session** button: a session begins when the player resets the Play Report **in-game**, which the app detects on its own (see *Reset detection* below). The buffer is also cleared when the app starts and whenever **Start reading** is pressed, so a stale baseline is never more than one button away from being discarded.

**Offline parsing does not touch the live session.** *Capture once* reads the panel as it is now and counts as a real reading; *OCR last capture* and *Parse a PNG…* re-read a file that may be hours old, which is indistinguishable from a fresh reset — they display their result and store nothing.

**Polling (plan step 15):** **Start reading** captures → OCR → accept-or-discard every 10s until **Stop reading**. The buttons control whether the app is looking at the screen; they do not open or close a session. A tick whose XP / Adena / play time dropped versus the last accepted snapshot is discarded (OCR misread). Lamp XP is monotonic only when both ticks had `lampXpRead`; a closed Magic Lamp panel is not a misread. A tick that finishes after Stop does not append.

**Reset detection:** because the player restarts the Play Report in-game to begin a session, the panel legitimately drops — so a drop is no longer automatically a misread. Four layers, because no single one covers every route:

1. **The client went away and came back under a new process id** (`GameProcessWatch`) — a restarted client always comes back with a zeroed panel. A changed id on its own is not enough: the app follows whichever client is in front, so with two clients open the id flips on every alt-tab, and again when one of the two is closed. The app has to have observed *no* game window at all in between.
2. **A coherent drop**: the duration went backwards *and* XP actually fell *and* Adena did not grow, with every field readable. There is deliberately **no ceiling** on how far the new duration may have advanced: whether the reset is caught in its first minute or its fifteenth depends only on when a tick happened to land, and readings stop landing for ordinary reasons (panel closed, client relogged, tracking paused). XP has to have *fallen*, not merely failed to grow — an unchanged XP beside a shorter duration is the duration line being misread.
3. **`StaleBaselineStrikes` (3) rejections in a row** against the same stored row: a real misread is transient, so several in a row mean the stored row is the stale thing. It is dropped and counting restarts.
4. **Pressing Start reading**, and starting the app, clear the buffer outright.

Anything else stays a misread and is discarded. Mistaking a misread for a reset costs a dropped buffer and nothing more — the save is built from a single frame and its lock lives in a separate table.

**Live status (plan step 16):** a traffic light on the latest parse (not only accepted snapshots). Red = unread farm field or a lamp table that is in frame but unreadable; orange = Magic Lamp panel closed; green = farm + lamps read. Missing minimap hint does not change the colour. Updates every poll tick. The card also shows live XP and Adena rates from that same parse (plan step 23).

**Auth (plan step 17):** the app opens on a **sign-in screen** and nothing else is reachable until a token validates — paste the website JWT (`localStorage` key `l2_jwt_token`). It is stored DPAPI-encrypted (`%LOCALAPPDATA%\L2TrackerCompanion\auth.bin`, current-user scope) only after `GET /api/me` and then `GET /api/characters` succeed. A token the server actually *rejects* (401/403) is deleted, not left on disk; a call that never got an answer (offline, DNS, timeout, a 5xx from the edge) keeps it, so a network blip cannot sign you out — the gate then offers **Retry with stored token** instead of making you fetch the JWT again. Pressing **Sign in** on an empty box is likewise treated as a slip and keeps it — only **Sign out** and a real rejection remove the file. Default API base is `https://l2tracker.cc`, overridden only by `%LOCALAPPDATA%\L2TrackerCompanion\api-base-url.txt` when that file exists — there is no in-app control that shows or edits it.

**Desktop access gate:** `/api/me` reports `desktopAppEnabled` for the account. When it is `false` the app refuses to sign in ("Desktop access is not enabled for this account.") and stores nothing — the same account keeps full use of the website. The flag lives in the database (`users.desktop_app_enabled`, default `true`, flipped by hand in SQL), not in the JWT, so revoking it takes effect here rather than whenever the user's 14-day token expires. `SignInAsync` and `TryRestoreAsync` both go through `ValidateAndStoreAsync`, so a revoked account is stopped on the next app launch too, not only on the next paste. There is no fallback for a backend without `/api/me` — deploy the backend first.

**Sign-in gate:** the token form is the first and only view at startup. The Session and Settings tabs are hidden until `GET /api/me` + `GET /api/characters` accept the token, and the app drops straight back to this screen on **Sign out** or if the stored token disappears mid-session. Reaching the gate stops tracking (the Stop button goes with the Session tab), and **Sign out** additionally wipes the local session store, so unsaved snapshots cannot be posted to whichever account signs in next — it asks for confirmation first whenever that store holds a savable delta. **Retry with stored token** appears whenever `auth.bin` still exists, and re-runs the same validation without a paste.

**Settings tab:** **Options** (User / Debug) and **Account** (signed-in status, **Sign out**) live here, not on the Session tab — this is the only place the mode is switched. Token entry is on the sign-in screen, not here; the API base URL has no control on either screen (see above). User mode hides capture dumps, parse tools, and session inspect on the Session tab. The choice is stored in `%LOCALAPPDATA%\L2TrackerCompanion\options.txt` (`user` / `debug`).

```bash
chmod +x scripts/auth.sh   # once
./scripts/auth.sh --token '<jwt>'
./scripts/auth.sh --garbage   # must print that nothing is on disk
./scripts/auth.sh --status
```

A single window titled **L2 Tracker Companion** should open.

**Character + spot pickers (plan step 18):** after a valid token, the window lists characters from `GET /api/characters` and, on character change, spots from `GET /api/spots?characterId=`. Character is required. Spot may be left empty when Location is stable (see step 21). `% Bonus` prefills from `GET /api/settings` (`defaultBonus`; lamp values and `defaultMinutes` are ignored). Live rates use that same GET's `rateUnit` (`hour` or `minute`). If the GET fails, bonus is schema default 25 and rates are schema default `hour`, with a hint saying why. Session minutes on Save are wall-clock, not a form field. Headless:

```bash
./scripts/auth.sh --spots
```

**Native HTTP smoke (plan step 19):** `HttpClient` GET against production with `Authorization: Bearer` and **no** `Origin` header. Confirms CORS/auth middleware does not reject a desktop client. Uses the stored JWT:

```bash
./scripts/auth.sh --http-smoke
```

Must print `HTTP 200` and `JSON: yes` for `/api/characters` and `/api/settings`.

**Save session (plan step 20):** Save POSTs **the latest single reading** as a `FarmLog` (`xpFarmed` / `adena` / lamp XP **in thousands**, `minutes` taken from the Play Report's own duration, `% Bonus` = `acquiredXpSp`). The panel is already a complete session record, so nothing is subtracted and the wall clock is not consulted — a log can be saved long after the farming stopped, and the companion need not have been running for it.

Save is gated by `SaveGate`, which trusts a frame on **in-frame agreement** rather than repetition — OCR of an unchanged screen is deterministic, so re-reading a static panel would only reproduce the same misread. It blocks when: the play-time dual-read contradicted itself, the two Adena reads disagreed, the two XP reads disagreed on digit count, XP / Adena / play time are unread, play time is 0, the Magic Lamp panel is closed or its XP column unread (no silent zeros), lamp XP exceeds dialog XP, the previous tick was a misread, or the session is still locked by an earlier save. A spliced XP figure warns (orange) but still saves — and the warning **names both competing figures** ("XP disputed — token read 4,210,400, crop read 9,210,400. Saving 9,210,400 (spliced)") so the player, who has the panel on screen, can settle it at a glance. A blocked Adena says the same, which distinguishes a dropped digit from a failed read. On 2xx the panel's play time and XP are recorded in `saved_logs` and the session is **locked**: because the panel is cumulative, a later frame still contains every minute already posted, so a second save would double-count the whole first stretch. The lock is released by a frame whose **XP** is below the saved figure. XP never falls within one run, so that is proof of a different run — at any duration, which matters because a reset nobody was watching can easily have outgrown the saved session by the time the app looks again. A shorter play time on its own does **not** release it, so a misread of the duration line cannot unlock a live session; play time is the release signal only for the degenerate case of a log saved at zero XP. Detecting a reset does **not** release it separately: the XP comparison already covers the real case, and a second, heuristic release path would only add a way to lose the lock on one misclassified frame.

`saved_logs` holds exactly one row — the current lock, not a history; keeping every save and reading an aggregate meant a short session after a long one never locked at all. It is deliberately independent of the snapshot buffer: the buffer is dropped routinely (pressing Start, app startup, a stale baseline) and none of those may re-open a session that was already posted. The lock is also mirrored in memory, so a failed write cannot let the next poll tick re-arm the button for a log already on the server. The local snapshot buffer is **not** cleared on save, and the in-game Play Report keeps counting until the player resets it. A successful Save in the WPF app **does** stop the 10s tracking loop: the lock already forbids another POST, so further OCR ticks would only nag to reset. The session-status line keeps the confirmation until **Start reading**, sign-out, or a game restart (picker changes and stray ticks no longer replace it with the lock reason). Start reading again after resetting the panel.

```bash
chmod +x scripts/save.sh   # once
./scripts/save.sh --character-id <id> [--spot-id <id>] [--bonus <n>]
```

It saves the most recent stored reading, subject to the same gate. Omit `--spot-id` to resolve from a stable Location hint (exact match, or create under World).

**Spot preselect from hint (plan step 21):** a minimap `locationHint` exact-matches a spot **name** (case-insensitive, never fuzzy, never the area label). Preselect only runs when Location is already stable (last 5 non-empty accepted hints, 4/5 the same) **and** this reading names that same hint; **Clear** or a manual choice still wins. A miss leaves the picker empty. Save with an empty picker uses that same stable name: existing exact match, or a new World spot. The current reading must still show that name (so a move in the last tick does not file under the old majority). Never auto-saves.

```bash
./scripts/ocr-parse.sh --match-hint /path/to/hud.png
```

Uses the stored JWT. Switching character in the window reloads spots.

## Publish (plan step 22, revised for auto-update)

Self-contained `win-x64` app, packaged with [Velopack](https://velopack.io) into an installer (`L2TrackerCompanion-win-Setup.exe`) rather than a portable single-file `.exe` — Velopack owns install location (`%LocalAppData%\L2TrackerCompanion\current\`), Start Menu shortcut, and in-app updates, and needs individual (non-bundled) files to compute delta patches between versions. Requires the `vpk` global dotnet tool once per machine: `dotnet tool install -g vpk`. Output is gitignored (`publish-output/`, `releases/`).

```bash
chmod +x scripts/publish.sh   # once
./scripts/publish.sh
```

From Windows:

```bash
dotnet publish L2TrackerCompanion/L2TrackerCompanion.csproj -c Release -r win-x64 --self-contained -p:DebugType=none -o publish-output
vpk pack -u L2TrackerCompanion -v <version-from-csproj> -p publish-output -e L2TrackerCompanion.exe -o releases
```

Ship the resulting `releases/L2TrackerCompanion-win-Setup.exe` (plus the `.nupkg`/delta files alongside it) as a GitHub Release on this repo (not the VPS / Docker stack) — `vpk upload github` can push them directly given a PAT with `contents:write` on this repo. Bump `<Version>` in `L2TrackerCompanion.csproj` before every release; that's what the running app compares against to detect an update.

**Auto-update:** the app checks this repo's public GitHub Releases feed on startup and every 4 hours while running (`UpdateService.cs`, stops re-checking once a download is pending), downloading silently in the background — a failed check/download is traced (`Trace.WriteLine`) and simply retried next cycle, never surfaced to the user. It never restarts on its own: a downloaded update shows a status-bar button ("Update available — restart to install") that the user clicks when ready. That click goes through the same unsaved-session gate as **Sign out** (`_saveInFlight`, `ConfirmDiscardSession` — "Restarting to update" phrasing) before restarting, since the app may be mid-poll or holding an unsaved farm-log delta; the button disables itself once clicked, and a failed apply (locked file, AV interference) re-enables it with a status message instead of crashing. Velopack only manages installs it created: existing users on the old portable `.exe` won't auto-update to the first Velopack release and need to grab `L2TrackerCompanion-win-Setup.exe` once manually.

**Live rates (plan step 23):** the live-status card (and `ocr-parse.sh` / `--parse`) shows XP and Adena rates from the **current screenshot**: raw OCR XP and Adena divided by OCR play-time minutes, rounded away from zero. The WPF Session tab uses the signed-in account's `user_settings.rate_unit` from `GET /api/settings` (`minute` → XP/min, `hour` → XP/h; Prisma default is `hour`). Settings are re-fetched when tracking starts. CLI parse output stays per minute. Display-only — Save still uses last−first / wall-clock, and these figures are not converted to thousands. Play time must be greater than 0; otherwise both rates show `(need play time)`. Unread XP still allows Adena rate and the reverse. Dialog XP includes lamps, so a Magic Lamp pop jumps the XP rate. Play Report time is whole minutes (the rate only moves when that digit or the totals change) and is the panel's own duration, not this companion session.

## Implementation plan

Step-by-step work is tracked in the parent workspace [`PLAN.md`](../PLAN.md) (sibling folder, not part of this repo).
