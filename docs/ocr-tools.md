# Headless OCR tools and baselines

Part of the L2Tracker Companion's developer notes (see the README). Each script runs the same code as the app on a screenshot and writes its output under `%LOCALAPPDATA%\L2TrackerCompanion\`. Those marked **Windows required** need the Windows OCR engine; from WSL they shell out to Windows.

**Capture output:** `capture.png` is written to `%LOCALAPPDATA%\L2TrackerCompanion\capture.png` (e.g. `C:\Users\<you>\AppData\Local\L2TrackerCompanion\capture.png`), not beside the build output – so the path stays the same whether you launch from WSL, PowerShell, or a published `.exe`.

**OCR word dump:** `Parse last capture` / `Parse a PNG...` in the window, or headless:

```bash
./scripts/ocr-dump.sh /path/to/screenshot.png
```

Writes `%LOCALAPPDATA%\L2TrackerCompanion\ocr-words.txt` (words + bounding boxes, no parsing). From Windows:

```bash
dotnet run --project L2TrackerCompanion.OcrDump -- path\to\screenshot.png
```

**OCR batch dump:** all top-level PNGs of a folder of Play Report screenshots (the first argument; the default is the maintainer's own workspace layout, and the screenshots themselves are not in this repository, since they are real player captures):

```bash
./scripts/ocr-batch.sh
```

Writes `%LOCALAPPDATA%\L2TrackerCompanion\ocr-poc-dumps\` – one `.txt` per image plus `_summary.tsv`. The app stays on `Windows.Media.Ocr` on purpose; the `baselines/` files the scripts compare against are the output of the website's Tesseract-based reader (see `baselines/README.md`).

**Parsers:** WinRT-free `net8.0` library (`L2TrackerCompanion.Parsing`) – digit look-alike fold, magnitude-group sum, play-time line, `/1000`. No OCR types. From WSL:

```bash
./scripts/test-parsing.sh
```

From Windows: `dotnet test L2TrackerCompanion.Parsing.Tests`.

**Dialog crop:** locate "Report" (fallback topmost "Characters"), crop with equal 550px left/right margins, second OCR pass. Over the test set:

```bash
./scripts/ocr-crop.sh
```

Writes `%LOCALAPPDATA%\L2TrackerCompanion\ocr-poc-crops\` – one crop PNG + `.txt` per image plus `_summary.tsv`. **Windows required.**

**Farm fields:** XP + Adena from the dialog crop. Token bands around the `adena` unit word, XP splice, Adena fallback crop.

```bash
./scripts/ocr-farm.sh
```

Writes `%LOCALAPPDATA%\L2TrackerCompanion\ocr-poc-farm\` and compares to `baselines/tesseract-farm.tsv`. **Windows required.**

**Play time:** dual-read of the duration line (tokens + micro-crop). Contradiction or hours>23 / minutes>59 refuses the read.

```bash
./scripts/ocr-playtime.sh
```

Writes `%LOCALAPPDATA%\L2TrackerCompanion\ocr-poc-playtime\` (including `_playtime.tsv`). Unlike the other stages it does not read a baseline file: `baselines/tesseract-playtime.tsv` is the reference to compare `_playtime.tsv` against by hand. **Windows required.**

**Lamp table XP:** 3× table crop from row-name anchors + row pitch, re-locate rows, read the four XP cells. All-or-none. Sum must not exceed dialog XP. A collapsed Magic Lamp panel is `lampPanelClosed`, not a failed read.

```bash
./scripts/ocr-lamps.sh
```

Writes `%LOCALAPPDATA%\L2TrackerCompanion\ocr-poc-lamps\` and compares to `baselines/tesseract-lamps.tsv`. **Windows required.**

**Location hint:** minimap zone header, off the same full-image pass that locates the dialog. Width/position gates; at least two words. Dialog-only crops return nothing rather than a single-word guess.

```bash
./scripts/ocr-location.sh
```

Writes `%LOCALAPPDATA%\L2TrackerCompanion\ocr-poc-location\` and compares to `baselines/tesseract-location.tsv`. **Windows required.**

**One-shot parse:** one PNG → XP, Adena, play time, four lamp XP figures, `lampXpRead` / `lampPanelClosed`, location hint. **Capture once** in the WPF window captures then parses; **Parse a PNG...** does the same without the game.

```bash
./scripts/ocr-parse.sh /path/to/screenshot.png
```

Writes debug crops under `%LOCALAPPDATA%\L2TrackerCompanion\ocr-poc-parse\`. **Windows required.**
