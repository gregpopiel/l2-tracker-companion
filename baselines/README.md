# Baselines

Reference output for the `scripts/ocr-*.sh` tools, one file per stage:

| File | Stage | Compared by |
| :--- | :--- | :--- |
| `tesseract-farm.tsv` | XP and Adena from the dialog | `scripts/ocr-farm.sh` |
| `tesseract-playtime.tsv` | Play time | By hand: `scripts/ocr-playtime.sh` writes `_playtime.tsv` and does not read this file |
| `tesseract-lamps.tsv` | The four Magic Lamp XP figures | `scripts/ocr-lamps.sh` |
| `tesseract-location.tsv` | The minimap's location header | `scripts/ocr-location.sh` |

They were produced by the website's Tesseract-based reader (the lab the website's screenshot import came from) over a fixed set of 41 Play Report screenshots. The app itself reads with `Windows.Media.Ocr`; the baselines are the yardstick it is measured against, not its output.

- **The screenshots are not in this repository.** They are real player captures and are kept locally; the scripts take the folder as an argument. Without them the scripts cannot run, but the baselines still show what the expected values were.
- **Rows are keyed by the screenshots' original file names** (the first column), which are Polish (`Zrzut ekranu …`, a Polish Windows screenshot name). Do not rename them: the scripts match baseline rows to images by name.
- The header comment of each file says it was generated from `/tmp/perrow-baseline.log` on 2026-09-02: that was a temporary file on the machine that made them, not something to look for.
- A change in a stage's result against its baseline is either a regression or an improvement; judge it by looking at the image, then update the baseline on purpose.
