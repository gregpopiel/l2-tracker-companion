using System.Globalization;
using System.Text.RegularExpressions;

namespace L2TrackerCompanion.Parsing;

/// <summary>
/// Parse an in-game XP/Adena/lamp-XP figure from OCR tokens or a single
/// figure line. Groups are summed (<c>1M 165K 47</c> → 1,165,047), never
/// concatenated, so a stray space Tesseract inserted between two real groups
/// is not closed up into one wrong number.
/// </summary>
/// <remarks>
/// Lamp counts, per-lamp scale solve, and the <c>N pc(s).</c> checksum are
/// deliberately not ported — production reads the printed XP figures only.
/// </remarks>
public static partial class GameNumber
{
    private static readonly Dictionary<char, long> Magnitudes = new()
    {
        ['B'] = 1_000_000_000,
        ['M'] = 1_000_000,
        ['K'] = 1_000,
    };

    [GeneratedRegex(@"[A-JL-Za-jl-z]", RegexOptions.CultureInvariant)]
    private static partial Regex LeftoverLetters();

    [GeneratedRegex(@"[^0-9BMK]", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex NonFigureChars();

    [GeneratedRegex(@"^(?:(\d+)B)?(?:(\d+)M)?(?:(\d+)K)?(\d*)$", RegexOptions.CultureInvariant)]
    private static partial Regex FigureLineShape();

    /// <summary>
    /// Token-wise parse. Whether the dialog's <c>1M 165K 47</c> arrives as
    /// three tokens or as <c>1M165K</c> + <c>47.</c> is the engine's choice;
    /// a token carrying more than one magnitude group is split back apart.
    /// Returns <see langword="null"/> rather than a partial sum — a plausible
    /// wrong number is worse than unread.
    /// </summary>
    public static long? Parse(params string[] tokens) => Parse((IEnumerable<string>)tokens);

    public static long? Parse(IEnumerable<string> tokens)
    {
        ArgumentNullException.ThrowIfNull(tokens);

        var segments = new List<Segment>();
        foreach (var raw in tokens)
        {
            var parsed = ParseToken(raw);
            if (parsed.Status is TokenStatus.Orphan)
            {
                var split = SplitMagnitudeGroups(raw);
                if (split is null)
                {
                    return null;
                }

                segments.AddRange(split);
                continue;
            }

            if (parsed.Status is TokenStatus.Skip)
            {
                var split = SplitMagnitudeGroups(raw);
                if (split is null)
                {
                    continue;
                }

                segments.AddRange(split);
                continue;
            }

            segments.Add(parsed.Segment);
        }

        if (segments.Count == 0)
        {
            return null;
        }

        for (var i = 1; i < segments.Count; i++)
        {
            if (segments[i].Scale >= segments[i - 1].Scale)
            {
                return null;
            }

            if (segments[i].Value > 999)
            {
                return null;
            }
        }

        long total = 0;
        foreach (var segment in segments)
        {
            total += segment.Value * segment.Scale;
        }

        return total;
    }

    /// <summary>
    /// One tight line holding exactly one figure. Closes a stray space
    /// <em>inside</em> a group (Tesseract split <c>751K</c> into <c>75</c> +
    /// <c>1K</c>; token-wise that is unrecoverable). Any letter surviving the
    /// fold that isn't a <c>B</c>/<c>M</c>/<c>K</c> suffix refuses the read —
    /// otherwise <c>garbage</c> would fold to 969.
    /// </summary>
    public static long? ParseLine(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        var folded = DigitFold.ApplyExceptB(text);
        var withoutSuffixLetters = Regex.Replace(folded, "[BMK]", string.Empty, RegexOptions.IgnoreCase);
        if (LeftoverLetters().IsMatch(withoutSuffixLetters))
        {
            return null;
        }

        var compact = NonFigureChars().Replace(folded, string.Empty).ToUpperInvariant();
        var match = FigureLineShape().Match(compact);
        if (!match.Success)
        {
            return null;
        }

        var billions = match.Groups[1].Value;
        var millions = match.Groups[2].Value;
        var thousands = match.Groups[3].Value;
        var units = match.Groups[4].Value;
        var groups = new[] { billions, millions, thousands, units }
            .Where(group => group.Length > 0)
            .ToArray();
        if (groups.Length == 0)
        {
            return null;
        }

        if (groups.Skip(1).Any(group => group.Length > 3))
        {
            return null;
        }

        return ParseGroup(billions) * 1_000_000_000
            + ParseGroup(millions) * 1_000_000
            + ParseGroup(thousands) * 1_000
            + ParseGroup(units);
    }

    private static long ParseGroup(string digits) =>
        digits.Length == 0 ? 0 : long.Parse(digits, CultureInfo.InvariantCulture);

    private static TokenParse ParseToken(string raw)
    {
        var trimmed = StripTrailingSeparators(raw);
        char? suffix = null;
        var digitPart = trimmed;
        if (trimmed.Length > 0)
        {
            var last = trimmed[^1];
            if (last is 'B' or 'M' or 'K')
            {
                suffix = last;
                digitPart = trimmed[..^1];
            }
        }

        if (digitPart.Length == 0)
        {
            return suffix is not null ? TokenParse.Orphan : TokenParse.Skip;
        }

        var folded = DigitFold.Apply(digitPart);
        if (!folded.All(char.IsAsciiDigit))
        {
            return suffix is not null ? TokenParse.Orphan : TokenParse.Skip;
        }

        var value = long.Parse(folded, CultureInfo.InvariantCulture);
        var scale = suffix is { } s ? Magnitudes[s] : 1;
        return TokenParse.Value(value, scale);
    }

    private static List<Segment>? SplitMagnitudeGroups(string raw)
    {
        var trimmed = StripTrailingSeparators(raw);
        var pieces = new List<string>();
        var start = 0;
        for (var i = 0; i < trimmed.Length; i++)
        {
            if (trimmed[i] is 'B' or 'M' or 'K')
            {
                pieces.Add(trimmed[start..(i + 1)]);
                start = i + 1;
            }
        }

        if (start < trimmed.Length)
        {
            pieces.Add(trimmed[start..]);
        }

        if (pieces.Count < 2)
        {
            return null;
        }

        var parsed = new List<Segment>(pieces.Count);
        foreach (var piece in pieces)
        {
            var token = ParseToken(piece);
            if (token.Status is not TokenStatus.Value)
            {
                return null;
            }

            parsed.Add(token.Segment);
        }

        return parsed;
    }

    private static string StripTrailingSeparators(string raw)
    {
        var end = raw.Length;
        while (end > 0 && raw[end - 1] is '.' or ',')
        {
            end--;
        }

        return end == raw.Length ? raw : raw[..end];
    }

    private enum TokenStatus
    {
        Value,
        Orphan,
        Skip,
    }

    private readonly record struct Segment(long Value, long Scale);

    private readonly record struct TokenParse(TokenStatus Status, Segment Segment)
    {
        public static TokenParse Orphan { get; } = new(TokenStatus.Orphan, default);

        public static TokenParse Skip { get; } = new(TokenStatus.Skip, default);

        public static TokenParse Value(long value, long scale) =>
            new(TokenStatus.Value, new Segment(value, scale));
    }
}
