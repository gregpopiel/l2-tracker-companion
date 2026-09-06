namespace L2TrackerCompanion.Api;

/// <summary>
/// Which spot a Save should attach to when the picker may be empty.
/// </summary>
/// <remarks>
/// A manually chosen row always wins. An empty picker may still save when this
/// read's hint exactly names a spot the user owns, when the location hint is
/// already a stable unique name, or when that name can be created under World.
/// Location stability itself is decided elsewhere.
///
/// The exact-name shortcut deliberately outranks every stability rule: matching
/// a closed vocabulary is a stronger signal than reads agreeing with each other,
/// and it is the only path that works at all when minimap OCR never repeats a
/// spelling. It can only match, never create — see <see cref="Evaluate"/>.
/// Creating still requires the window, and so does the current-hint agreement
/// check (the current tick, or the last verified frame when Save is holding).
/// A loaded spot list is required throughout — null spots is "not loaded",
/// not "none".
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

        // A single read that exactly names one spot the user already owns is
        // stronger evidence than five reads agreeing with each other: the spot
        // list is a closed vocabulary, and OCR noise garbles a name rather than
        // turning it into a different valid one. Minimap OCR of a zone label is
        // erratic enough that the window can otherwise never settle at all.
        //
        // Deliberately only ever *matches*. A name that is not already a spot
        // still has to earn the window below, because creating one from a
        // single read would put an OCR misreading into the account permanently.
        if (SpotMatch.ExactName(currentHint, spots) is { } owned)
        {
            return SpotResolveDecision.UseExisting(owned);
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

    /// <summary>
    /// The location name a read is evidence for: a hint that exactly names one
    /// spot the user owns (good on its own), otherwise the settled window's
    /// name. Null when neither holds — a garbled hint that matches nothing is
    /// evidence of nothing, and must not raise a warning.
    /// </summary>
    public static string? DetectedName(
        string? currentHint,
        string? stableHint,
        IEnumerable<SpotInfo>? spots)
        => SpotMatch.ExactName(currentHint, spots)?.Name
            ?? (string.IsNullOrWhiteSpace(stableHint) ? null : stableHint.Trim());
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

    /// <param name="sampleCount">
    /// Non-empty hints gathered. Reaching <paramref name="windowSize"/> while
    /// still unstable is not "collecting" — it is a full window whose reads
    /// disagree, which needs saying, because an unexplained "(5/5)" reads as a
    /// finished progress bar and leaves the player waiting for nothing.
    /// </param>
    /// <param name="majorityCount">
    /// How many of that window agree — only ever quoted at a full window.
    /// <see cref="LocationStability"/> does not compute a majority below
    /// <paramref name="windowSize"/> and leaves this at 0 there, so printing
    /// it for a partial window would claim that none of the reads agree, which
    /// cannot be true of even a single read.
    /// </param>
    /// <param name="tracking">
    /// Whether the poll loop is still running, i.e. whether more reads are on
    /// their way. Advice to keep waiting is only true while it is. This never
    /// returns an empty string for a blocked kind: the caller owns the decision
    /// to stay silent (see MainWindow's ShowSpotResolveHint, which does that
    /// only before a session exists at all), because the line under Save blanks
    /// itself on the assumption that this slot carried the reason.
    /// </param>
    public string Hint(int sampleCount, int majorityCount, int windowSize, bool tracking) => Kind switch
    {
        SpotResolveKind.UseExisting => $"Save will use existing spot: {Name}.",
        SpotResolveKind.CreateWorld => $"Save will create a new World spot: {Name}.",
        SpotResolveKind.Ambiguous =>
            $"Multiple spots match \"{Name}\" — pick one.",
        SpotResolveKind.MissingWorld =>
            "The World area was not found. Pick a spot, or add spots on the website.",
        SpotResolveKind.Unstable => tracking
            ? sampleCount < windowSize
                ? $"Pick a spot, or keep tracking until Location is stable ({sampleCount}/{windowSize})."
                : "Pick a spot — Location keeps reading differently, so it never settles "
                    + $"(best {majorityCount} of {windowSize} agree)."
            : sampleCount == 0
                ? "Pick a spot — Location was never readable this session."
                : sampleCount < windowSize
                    // No majority to quote here: the window never filled, so
                    // none was computed. Say how short it fell instead.
                    ? $"Pick a spot — tracking stopped with {sampleCount} of {windowSize} "
                        + "Location reads, so it never settled."
                    : "Pick a spot — Location kept reading differently, so it never settled "
                        + $"(best {majorityCount} of {windowSize} agree).",
        SpotResolveKind.SpotsNotLoaded => "Spots have not loaded yet.",
        SpotResolveKind.CurrentMismatch => tracking
            ? $"This read's Location is not \"{Name}\" — pick a spot, or wait for a consistent read."
            : $"This read's Location is not \"{Name}\" — pick a spot.",
        _ => string.Empty,
    };
}
