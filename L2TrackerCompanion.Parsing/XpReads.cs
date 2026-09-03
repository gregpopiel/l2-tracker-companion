using System.Globalization;

namespace L2TrackerCompanion.Parsing;

/// <summary>
/// Combine the two XP reads (token band vs micro-crop). They fail in
/// different halves of the number: tokens mangle the leading group, the
/// crop smears the trailing groups. Splice by where they disagree rather
/// than picking one figure wholesale.
/// </summary>
/// <remarks>
/// A splice is a <em>repair</em> of a read the two sources could not agree
/// on, so <see cref="CombineDetailed"/> reports it alongside the value.
/// Snapshot saves surface that as a warning instead of silently trusting the
/// repaired figure — see <see cref="SaveGate"/>.
/// </remarks>
public static class XpReads
{
    public static long? Combine(long? fromTokens, long? fromCrop)
        => CombineDetailed(fromTokens, fromCrop).Value;

    public static XpCombineResult CombineDetailed(long? fromTokens, long? fromCrop)
    {
        if (fromTokens is null)
        {
            return new XpCombineResult(fromCrop, false, false, false);
        }

        if (fromCrop is null)
        {
            return new XpCombineResult(fromTokens, false, false, false);
        }

        if (fromTokens == fromCrop)
        {
            return new XpCombineResult(fromTokens, false, false, false);
        }

        var tokenDigits = fromTokens.Value.ToString(CultureInfo.InvariantCulture);
        var cropDigits = fromCrop.Value.ToString(CultureInfo.InvariantCulture);
        if (tokenDigits.Length != cropDigits.Length)
        {
            return new XpCombineResult(fromCrop, Disagreed: true, Spliced: false, MagnitudeMismatch: true);
        }

        var lead = Math.Max(1, tokenDigits.Length - 6);
        if (tokenDigits.AsSpan(0, lead).SequenceEqual(cropDigits.AsSpan(0, lead)))
        {
            return new XpCombineResult(fromTokens, Disagreed: true, Spliced: false, MagnitudeMismatch: false);
        }

        var spliced = long.Parse(
            string.Concat(cropDigits.AsSpan(0, lead), tokenDigits.AsSpan(lead)),
            CultureInfo.InvariantCulture);
        return new XpCombineResult(spliced, Disagreed: true, Spliced: true, MagnitudeMismatch: false);
    }
}

/// <param name="Value">The figure to use.</param>
/// <param name="Disagreed">Both sources parsed, and they did not match.</param>
/// <param name="Spliced">
/// <paramref name="Value"/> is a hybrid of both sources rather than either one.
/// </param>
/// <param name="MagnitudeMismatch">
/// The two sources disagreed on how many digits the figure has. Dropping or
/// adding a digit is the failure mode that would corrupt a saved log by a
/// factor of ten, so this one is never repaired — it blocks the save.
/// </param>
public readonly record struct XpCombineResult(
    long? Value,
    bool Disagreed,
    bool Spliced,
    bool MagnitudeMismatch);
