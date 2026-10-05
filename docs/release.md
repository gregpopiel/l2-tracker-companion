# Releasing and auto-update

Part of the L2Tracker Companion's developer notes (see the README).

## Building a release

Self-contained `win-x64` app, packaged with [Velopack](https://velopack.io) as an installer (`L2Tracker-Setup.exe`) and a portable zip (`L2Tracker-Portable.zip`). Both auto-update against this repo's GitHub Releases feed. The installer puts the app in `%LocalAppData%\L2Tracker\current\` with a Start Menu shortcut; the zip unpacks the same layout into whatever folder you choose. Velopack needs individual (non-bundled) files to compute delta patches – there is no single-file `PublishSingleFile` `.exe`. Requires the `vpk` global dotnet tool once per machine: `dotnet tool install -g vpk`. Output is gitignored (`publish-output/`, `releases/`).

```bash
./scripts/publish.sh
```

From Windows:

```bash
dotnet publish L2TrackerCompanion/L2TrackerCompanion.csproj -c Release -r win-x64 --self-contained -p:DebugType=none -o publish-output
vpk pack -u L2Tracker --packTitle "L2Tracker Companion" -v <version-from-csproj> -p publish-output -e L2TrackerCompanion.exe -o releases
# then drop the channel suffix from the two user downloads (vpk names them {id}-win-*):
#   L2Tracker-win-Setup.exe    → L2Tracker-Setup.exe
#   L2Tracker-win-Portable.zip → L2Tracker-Portable.zip
```

## Publishing it

1. Bump `<Version>` in `L2TrackerCompanion/L2TrackerCompanion.csproj`: that is what a running app compares against to detect an update, and it names the release (`v<Version>`).
2. Run `./scripts/publish.sh` (above). It builds into `publish-output/` and packs into `releases/`.
3. Run `./scripts/release.sh`. It needs the GitHub CLI (`gh`), signed in with write access to this repository, and creates the release `v<Version>`, titled `L2Tracker Companion v<Version>`, with exactly the files users and the updater need: `L2Tracker-Setup.exe`, `L2Tracker-Portable.zip`, `L2Tracker-<Version>-full.nupkg` and `releases.win.json`. It refuses to run if one of them is missing.

Notes on what is and is not attached:

- `vpk pack` also writes `RELEASES` (a Squirrel leftover) and `assets.win.json` (only `vpk upload` uses it). The updater reads neither; do not attach them.
- Do not attach a lone `L2TrackerCompanion.exe`: that old single-file publish cannot check the update feed.
- The release goes to this repository's GitHub Releases, not to a server. The website's download buttons point at `releases/latest/download/…`, so a new release becomes the download immediately.
- `release.sh` publishes with GitHub's generated notes, which are only a "Full Changelog" link. Publishing a release also announces it on the project's Discord with the notes as they are at that moment, and editing them afterwards does not announce again; if players should get a description, write it before publishing.

**Auto-update:** the app checks this repo's public GitHub Releases feed on startup and every 4 hours while running (`UpdateService.cs`, stops re-checking once a download is pending), downloading silently in the background – a failed check/download is traced (`Trace.WriteLine`) and simply retried next cycle, never surfaced to the user. It never restarts on its own: a downloaded update shows a status-bar button ("Update available – restart to install") that the user clicks when ready. That click goes through the same unsaved-session gate as **Sign out** (`_saveInFlight`, `ConfirmDiscardSession` – "Restarting to update" phrasing) before restarting, since the app may be mid-poll or holding an unsaved farm-log delta; the button disables itself once clicked, and a failed apply (locked file, AV interference) re-enables it with a status message instead of crashing. Both the Setup.exe install and the portable zip update this way (Velopack `IsInstalled`); `dotnet run` does not check the feed.
