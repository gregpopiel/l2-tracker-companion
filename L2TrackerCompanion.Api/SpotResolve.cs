namespace L2TrackerCompanion.Api;

/// <summary>
/// Which spot a Save should attach to when the picker may be empty.
/// </summary>
/// <remarks>
/// A manually chosen row always wins. An empty picker may still save when the
/// location hint is already a stable unique name, or when that name can be
/// created under World. Location stability itself is decided elsewhere.
/// Auto-resolve also requires the read being saved to name that same
/// hint (the current tick, or the last verified frame when Save is holding),
/// and a loaded spot list — null spots is "not loaded", not "none".
/// </remarks>
public static class SpotResolve
{
    public static SpotResolveDecision Evaluate(
        SpotInfo? selected,
        string? stableHint,
        string? currentHint,
        IEnumerable<SpotInfo>? spots,
        bool spotsLoaded,
        AreaInfo? worldArea)
    {
        if (selected is not null && selected.Id > 0)
        {
            return SpotResolveDecision.UseSelected(selected);
        }

        if (!spotsLoaded)
        {
            return SpotResolveDecision.Blocked(SpotResolveKind.SpotsNotLoaded);
        }

        if (string.IsNullOrWhiteSpace(stableHint))
        {
            return SpotResolveDecision.Blocked(SpotResolveKind.Unstable);
        }

        var name = stableHint.Trim();
        if (!SpotMatch.SameName(name, currentHint))
        {
            return SpotResolveDecision.Blocked(SpotResolveKind.CurrentMismatch, name);
        }

        var matches = SpotMatch.ExactNames(name, spots);
        if (matches.Count > 1)
        {
            return SpotResolveDecision.Blocked(SpotResolveKind.Ambiguous, name);
        }

        if (matches.Count == 1)
        {
            return SpotResolveDecision.UseExisting(matches[0]);
        }

        if (worldArea is null || worldArea.Id <= 0)
        {
            return SpotResolveDecision.Blocked(SpotResolveKind.MissingWorld, name);
        }

        return SpotResolveDecision.CreateWorld(name, worldArea);
    }
}

public enum SpotResolveKind
{
    UseSelected,
    UseExisting,
    CreateWorld,
    Unstable,
    Ambiguous,
    MissingWorld,
    SpotsNotLoaded,
    CurrentMismatch,
}

public sealed record SpotResolveDecision(
    SpotResolveKind Kind,
    SpotInfo? Spot,
    string? Name,
    AreaInfo? WorldArea)
{
    public bool CanSave => Kind is SpotResolveKind.UseSelected
        or SpotResolveKind.UseExisting
        or SpotResolveKind.CreateWorld;

    public static SpotResolveDecision UseSelected(SpotInfo spot)
        => new(SpotResolveKind.UseSelected, spot, spot.Name, null);

    public static SpotResolveDecision UseExisting(SpotInfo spot)
        => new(SpotResolveKind.UseExisting, spot, spot.Name, null);

    public static SpotResolveDecision CreateWorld(string name, AreaInfo worldArea)
        => new(SpotResolveKind.CreateWorld, null, name, worldArea);

    public static SpotResolveDecision Blocked(SpotResolveKind kind, string? name = null)
        => new(kind, null, name, null);

    public string Hint(int unstableSampleCount, int windowSize) => Kind switch
    {
        SpotResolveKind.UseExisting => $"Save will use existing spot: {Name}.",
        SpotResolveKind.CreateWorld => $"Save will create a new World spot: {Name}.",
        SpotResolveKind.Ambiguous =>
            $"Multiple spots match \"{Name}\" — pick one.",
        SpotResolveKind.MissingWorld =>
            "The World area was not found. Pick a spot, or add spots on the website.",
        SpotResolveKind.Unstable =>
            $"Pick a spot, or keep tracking until Location is stable ({unstableSampleCount}/{windowSize}).",
        SpotResolveKind.SpotsNotLoaded => "Spots have not loaded yet.",
        SpotResolveKind.CurrentMismatch =>
            $"This read's Location is not \"{Name}\" — pick a spot, or wait for a consistent read.",
        _ => string.Empty,
    };
}
