using System.Globalization;

namespace L2TrackerCompanion.Parsing;

/// <summary>
/// Combine the two XP reads (token band vs micro-crop). They fail in
/// different halves of the number: tokens mangle the leading group, the
/// crop smears the trailing groups. Splice by where they disagree rather
/// than picking one figure wholesale.
/// </summary>
public static class XpReads
{
    public static long? Combine(long? fromTokens, long? fromCrop)
    {
        if (fromTokens is null)
        {
            return fromCrop;
        }

        if (fromCrop is null)
        {
            return fromTokens;
        }

        if (fromTokens == fromCrop)
        {
            return fromTokens;
        }

        var tokenDigits = fromTokens.Value.ToString(CultureInfo.InvariantCulture);
        var cropDigits = fromCrop.Value.ToString(CultureInfo.InvariantCulture);
        if (tokenDigits.Length != cropDigits.Length)
        {
            return fromCrop;
        }

        var lead = Math.Max(1, tokenDigits.Length - 6);
        if (tokenDigits.AsSpan(0, lead).SequenceEqual(cropDigits.AsSpan(0, lead)))
        {
            return fromTokens;
        }

        return long.Parse(
            string.Concat(cropDigits.AsSpan(0, lead), tokenDigits.AsSpan(lead)),
            CultureInfo.InvariantCulture);
    }
}
