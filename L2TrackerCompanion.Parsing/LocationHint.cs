namespace L2TrackerCompanion.Parsing;

/// <summary>
/// Minimap location header, read off the same full-image word list that
/// locates the dialog — no extra OCR pass. Present only on a genuine
/// desktop capture with the HUD in frame; dialog-only crops return
/// nothing rather than a single-word guess that could misfile a session.
/// </summary>
/// <remarks>
/// Gates are the measured ones from <c>screenshotOcr.js</c>. Windows.Media.Ocr
/// has no per-word confidence, so the Tesseract <c>minConfidence: 60</c>
/// floor is omitted — compass-ring fragments ("em" at 15%, "me" at 48%)
/// are still dropped by the two-word line rule, which is what kept a
/// lone nameplate ("Dragon") from being reported as a zone.
/// </remarks>
public static class LocationHint
{
    public const int MinImageWidth = 900;

    public const double MaxTopFraction = 0.08;

    public const double MaxRightGapFraction = 0.25;

    public const double LineTolerancePx = 10;

    public static string? Read(IEnumerable<WordBox> words, int imageWidth, int imageHeight)
    {
        ArgumentNullException.ThrowIfNull(words);
        if (imageWidth < MinImageWidth || imageHeight <= 0)
        {
            return null;
        }

        var maxTop = imageHeight * MaxTopFraction;
        var maxRightGap = imageWidth * MaxRightGapFraction;
        var candidates = words
            .Where(w => w.Top < maxTop
                && (imageWidth - (w.Left + w.Width)) < maxRightGap
                && w.Text.Any(char.IsAsciiLetter))
            .OrderBy(w => w.Top)
            .ToList();
        if (candidates.Count == 0)
        {
            return null;
        }

        var lines = new List<List<WordBox>>();
        foreach (var word in candidates)
        {
            var line = lines.FirstOrDefault(l => Math.Abs(l[0].Top - word.Top) <= LineTolerancePx);
            if (line is null)
            {
                lines.Add([word]);
            }
            else
            {
                line.Add(word);
            }
        }

        var best = lines
            .Where(l => l.Count >= 2)
            .OrderByDescending(l => l.Count)
            .FirstOrDefault();
        if (best is null)
        {
            return null;
        }

        return string.Join(" ", best.OrderBy(w => w.Left).Select(w => w.Text));
    }
}
