using System.Globalization;
using System.Text.RegularExpressions;

namespace L2TrackerCompanion.Parsing;

/// <summary>
/// Parse the Play Report's fixed-shape duration line
/// <c>&lt;d&gt; d. &lt;h&gt; h. &lt;m&gt; min.</c> into minutes.
/// </summary>
/// <remarks>
/// The raw OCR pass repeatedly misreads <c>0 d.</c> as a single token
/// <c>04d.</c> / <c>04.</c>, which a naive parse treats as 4 days (a real
/// screenshot came back as 6432 minutes against a true 672). The day count
/// is the digit <em>before</em> the marker: <c>04.</c> is 0 days, while a
/// genuine four-day session renders <c>4 d.</c> and still parses as 4. The
/// game never zero-pads the count, so a leading zero is never a legitimate
/// day value. Hours &gt; 23 or minutes &gt; 59 refuse the read rather than
/// trust an implausible result. WinOCR-specific: <c>time</c> is missing on
/// most of the POC set, so locate falls back to <c>Total</c>; a leading
/// <c>O</c> on this line is folded to 0 (<c>O d.</c> / <c>O h.</c>) without
/// running <see cref="DigitFold"/> (that would turn <c>min</c> into <c>m1n</c>).
/// </remarks>
public static partial class PlayTime
{
    [GeneratedRegex(
        @"(\d{1,2})\s*[d4][.,]?\s*(\d{1,2})\s*h[.,]?\s*(\d{1,2})\s*m",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex DurationLine();

    public static int? ParseMinutes(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        // WinOCR emits O for 0 on this line ("O h."). Do not run DigitFold:
        // that maps i→1 and would turn "min" into "m1n".
        text = text.Replace('O', '0').Replace('o', '0');

        var match = DurationLine().Match(text);
        if (!match.Success)
        {
            return null;
        }

        var dayDigits = match.Groups[1].Value;
        var days = dayDigits.Length > 1 && dayDigits[0] == '0'
            ? dayDigits[0] - '0'
            : int.Parse(dayDigits, CultureInfo.InvariantCulture);
        var hours = int.Parse(match.Groups[2].Value, CultureInfo.InvariantCulture);
        var mins = int.Parse(match.Groups[3].Value, CultureInfo.InvariantCulture);
        if (hours > 23 || mins > 59)
        {
            return null;
        }

        return days * 1440 + hours * 60 + mins;
    }

    /// <summary>
    /// Fixed-pixel band below the "time" label, not a multiple of the
    /// (noisy) glyph height. Value line sits ~14–17px below the label;
    /// 45px still misses the Reset button further down.
    /// </summary>
    public const int ValueBandMinBelowPx = 3;

    public const int ValueBandMaxBelowPx = 45;

    public const int ValueBandMaxDxPx = 160;

    public const int CropPadX = 8;

    public const int CropPadY = 6;

    public const int EnhanceTargetHeight = 140;

    /// <summary>Value line starts ~14–17px below the label top; 8px clears the glyphs.</summary>
    public const int StripBelowLabelPx = 8;

    public const int StripMaxBelowPx = 40;

    public const int StripFromTotalLeftPadPx = 8;

    public const int StripFromTotalWidthPx = 110;

    public const int StripFromTimeLeftPx = 90;

    public const int StripFromTimeRightPadPx = 16;

    [GeneratedRegex(
        @"^(?:d|h|min)[.,]?$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex UnitFragment();

    public static bool IsTimeWord(string text)
        => string.Equals(text, "time", StringComparison.OrdinalIgnoreCase);

    public static bool IsTotalWord(string text)
        => string.Equals(text, "Total", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Tokens that can belong to the value line. "Total" (the other word of
    /// the label) is purely alphabetic and must stay out — its box sometimes
    /// reports a top a few pixels below "time" and would otherwise drag the
    /// crop up into the label.
    /// </summary>
    public static bool IsValueToken(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        return text.Any(char.IsAsciiDigit) || UnitFragment().IsMatch(text);
    }

    /// <summary>
    /// The "time" of "Total play time", falling back to "Total". WinOCR
    /// sees <c>time</c> on only ~1/3 of the POC set but <c>Total</c> on
    /// all 41. Tesseract.js used highest-conf <c>time</c>; without
    /// confidence, the candidate with the most value-line tokens below it
    /// wins, preferring <c>time</c> when the counts tie.
    /// </summary>
    public static WordBox? PickTimeAnchor(IEnumerable<WordBox> words)
    {
        ArgumentNullException.ThrowIfNull(words);
        var list = words as IList<WordBox> ?? words.ToList();
        var candidates = list.Where(w => IsTimeWord(w.Text) || IsTotalWord(w.Text)).ToList();
        if (candidates.Count == 0)
        {
            return null;
        }

        return candidates
            .OrderByDescending(c => ValueTokens(list, c).Count)
            .ThenByDescending(c => IsTimeWord(c.Text) ? 1 : 0)
            .ThenByDescending(c => c.Left)
            .First();
    }

    public static IReadOnlyList<WordBox> ValueTokens(IEnumerable<WordBox> words, WordBox anchor)
    {
        ArgumentNullException.ThrowIfNull(words);
        ArgumentNullException.ThrowIfNull(anchor);
        return words
            .Where(w => w.Top > anchor.Top + ValueBandMinBelowPx
                && w.Top < anchor.Top + ValueBandMaxBelowPx
                && Math.Abs(w.Left - anchor.Left) < ValueBandMaxDxPx
                && IsValueToken(w.Text))
            .OrderBy(w => w.Left)
            .ToList();
    }

    public static PlayTimeRead ReadTokens(IEnumerable<WordBox> words)
    {
        ArgumentNullException.ThrowIfNull(words);
        var list = words as IList<WordBox> ?? words.ToList();
        var anchor = PickTimeAnchor(list);
        if (anchor is null)
        {
            return PlayTimeRead.Empty;
        }

        var tokens = ValueTokens(list, anchor);
        int? fromTokens = tokens.Count == 0
            ? null
            : ParseMinutes(string.Join(' ', tokens.Select(w => w.Text)));
        return new PlayTimeRead(anchor, tokens, fromTokens);
    }

    public static CropRect ValueCrop(IReadOnlyList<WordBox> tokens, int imageWidth, int imageHeight)
    {
        ArgumentNullException.ThrowIfNull(tokens);
        if (tokens.Count == 0)
        {
            return default;
        }

        var left = Math.Max(0, (int)Math.Round(tokens.Min(w => w.Left) - CropPadX, MidpointRounding.AwayFromZero));
        var top = Math.Max(0, (int)Math.Round(tokens.Min(w => w.Top) - CropPadY, MidpointRounding.AwayFromZero));
        var right = Math.Min(imageWidth, (int)Math.Round(tokens.Max(w => w.Left + w.Width) + CropPadX, MidpointRounding.AwayFromZero));
        var bottom = Math.Min(imageHeight, (int)Math.Round(tokens.Max(w => w.Top + w.Height) + CropPadY, MidpointRounding.AwayFromZero));
        return new CropRect(left, top, Math.Max(0, right - left), Math.Max(0, bottom - top));
    }

    /// <summary>
    /// Fixed strip under the label. Token-union crops miss the leading
    /// <c>0 d.</c> when WinOCR only tokenizes <c>h.</c>/<c>min.</c>.
    /// </summary>
    public static CropRect LabelStrip(WordBox anchor, int imageWidth, int imageHeight)
    {
        ArgumentNullException.ThrowIfNull(anchor);
        double rawLeft;
        double rawRight;
        if (IsTimeWord(anchor.Text))
        {
            rawLeft = anchor.Left - StripFromTimeLeftPx;
            rawRight = anchor.Left + anchor.Width + StripFromTimeRightPadPx;
        }
        else
        {
            rawLeft = anchor.Left - StripFromTotalLeftPadPx;
            rawRight = anchor.Left + StripFromTotalWidthPx;
        }

        var left = Math.Max(0, (int)Math.Round(rawLeft, MidpointRounding.AwayFromZero));
        var top = Math.Max(0, (int)Math.Round(anchor.Top + StripBelowLabelPx, MidpointRounding.AwayFromZero));
        var right = Math.Min(imageWidth, (int)Math.Round(rawRight, MidpointRounding.AwayFromZero));
        var bottom = Math.Min(imageHeight, (int)Math.Round(anchor.Top + StripMaxBelowPx, MidpointRounding.AwayFromZero));
        return new CropRect(left, top, Math.Max(0, right - left), Math.Max(0, bottom - top));
    }

    public static CropRect Union(CropRect a, CropRect b)
    {
        if (a.IsEmpty)
        {
            return b;
        }

        if (b.IsEmpty)
        {
            return a;
        }

        var left = Math.Min(a.Left, b.Left);
        var top = Math.Min(a.Top, b.Top);
        var right = Math.Max(a.Right, b.Right);
        var bottom = Math.Max(a.Bottom, b.Bottom);
        return new CropRect(left, top, right - left, bottom - top);
    }

    /// <summary>
    /// Always crop the label strip when an anchor exists, even if the
    /// token band is empty — WinOCR often emits no duration tokens at all.
    /// Union with the token boxes so a far-right <c>min.</c> is not clipped.
    /// </summary>
    public static CropRect CombinedValueCrop(PlayTimeRead read, int imageWidth, int imageHeight)
    {
        ArgumentNullException.ThrowIfNull(read);
        if (read.Anchor is null)
        {
            return default;
        }

        return Union(LabelStrip(read.Anchor, imageWidth, imageHeight), ValueCrop(read.ValueTokens, imageWidth, imageHeight));
    }

    /// <summary>
    /// Dual-read: crop and tokens must not contradict. Either side alone
    /// is allowed (the upscale smears this line on many shots; the tokens
    /// carry the "0 d." / "04." ambiguity <see cref="ParseMinutes"/>
    /// resolves). A disagreement is unmodelled — refuse rather than guess.
    /// </summary>
    public static int? Combine(int? fromCrop, int? fromTokens)
    {
        if (fromCrop is not null && fromTokens is not null && fromCrop != fromTokens)
        {
            return null;
        }

        return fromCrop ?? fromTokens;
    }
}

public sealed record PlayTimeRead(
    WordBox? Anchor,
    IReadOnlyList<WordBox> ValueTokens,
    int? FromTokens)
{
    public static PlayTimeRead Empty { get; } = new(null, [], null);
}
