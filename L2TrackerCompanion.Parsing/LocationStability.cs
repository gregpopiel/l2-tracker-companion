namespace L2TrackerCompanion.Parsing;

/// <summary>
/// Whether a run of minimap location hints is stable enough to stand in for
/// a manually chosen spot.
/// </summary>
/// <remarks>
/// Save itself still posts a single agreeing frame (see <see cref="SaveGate"/>),
/// falling back to the last such frame when the current tick is rejected.
/// This gate is only for auto-resolving the spot: a one-off OCR of the HUD
/// header is too thin to create or attach a spot, so we require the last
/// <see cref="WindowSize"/> non-empty hints to agree at least
/// <see cref="MinMajority"/> times (80%). Empty reads are skipped so a
/// briefly occluded minimap does not poison the window.
/// </remarks>
public static class LocationStability
{
    public const int WindowSize = 5;

    public const int MinMajority = 4;

    public static LocationStabilityDecision Evaluate(IEnumerable<string?> hints)
    {
        ArgumentNullException.ThrowIfNull(hints);

        var nonEmpty = hints
            .Select(TrimOrNull)
            .Where(hint => hint is not null)
            .Select(hint => hint!)
            .ToList();

        if (nonEmpty.Count < WindowSize)
        {
            return LocationStabilityDecision.Unstable(nonEmpty.Count);
        }

        var window = nonEmpty.TakeLast(WindowSize).ToList();
        var majority = window
            .GroupBy(hint => hint, StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(group => group.Count())
            .First();

        if (majority.Count() < MinMajority)
        {
            return LocationStabilityDecision.Unstable(WindowSize, majority.Count());
        }

        var canonical = majority
            .GroupBy(hint => hint, StringComparer.Ordinal)
            .OrderByDescending(group => group.Count())
            .First()
            .Key;

        return LocationStabilityDecision.Stable(canonical, WindowSize, majority.Count());
    }

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

public sealed record LocationStabilityDecision(
    bool IsStable,
    string? CanonicalName,
    int SampleCount,
    int MajorityCount)
{
    public static LocationStabilityDecision Unstable(int sampleCount, int majorityCount = 0)
        => new(false, null, sampleCount, majorityCount);

    public static LocationStabilityDecision Stable(string canonicalName, int sampleCount, int majorityCount)
        => new(true, canonicalName, sampleCount, majorityCount);
}
