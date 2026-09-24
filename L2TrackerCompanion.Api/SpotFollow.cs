using L2TrackerCompanion.Parsing;

namespace L2TrackerCompanion.Api;

/// <summary>
/// Whether the player has taken over the spot picker.
/// </summary>
/// <remarks>
/// A manual pick sets it. <see cref="Clear"/>, a character change,
/// sign-out, a reloaded spot list, or a successful save calls <see cref="Reset"/>, so
/// detection can fill the picker again. The picker value itself
/// (a spot, or empty) lives with the control.
/// </remarks>
public sealed class SpotFollowState
{
    public bool UserChose { get; private set; }

    public void NoteUserChoice() => UserChose = true;

    /// <summary>
    /// What the Clear button does. Drops a manual choice so the next
    /// <see cref="SpotTarget.Decide"/> can fill the picker from the reading.
    /// </summary>
    public void Clear() => Reset();

    public void Reset() => UserChose = false;
}

/// <summary>
/// The spot a Save should attach to, and the one line under the picker.
/// </summary>
public static class SpotTarget
{
    public static SpotTargetDecision Decide(
        bool userChose,
        SpotInfo? selected,
        string? currentHint,
        string? settledName,
        IEnumerable<SpotInfo>? spots,
        bool spotsLoaded,
        AreaInfo? worldArea,
        bool tracking,
        bool hasReads)
    {
        if (userChose)
        {
            return selected is { Id: > 0 }
                ? SpotTargetDecision.Chosen(selected)
                : SpotTargetDecision.None();
        }

        if (!hasReads)
        {
            return SpotTargetDecision.None();
        }

        if (!spotsLoaded)
        {
            return SpotTargetDecision.Blocked("Spots have not loaded yet.");
        }

        var matchesNow = SpotMatch.ExactNames(currentHint, spots);
        if (matchesNow.Count > 1)
        {
            if (selected is { Id: > 0 })
            {
                return SpotTargetDecision.Following(selected, null);
            }

            return SpotTargetDecision.Blocked($"Multiple spots match \"{currentHint!.Trim()}\" – pick one.");
        }

        var owned = matchesNow.Count == 1 ? matchesNow[0] : null;
        if (owned is not null)
        {
            if (selected is { Id: > 0 } && LocationName.SamePlace(selected.Name, owned.Name))
            {
                return SpotTargetDecision.Following(selected, null);
            }

            var switched = selected is { Id: > 0 } && selected.Id != owned.Id;
            return SpotTargetDecision.Following(
                owned,
                switched ? $"Spot switched to \"{owned.Name}\"." : null);
        }

        if (selected is { Id: > 0 })
        {
            var ambiguous = AmbiguousSuffix(currentHint, settledName, spots, selected);
            if (ambiguous is not null)
            {
                return ambiguous;
            }

            return SpotTargetDecision.Following(selected, null);
        }

        // The settled name is the recent run. The hint is the frame Save would
        // post, which can be an older held read. Creating or attaching from
        // the run is only valid when that frame is still the same place.
        if (string.IsNullOrWhiteSpace(settledName))
        {
            return SpotTargetDecision.Blocked(PickSpot(tracking));
        }

        if (!LocationName.SamePlace(currentHint, settledName))
        {
            return SpotTargetDecision.Blocked(
                "The reading to save is from another place. Pick a spot.");
        }

        var name = settledName.Trim();
        var samePlace = (spots ?? [])
            .Where(spot => LocationName.SamePlace(spot.Name, name))
            .ToList();
        if (samePlace.Count > 1)
        {
            return SpotTargetDecision.Blocked($"Multiple spots match \"{name}\" – pick one.");
        }

        if (samePlace.Count == 1)
        {
            return SpotTargetDecision.Following(samePlace[0], null);
        }

        // The official minimap name, plus the player's own trailing words
        // ("Hot Springs" against "Hot Springs Top"). An equal name was
        // already taken above. Several suffixes preselect the first name
        // and still ask for a pick.
        var withSuffix = Suffixes(name, spots);
        if (withSuffix.Count > 1)
        {
            return FirstSuffix(withSuffix, name);
        }

        if (withSuffix.Count == 1)
        {
            return SpotTargetDecision.Following(withSuffix[0], null);
        }

        if (worldArea is null || worldArea.Id <= 0)
        {
            return SpotTargetDecision.Blocked(
                "The World area was not found. Pick a spot, or add spots on the website.");
        }

        return SpotTargetDecision.Create(
            name,
            worldArea,
            $"Save will create a new World spot: {name}.");
    }

    /// <summary>
    /// The alphabetical suffix, with the pick message, when the selected
    /// spot is already one of several. An unrelated spot returns null so
    /// the caller keeps it quietly.
    /// </summary>
    private static SpotTargetDecision? AmbiguousSuffix(
        string? currentHint,
        string? settledName,
        IEnumerable<SpotInfo>? spots,
        SpotInfo selected)
    {
        if (string.IsNullOrWhiteSpace(settledName) || !LocationName.SamePlace(currentHint, settledName))
        {
            return null;
        }

        var name = settledName.Trim();
        var withSuffix = Suffixes(name, spots);
        if (withSuffix.Count < 2 || withSuffix.All(spot => spot.Id != selected.Id))
        {
            return null;
        }

        return FirstSuffix(withSuffix, name);
    }

    private static List<SpotInfo> Suffixes(string official, IEnumerable<SpotInfo>? spots)
        => (spots ?? [])
            .Where(spot => HasTrailingWords(spot.Name, official))
            .ToList();

    private static SpotTargetDecision FirstSuffix(IReadOnlyList<SpotInfo> spots, string official)
    {
        var first = spots.OrderBy(spot => spot.Name, StringComparer.OrdinalIgnoreCase).First();
        return SpotTargetDecision.Following(first, $"Multiple spots match \"{official}\" – pick one.");
    }

    private static bool HasTrailingWords(string? spotName, string official)
    {
        if (string.IsNullOrWhiteSpace(spotName))
        {
            return false;
        }

        var spotWords = spotName.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var officialWords = official.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (spotWords.Length <= officialWords.Length)
        {
            return false;
        }

        for (var i = 0; i < officialWords.Length; i++)
        {
            if (!string.Equals(spotWords[i], officialWords[i], StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
        }

        return true;
    }

    private static string PickSpot(bool tracking)
        => tracking
            ? "Pick a spot, or keep tracking until the location name settles."
            : "Pick a spot.";

}

public sealed record SpotTargetDecision(
    SpotInfo? Spot,
    string? CreateName,
    AreaInfo? WorldArea,
    bool CanSave,
    string? Hint)
{
    public static SpotTargetDecision Chosen(SpotInfo spot)
        => new(spot, null, null, true, null);

    public static SpotTargetDecision Following(SpotInfo spot, string? hint)
        => new(spot, null, null, true, hint);

    public static SpotTargetDecision Create(string name, AreaInfo worldArea, string hint)
        => new(null, name, worldArea, true, hint);

    public static SpotTargetDecision Blocked(string hint)
        => new(null, null, null, false, hint);

    public static SpotTargetDecision None()
        => new(null, null, null, false, null);
}
