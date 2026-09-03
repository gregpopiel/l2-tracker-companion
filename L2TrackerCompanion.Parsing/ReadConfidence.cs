namespace L2TrackerCompanion.Parsing;

/// <summary>
/// In-frame agreement flags for a single Play Report parse.
/// </summary>
/// <remarks>
/// OCR of an unchanged screen is deterministic: a player who has stopped
/// farming produces byte-identical frames, so repeating a read over time
/// re-confirms nothing — the same misread simply recurs. Trust therefore has
/// to come from two <em>independent</em> extraction paths of the same frame
/// disagreeing or not, which is what these flags record. <see cref="PlayTime"/>
/// already worked this way (contradiction refuses the read); XP and Adena did
/// not, and now do.
/// </remarks>
/// <param name="XpFromTokens">The token-band figure, kept for the save preview.</param>
/// <param name="XpFromCrop">The micro-crop figure, kept for the save preview.</param>
/// <param name="AdenaFromTokens">The token-band figure, kept for the save preview.</param>
/// <param name="AdenaFromCrop">The fallback-crop figure, kept for the save preview.</param>
public sealed record ReadConfidence(
    bool XpDisagreed,
    bool XpSpliced,
    bool XpMagnitudeMismatch,
    bool AdenaDisagreed,
    bool PlayTimeDisagreed,
    long? XpFromTokens = null,
    long? XpFromCrop = null,
    long? AdenaFromTokens = null,
    long? AdenaFromCrop = null)
{
    /// <summary>Every field agreed with itself (or had only one source).</summary>
    public static ReadConfidence Trusted { get; } = new(false, false, false, false, false);

    /// <summary>Any field whose two reads contradicted each other.</summary>
    public bool AnyDisagreement => XpDisagreed || AdenaDisagreed || PlayTimeDisagreed;

    /// <summary>
    /// "token read A, crop read B" for a disputed field, or null when the two
    /// figures were not both available. Naming the numbers is the point: the
    /// player has the Play Report on screen and can settle it at a glance,
    /// which no heuristic here can do.
    /// </summary>
    public string? DescribeXpDispute() => Describe(XpFromTokens, XpFromCrop);

    public string? DescribeAdenaDispute() => Describe(AdenaFromTokens, AdenaFromCrop);

    private static string? Describe(long? fromTokens, long? fromCrop)
    {
        if (fromTokens is null || fromCrop is null)
        {
            return null;
        }

        var inv = System.Globalization.CultureInfo.InvariantCulture;
        return $"token read {fromTokens.Value.ToString("N0", inv)}, "
            + $"crop read {fromCrop.Value.ToString("N0", inv)}";
    }
}
