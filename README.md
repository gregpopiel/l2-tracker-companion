# L2Tracker Companion

Windows desktop app for [L2Tracker](https://l2tracker.cc), a Lineage II farm tracker. While you farm it looks at the game's in-game **Play Report** panel about every 10 seconds, reads the figures with the text recognition built into Windows, and, when you press Save, adds the session (XP, Adena, Magic Lamps, play time) to your L2Tracker account through the website's API. What it does and does not do is explained for players at <https://l2tracker.cc/companion>.

This repository is the desktop app only. The website and its API are separate projects.

## Stack

| Piece | Choice |
| :--- | :--- |
| Runtime | .NET 8, self-contained `win-x64` (`net8.0-windows10.0.22621.0`, minimum Windows 10 build 19041) |
| UI | WPF |
| Screen capture | `Windows.Graphics.Capture` |
| OCR (Optical Character Recognition) | `Windows.Media.Ocr`, built into Windows |
| Local storage | SQLite, DPAPI-encrypted token file |
| Packaging and updates | [Velopack](https://velopack.io): installer and portable zip, auto-update from this repo's GitHub Releases |

## Requirements

- Windows 10 or later
- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0) for development (the published app is self-contained)

## Run it

From Windows (PowerShell or CMD):

```bash
dotnet build
dotnet run --project L2TrackerCompanion
```

From WSL (Windows Subsystem for Linux), with the repo checked out under `/home/...` and the game on Windows, do **not** use Linux `dotnet run`: WPF and `Windows.Graphics.Capture` need the Windows host. The scripts shell out to Windows `dotnet` for you:

```bash
./scripts/run.sh
```

A single window titled **L2Tracker Companion** opens on a sign-in screen. Copy your token with the **Token** button on l2tracker.cc (in the header, or Settings → Companion App) and paste it in. Then pick a character, press **Start tracking**, reset the Play Report in the game when you begin a run, and press **Save** when you are done. How the app decides what to save is in `docs/behavior.md`.

Tests (all three test projects), from Windows `dotnet test L2TrackerCompanion.sln`, or from WSL:

```bash
./scripts/test-parsing.sh
```

## Where the app keeps its files

Everything is under `%LOCALAPPDATA%\L2TrackerCompanion\`, whether the app is launched from WSL, PowerShell or an installed build:

| File | What |
| :--- | :--- |
| `session.db` | SQLite snapshots of the current session |
| `auth.bin` | The sign-in token, encrypted for the current Windows user |
| `options.txt` | `user` or `debug` mode |
| `api-base-url.txt` | Optional override of the API address (default `https://l2tracker.cc`); the app has no control for it |
| `capture.png`, `ocr-*` folders | Debug output of the capture and the headless tools |

## Scripts

All are `bash` scripts in `scripts/` that run the Windows tooling from WSL; none needs `chmod`.

| Script | What it does |
| :--- | :--- |
| `run.sh` | Runs the app |
| `test-parsing.sh` | Runs the solution's tests |
| `auth.sh` | Headless sign-in checks: `--token <jwt>`, `--status`, `--garbage`, `--spots`, `--http-smoke` (see `docs/account.md`) |
| `save.sh` | Posts the last verified reading as a farm log (see `docs/behavior.md`) |
| `ocr-dump.sh`, `ocr-batch.sh`, `ocr-crop.sh`, `ocr-farm.sh`, `ocr-playtime.sh`, `ocr-lamps.sh`, `ocr-location.sh`, `ocr-parse.sh` | Run the reading pipeline stage by stage, or whole, over screenshots and compare with `baselines/` (see `docs/ocr-tools.md`). **Windows required** |
| `publish.sh`, `release.sh` | Build and publish a release (see `docs/release.md`) |

## Releasing

Bump `<Version>` in `L2TrackerCompanion/L2TrackerCompanion.csproj`, run `./scripts/publish.sh`, then `./scripts/release.sh`. Installed apps pick up the new release by themselves and never restart without the user's click. Details, and what to attach and not attach, are in `docs/release.md`.

## More

| File | Covers |
| :--- | :--- |
| `docs/behavior.md` | Polling, warm-up, reset detection, the save gate, spot following, live rates |
| `docs/account.md` | Sign-in, the desktop-access gate, settings, the API calls it makes |
| `docs/ocr-tools.md` | The headless OCR tools and their baselines |
| `docs/release.md` | Building and publishing a release, auto-update |
| `baselines/README.md` | What the files in `baselines/` are |

See `LICENSE` for the terms.
