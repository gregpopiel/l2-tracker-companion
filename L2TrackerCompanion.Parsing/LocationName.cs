using System.Globalization;
using System.Text;

namespace L2TrackerCompanion.Parsing;

/// <summary>
/// Whether two minimap labels are the same place, loosely enough to ignore
/// OCR garble and tightly enough to keep a real move.
/// </summary>
/// <remarks>
/// OCR garbles glyphs inside a word. It does not swap in a different word or
/// change how many words the label has, so the discriminator is per token,
/// not the whole string: <c>prägon Villey (east)</c> and
/// <c>Dragon Valley (west)</c> are both distance 2 from
/// <c>Dragon Valley (east)</c>, and only the first is the same place.
/// </remarks>
public static class LocationName
{
    public static string Normalize(string value)
    {
        ArgumentNullException.ThrowIfNull(value);

        var decomposed = value.Normalize(NormalizationForm.FormD);
        var stripped = new StringBuilder(decomposed.Length);
        foreach (var ch in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(ch) != UnicodeCategory.NonSpacingMark)
            {
                stripped.Append(ch);
            }
        }

        var collapsed = new StringBuilder(stripped.Length);
        var pendingSpace = false;
        foreach (var ch in stripped.ToString().ToLowerInvariant())
        {
            if (char.IsAsciiLetterOrDigit(ch))
            {
                if (pendingSpace && collapsed.Length > 0)
                {
                    collapsed.Append(' ');
                }

                pendingSpace = false;
                collapsed.Append(FoldLookAlike(ch));
            }
            else
            {
                pendingSpace = true;
            }
        }

        return collapsed.ToString();
    }

    /// <summary>
    /// Blank names match nothing, including another blank — the same rule
    /// <c>SpotMatch.SameName</c> uses for an exact spot match.
    /// </summary>
    public static bool SamePlace(string? left, string? right)
    {
        if (string.IsNullOrWhiteSpace(left) || string.IsNullOrWhiteSpace(right))
        {
            return false;
        }

        var a = Normalize(left);
        var b = Normalize(right);
        if (a.Length == 0 || b.Length == 0)
        {
            return false;
        }

        if (string.Equals(a, b, StringComparison.Ordinal))
        {
            return true;
        }

        // OCR splitting or merging a word ("DragonValley" / "Dragon Valley").
        if (string.Equals(WithoutSpaces(a), WithoutSpaces(b), StringComparison.Ordinal))
        {
            return true;
        }

        var aTokens = a.Split(' ');
        var bTokens = b.Split(' ');
        if (aTokens.Length != bTokens.Length)
        {
            return false;
        }

        for (var i = 0; i < aTokens.Length; i++)
        {
            if (!NearVariant(aTokens[i], bTokens[i]))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Inverse direction of <see cref="DigitFold"/>, applied after lowercasing.
    /// Not a bijection: DigitFold maps <c>B</c> to 8 and <c>b</c> to 6, and
    /// <c>G</c> to 6 and <c>g</c> to 9, so several digits fold back to one letter.
    /// </summary>
    private static char FoldLookAlike(char ch) => ch switch
    {
        '0' => 'o',
        '1' => 'l',
        '5' => 's',
        '8' => 'b',
        '6' => 'g',
        '2' => 'z',
        '7' => 't',
        '9' => 'g',
        _ => ch,
    };

    private static string WithoutSpaces(string value) => value.Replace(" ", "", StringComparison.Ordinal);

    private static bool NearVariant(string left, string right)
    {
        if (string.Equals(left, right, StringComparison.Ordinal))
        {
            return true;
        }

        // A one- or two-character token is a direction or a number. Garble
        // that rewrites it is a different place, not a noisy glyph.
        if (left.Length < 3 || right.Length < 3)
        {
            return false;
        }

        // Length 3–4 keeps one edit, so east/west stays a real move.
        // Length 5–8 may differ by two; a longer word by three. A flat
        // allowance of three rewrites most of a short word.
        var longer = Math.Max(left.Length, right.Length);
        var limit = longer >= 9 ? 3 : longer >= 5 ? 2 : 1;
        return Levenshtein(left, right) <= limit;
    }

    private static int Levenshtein(string left, string right)
    {
        var previous = new int[right.Length + 1];
        var current = new int[right.Length + 1];
        for (var column = 0; column <= right.Length; column++)
        {
            previous[column] = column;
        }

        for (var row = 1; row <= left.Length; row++)
        {
            current[0] = row;
            for (var column = 1; column <= right.Length; column++)
            {
                var cost = left[row - 1] == right[column - 1] ? 0 : 1;
                current[column] = Math.Min(
                    Math.Min(current[column - 1] + 1, previous[column] + 1),
                    previous[column - 1] + cost);
            }

            (previous, current) = (current, previous);
        }

        return previous[right.Length];
    }
}
