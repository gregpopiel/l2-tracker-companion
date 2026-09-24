using System.Globalization;
using System.Text;

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

        return Clean(string.Join(" ", best.OrderBy(w => w.Left).Select(w => w.Text)));
    }

    /// <summary>
    /// Drop glyphs a minimap dot paints over the label. A zone name is
    /// ASCII letters, digits, spaces, and <c>( ) ' -</c>. An accent folds
    /// onto its base letter (<c>ä</c> to <c>a</c>). A mark between words
    /// becomes a space, so <c>Hot.Springs</c> stays two words, and a trailing
    /// one (<c>Hot Springs.</c>) trims away.
    /// </summary>
    public static string? Clean(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        var decomposed = text.Normalize(NormalizationForm.FormD);
        var buffer = new StringBuilder(decomposed.Length);
        var pendingSpace = false;
        foreach (var ch in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(ch) == UnicodeCategory.NonSpacingMark)
            {
                continue;
            }

            var mapped = MapAllowed(ch);
            if (mapped == ' ')
            {
                pendingSpace = buffer.Length > 0;
                continue;
            }

            if (mapped == '\0')
            {
                continue;
            }

            if (pendingSpace)
            {
                buffer.Append(' ');
            }

            pendingSpace = false;
            buffer.Append(mapped);
        }

        return buffer.Length == 0 ? null : buffer.ToString();
    }

    /// <summary>
    /// <c>' '</c> is a gap. <c>'\0'</c> drops a leftover non-ASCII letter
    /// without splitting the word.
    /// </summary>
    private static char MapAllowed(char ch)
    {
        if (char.IsAsciiLetterOrDigit(ch))
        {
            return ch;
        }

        return ch switch
        {
            '(' or ')' or '\'' or '-' => ch,
            '\u2018' or '\u2019' => '\'',
            '\u2013' or '\u2014' => '-',
            _ when char.IsLetter(ch) => '\0',
            _ => ' ',
        };
    }
}
