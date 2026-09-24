namespace L2TrackerCompanion.Parsing;

/// <summary>
/// Whether recent minimap hints are one place, settled enough to name a new spot.
/// </summary>
/// <remarks>
/// The last <see cref="RunLength"/> non-empty hints must be the same place
/// (<see cref="LocationName.SamePlace"/>), every pair of them, not only each
/// against the first. Two reads can drift from the first in opposite
/// directions and still be a different place from each other. Blank reads are
/// skipped so a briefly occluded minimap does not break the run. One hint
/// that is a different place unsettles the name. The spelling returned is the
/// one that occurs most often in the run, ignoring case. A tie keeps the later
/// spelling, so one leading artifact does not become the name.
/// </remarks>
public static class LocationStability
{
    public const int RunLength = 4;

    public static string? SettledName(IEnumerable<string?> hints)
    {
        ArgumentNullException.ThrowIfNull(hints);

        var nonEmpty = hints
            .Select(TrimOrNull)
            .Where(hint => hint is not null)
            .Select(hint => hint!)
            .ToList();

        if (nonEmpty.Count < RunLength)
        {
            return null;
        }

        var run = nonEmpty.TakeLast(RunLength).ToList();
        for (var i = 0; i < run.Count; i++)
        {
            for (var j = i + 1; j < run.Count; j++)
            {
                if (!LocationName.SamePlace(run[i], run[j]))
                {
                    return null;
                }
            }
        }

        return MajoritySpelling(run);
    }

    private static string MajoritySpelling(IReadOnlyList<string> run)
        => run
            .Select((hint, index) => (hint, index))
            .GroupBy(item => item.hint, StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(group => group.Count())
            .ThenByDescending(group => group.Max(item => item.index))
            .First()
            .GroupBy(item => item.hint, StringComparer.Ordinal)
            .OrderByDescending(group => group.Count())
            .ThenByDescending(group => group.Max(item => item.index))
            .First()
            .Key;

    private static string? TrimOrNull(string? hint)
    {
        if (string.IsNullOrWhiteSpace(hint))
        {
            return null;
        }

        var trimmed = hint.Trim();
        return trimmed.Length == 0 ? null : trimmed;
    }
}
