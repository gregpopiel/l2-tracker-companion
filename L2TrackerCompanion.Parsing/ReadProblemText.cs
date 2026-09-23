namespace L2TrackerCompanion.Parsing;

/// <summary>
/// The sentence on the read-problem banner: what is wrong with the frame
/// just read, why Save passed over it, and anything further about the frame
/// that would be posted.
/// </summary>
public static class ReadProblemText
{
    public const string Separator = " · ";

    /// <summary>
    /// Join the sources into one banner sentence, dropping blanks and
    /// identical repeats, or null when nothing is left.
    /// </summary>
    /// <remarks>
    /// Dedup is ordinal and exact: "Game not running." must not swallow a
    /// different sentence about the stored read (a spliced-XP figure could
    /// then be posted with nothing on screen questioning it). A hold reason
    /// that merely restates the current-frame problem is dropped, so a later
    /// extra fact (the held frame's issue) is not prefixed with the same
    /// defect twice.
    /// </remarks>
    public static string? Compose(params string?[] parts)
    {
        ArgumentNullException.ThrowIfNull(parts);

        List<string>? kept = null;
        foreach (var part in parts)
        {
            var value = BlankToNull(part);
            if (value is null)
            {
                continue;
            }

            kept ??= [];
            if (kept.Exists(existing => string.Equals(existing, value, StringComparison.Ordinal)))
            {
                continue;
            }

            kept.Add(value);
        }

        return kept is null || kept.Count == 0
            ? null
            : string.Join(Separator, kept);
    }

    private static string? BlankToNull(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value;
}
